using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public partial class ProductionConsensusAggregator
{
    private async Task<string> GetForebetJsonWithSessionCore(string jsonUrl)
    {
        await flareSemaphore.WaitAsync();

        try
        {
            using var flareClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(210)
            };

            //
            // 1. Прво отвараме нормална Forebet страница преку FlareSolverr.
            //    Со ова добиваме cookies + точниот browser User-Agent.
            //
            string bootstrapUrl =
                $"https://www.forebet.com/en/football-predictions/predictions-1x2/{targetDate}";

            var payload = new
            {
                cmd = "request.get",
                url = bootstrapUrl,
                maxTimeout = 180000
            };

            string payloadJson =
                JsonConvert.SerializeObject(payload);

            Console.WriteLine(
                $"[Forebet SESSION] Bootstrap: {bootstrapUrl}");

            var flareResponse =
                await flareClient.PostAsync(
                    flareSolverrUrl,
                    new StringContent(
                        payloadJson,
                        Encoding.UTF8,
                        "application/json"));

            string flareResult =
                await flareResponse.Content.ReadAsStringAsync();

            Console.WriteLine(
                $"[Forebet SESSION] FlareSolverr HTTP {(int)flareResponse.StatusCode}");

            if (!flareResponse.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"[Forebet SESSION] FlareSolverr failed.");

                return "";
            }

            JObject root =
                JObject.Parse(flareResult);

            JObject solution =
                root["solution"] as JObject;

            if (solution == null)
            {
                Console.WriteLine(
                    "[Forebet SESSION] solution missing.");

                return "";
            }

            string userAgent =
                solution["userAgent"]?.ToString();

            JArray cookies =
                solution["cookies"] as JArray;

            Console.WriteLine(
                $"[Forebet SESSION] Cookies: {cookies?.Count ?? 0}");

            if (string.IsNullOrWhiteSpace(userAgent))
            {
                Console.WriteLine(
                    "[Forebet SESSION] UserAgent missing.");

                return "";
            }

            //
            // 2. CookieContainer
            //
            var cookieContainer = new CookieContainer();

            if (cookies != null)
            {
                foreach (var item in cookies)
                {
                    try
                    {
                        string name = item["name"]?.ToString();
                        string value = item["value"]?.ToString();

                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        cookieContainer.Add(
                            new Uri("https://www.forebet.com"),
                            new System.Net.Cookie(
                                name,
                                value ?? ""));
                    }
                    catch
                    {
                        // Игнорирај проблематична cookie.
                    }
                }
            }

            //
            // 3. HttpClient со Forebet cookies
            //
            using var handler =
                new HttpClientHandler
                {
                    UseCookies = true,
                    CookieContainer = cookieContainer,
                    AutomaticDecompression =
                        DecompressionMethods.GZip |
                        DecompressionMethods.Deflate
                };

            using var client =
                new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(60)
                };

            client.DefaultRequestHeaders
                .TryAddWithoutValidation(
                    "User-Agent",
                    userAgent);

            client.DefaultRequestHeaders
                .TryAddWithoutValidation(
                    "Accept",
                    "application/json,text/plain,*/*");

            client.DefaultRequestHeaders
                .TryAddWithoutValidation(
                    "Referer",
                    bootstrapUrl);

            client.DefaultRequestHeaders
                .TryAddWithoutValidation(
                    "X-Requested-With",
                    "XMLHttpRequest");

            Console.WriteLine(
                $"[Forebet JSON] GET {jsonUrl}");

            using var response =
                await client.GetAsync(jsonUrl);

            string content =
                await response.Content.ReadAsStringAsync();

            Console.WriteLine(
                $"[Forebet JSON] HTTP {(int)response.StatusCode}");

            Console.WriteLine(
                $"[Forebet JSON] Length: {content.Length}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    "[Forebet JSON] Request failed.");

                return "";
            }

            //
            // Safety check.
            //
            string trimmed =
                content.TrimStart();

            if (!trimmed.StartsWith("["))
            {
                Console.WriteLine(
                    "[Forebet JSON] Response is not JSON.");

                Console.WriteLine(
                    content.Substring(
                        0,
                        Math.Min(content.Length, 300)));

                return "";
            }

            return content;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Forebet SESSION ERROR] {ex}");

            return "";
        }
        finally
        {
            flareSemaphore.Release();
        }
    }

    private async Task<string> GetForebetJsonWithSession(string jsonUrl)
    {
        const int maxAttempts = 3;

        for (int attempt = 1;
            attempt <= maxAttempts;
            attempt++)
        {
            Console.WriteLine(
                $"[Forebet SESSION] Attempt {attempt}/{maxAttempts}");

            string json =
                await GetForebetJsonWithSessionCore(jsonUrl);

            if (!string.IsNullOrWhiteSpace(json))
                return json;

            if (attempt < maxAttempts)
            {
                int delaySeconds = attempt * 5;

                Console.WriteLine(
                    $"[Forebet SESSION] Retry in {delaySeconds}s...");

                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds));
            }
        }

        Console.WriteLine(
            "[Forebet SESSION] FAILED after 3 attempts.");

        return "";
    }

    
    public async Task<List<SiteMatch>> ParseForebetAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();

        try
        {
            string url =
                "https://www.forebet.com/scripts/getrs.php" +
                "?ln=en" +
                "&tp=1x2" +
                $"&in={targetDate}" +
                "&ord=0" +
                "&tz=+180";

            Console.WriteLine();
            Console.WriteLine(
                $"[Forebet JSON] Loading for {targetDate}...");

            string json = await GetForebetJsonWithSession(url);

            if (string.IsNullOrWhiteSpace(json))
            {
                Console.WriteLine("[Forebet JSON] Empty response.");
                return matches;
            }

            var root = JArray.Parse(json);

            if (root.Count == 0 ||
                root[0] == null ||
                root[0].Type != JTokenType.Array)
            {
                Console.WriteLine(
                    "[Forebet JSON] Matches array not found.");

                return matches;
            }

            var items = (JArray)root[0];

            Console.WriteLine(
                $"[Forebet JSON] Raw matches: {items.Count}");

            int wrongDate = 0;
            int invalid = 0;

            foreach (var item in items)
            {
                try
                {
                    string dateBah =
                        item["DATE_BAH"]?.ToString();

                    if (string.IsNullOrWhiteSpace(dateBah))
                    {
                        invalid++;
                        continue;
                    }

                    // Земаме само натпревари за targetDate.
                    if (!dateBah.StartsWith(
                            targetDate,
                            StringComparison.Ordinal))
                    {
                        wrongDate++;
                        continue;
                    }

                    string home =
                        HtmlEntity.DeEntitize(
                            item["HOST_NAME"]?.ToString() ?? "")
                        .Trim();

                    string away =
                        HtmlEntity.DeEntitize(
                            item["GUEST_NAME"]?.ToString() ?? "")
                        .Trim();

                    if (string.IsNullOrWhiteSpace(home) ||
                        string.IsNullOrWhiteSpace(away))
                    {
                        invalid++;
                        continue;
                    }

                    if (!int.TryParse(
                            item["Pred_1"]?.ToString(),
                            out int p1))
                    {
                        invalid++;
                        continue;
                    }

                    if (!int.TryParse(
                            item["Pred_X"]?.ToString(),
                            out int pX))
                    {
                        invalid++;
                        continue;
                    }

                    if (!int.TryParse(
                            item["Pred_2"]?.ToString(),
                            out int p2))
                    {
                        invalid++;
                        continue;
                    }

                    int maxP =
                        Math.Max(p1, Math.Max(pX, p2));

                    string tip =
                        maxP == p1 ? "1" :
                        maxP == pX ? "X" :
                        "2";

                    string homeGoals =
                        item["host_sc_pr"]?.ToString();

                    string awayGoals =
                        item["guest_sc_pr"]?.ToString();

                    string score = "-";

                    if (int.TryParse(homeGoals, out int hg) &&
                        int.TryParse(awayGoals, out int ag))
                    {
                        score = $"{hg}-{ag}";
                    }

                    matches.Add(
                        new SiteMatch
                        {
                            SiteName = "Forebet",
                            HomeTeam = home,
                            AwayTeam = away,

                            Tip = tip,
                            Prob = maxP,

                            Score = score,

                            BttsMarket =
                                GetBttsMarket(score),

                            GoalsMarket =
                                GetGoalMarket(score)
                        });
                }
                catch (Exception ex)
                {
                    invalid++;

                    Console.WriteLine(
                        $"[Forebet JSON] Row error: {ex.Message}");
                }
            }

            // Safety dedup по Forebet ID не ни треба,
            // но правиме dedup по normalized team names.
            matches = matches
                .GroupBy(x =>
                    NormalizeName(x.HomeTeam) + "|" +
                    NormalizeName(x.AwayTeam))
                .Select(x => x.First())
                .ToList();

            Console.WriteLine(
                $"[Forebet JSON] Other dates ignored: {wrongDate}");

            Console.WriteLine(
                $"[Forebet JSON] Invalid ignored: {invalid}");

            Console.WriteLine(
                $"[Forebet JSON] FINAL MATCHES: {matches.Count}");

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Forebet JSON ERROR] {ex}");

            return matches;
        }
    }
}
