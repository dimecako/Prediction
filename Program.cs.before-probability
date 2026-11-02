using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Playwright;
using System.Globalization;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using PlaywrightCookie = Microsoft.Playwright.Cookie;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.IO;

public class SiteMatch
{
    public string SiteName { get; set; }
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public string Tip { get; set; }
    public int? Prob { get; set; }
    public string Score { get; set; } = "-";
    public string Btts { get; set; } = "-";
    public string BttsMarket { get; set; } = "-";
    public string GoalsMarket { get; set; } = "-";
}

public class BacktestRecord
{
    public string Date { get; set; }

    public string Source { get; set; }

    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }

    public string PredictedResult { get; set; }
    public string PredictedBtts { get; set; }
    public string PredictedGoals { get; set; }

    public int ActualHomeGoals { get; set; }
    public int ActualAwayGoals { get; set; }

    public string ActualResult { get; set; }
    public string ActualBtts { get; set; }
    public string ActualGoals { get; set; }

    public bool CorrectResult { get; set; }
    public bool CorrectBtts { get; set; }
    public bool CorrectGoals { get; set; }
}

public class MatchSourceData
{
    public string Tip { get; set; }
    public int? Prob { get; set; }
    public string Score { get; set; } = "-";
    public string Btts { get; set; } = "-";
    public string BttsMarket { get; set; } = "-";
    public string GoalsMarket { get; set; } = "-";
}

public class UnifiedMatch
{
    public string HomeOrig { get; set; }
    public string AwayOrig { get; set; }
    public ConcurrentDictionary<string, MatchSourceData> Sources { get; set; } = new ConcurrentDictionary<string, MatchSourceData>();

    public UnifiedMatch()
    {
        Sources.TryAdd("Forebet", null);
        Sources.TryAdd("Statarea", null);
        Sources.TryAdd("PredictZ", null);
        Sources.TryAdd("WinDrawWin", null);
    }
}

public class ProductionConsensusAggregator
{
    private string targetDate;
    private string todayStr;

    private static readonly SemaphoreSlim flareSemaphore = new SemaphoreSlim(1, 1);

    private readonly string flareSolverrUrl = Environment.GetEnvironmentVariable("FLARESOLVERR_URL") ?? "http://localhost:8191/v1";

    private ConcurrentDictionary<string, UnifiedMatch> unifiedDb = new ConcurrentDictionary<string, UnifiedMatch>();

    public ProductionConsensusAggregator(string targetDate)
    {
        this.targetDate = targetDate;
        this.todayStr = DateTime.Today.ToString("yyyy-MM-dd");
    }

    private async Task<HtmlDocument> GetPageFromFlareSolverrCore(string url)
    {
        await flareSemaphore.WaitAsync();

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(210)
            };

            var payload = new
            {
                cmd = "request.get",
                url = url,
                maxTimeout = 180000
            };

            string json = JsonConvert.SerializeObject(payload);

            Console.WriteLine($"[FlareSolverr] GET {url}");

            var response = await client.PostAsync(
                flareSolverrUrl,
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"));

            string result =
                await response.Content.ReadAsStringAsync();

            Console.WriteLine(
                $"[FlareSolverr] HTTP {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"[FlareSolverr] RESPONSE: {result}");

                return null;
            }

            dynamic obj =
                JsonConvert.DeserializeObject(result);

            if (obj?.solution?.response == null)
                return null;

            string html =
                obj.solution.response.ToString();

            Console.WriteLine(
                $"[FlareSolverr] HTML length: {html.Length}");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return doc;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[FlareSolverr ERROR] {ex.Message}");

            return null;
        }
        finally
        {
            flareSemaphore.Release();
        }
    }

    
    private async Task<HtmlDocument> GetPageFromFlareSolverr(string url)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var doc = await GetPageFromFlareSolverrCore(url);

                if (doc != null)
                    return doc;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FlareSolverr] Attempt {attempt}/{maxAttempts} failed: {ex.Message}");
            }

            if (attempt < maxAttempts)
            {
                int delaySeconds = attempt * 5;

                Console.WriteLine(
                    $"[FlareSolverr] Retry in {delaySeconds}s...");

                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds));
            }
        }

        Console.WriteLine(
            $"[FlareSolverr] FAILED after {maxAttempts} attempts: {url}");

        return null;
    }
    
    private async Task<string> GetRawFromFlareSolverr(string url)
    {
        await flareSemaphore.WaitAsync();

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(210)
            };

            var payload = new
            {
                cmd = "request.get",
                url = url,
                maxTimeout = 180000
            };

            string json =
                JsonConvert.SerializeObject(payload);

            Console.WriteLine(
                $"[FlareSolverr RAW] GET {url}");

            var response = await client.PostAsync(
                flareSolverrUrl,
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"));

            string result =
                await response.Content.ReadAsStringAsync();

            Console.WriteLine(
                $"[FlareSolverr RAW] HTTP {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"[FlareSolverr RAW] RESPONSE: {result}");

                return "";
            }

            dynamic obj =
                JsonConvert.DeserializeObject(result);

            if (obj?.solution?.response == null)
                return "";

            return obj.solution.response.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[FlareSolverr RAW ERROR] {ex.Message}");

            return "";
        }
        finally
        {
            flareSemaphore.Release();
        }
    }

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

    
    private async Task<HtmlDocument> ScrapeHtmlAsync(IPage page, string url)
    {
        try
        {
            await page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });

            string html = await page.ContentAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return doc;
        }
        catch
        {
            return null;
        }
    }

    private string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        name = HtmlEntity.DeEntitize(name);

        name = RemoveDiacritics(name)
            .ToLowerInvariant()
            .Trim();

        // punctuation
        name = name
            .Replace("'", "")
            .Replace("’", "")
            .Replace(".", "")
            .Replace(",", "")
            .Replace("-", " ")
            .Replace("_", " ")
            .Replace("&", " and ");

        name = Regex.Replace(name, @"\s+", " ").Trim();

        // -------------------------------------------------
        // KNOWN TEAM ALIASES - BEFORE generic replacements
        // -------------------------------------------------

        var aliases = new Dictionary<string, string>
        {
            { "man utd", "manchester united" },
            { "man united", "manchester united" },
            { "man city", "manchester city" },

            { "wolves", "wolverhampton" },
            { "wolverhampton wanderers", "wolverhampton" },

            { "heart of midlothian", "hearts" },

            { "grasshopper club zurich", "grasshoppers" },
            { "grasshopper club", "grasshoppers" },

            { "queen of the south", "queen of south" },

            { "sparta prague", "sparta praha" },
            { "slavia prague", "slavia praha" },

            { "olympiakos piraeus", "olympiacos" },
            { "olympiakos", "olympiacos" },

            { "jagiellonia bialystok", "jagiellonia" },

            { "iraklis gerolakkou", "iraklis yerolakkos" },

            { "nk mladost zdralovi", "mladost zdralovi" },

            { "sarpsborg 08 ff", "sarpsborg 08" },

            { "cambrian and clydach", "cambrian clydach" },

            { "puskas academy", "puskas akademia" },

            { "rapid vienna", "rapid wien" },

            { "ud logrones", "logrones" },
            { "logrones cf", "logrones" },

            { "nk sesvete", "radnik sesvete" },

            { "lyngby bk", "lyngby" },

            { "rkc waalwijk", "waalwijk" },

            { "as nancy", "nancy" },

            { "queens park fc", "queens park" },

            { "ayr utd", "ayr united" },

            { "az", "az alkmaar" },

            { "psg", "paris saint germain" },

            { "inter milan", "inter" },
            { "internazionale", "inter" }
        };

        if (aliases.TryGetValue(name, out string alias))
            name = alias;

        // -------------------------------------------------
        // Generic normalization
        // -------------------------------------------------

        name = Regex.Replace(name, @"\butd\b", "united");
        name = Regex.Replace(name, @"\bst\b", "saint");

        // Women
        name = Regex.Replace(
            name,
            @"\b(women|woman)\b",
            "w",
            RegexOptions.IgnoreCase);

        name = Regex.Replace(
            name,
            @"\(\s*w\s*\)",
            " w",
            RegexOptions.IgnoreCase);

        // II -> B
        name = Regex.Replace(name, @"\bii\b", "b");

        // Club prefixes/suffixes
        name = Regex.Replace(
            name,
            @"\b(fc|fk|afc|sc|cf|ac|sv)\b",
            " ");

        // Known transliteration differences
        name = name
            .Replace("skoevde", "skovde")
            .Replace("kaspiy", "kaspij");

        name = Regex.Replace(
            name,
            @"\band\b",
            " ");

        name = Regex.Replace(
            name,
            @"^(tsv|msv|deportivo|sporting|club|real)\s+",
            "");

        name = Regex.Replace(
            name,
            @"\s+(bgc)$",
            "");

        name = Regex.Replace(name, @"\s+", " ").Trim();

        return name;
    }

    private string GetGoalMarket(string score)
    {
        if (string.IsNullOrWhiteSpace(score) || !score.Contains("-")) return "-";
        var p = score.Split('-');
        if (!int.TryParse(p[0], out int hg) || !int.TryParse(p[1], out int ag)) return "-";
        int totalGoals = hg + ag;
        if (totalGoals >= 4) return "O3.5";
        if (totalGoals >= 3) return "O2.5";
        return "U2.5";
    }

    private string GetBttsMarket(string score)
    {
        if (string.IsNullOrWhiteSpace(score) || !score.Contains("-")) return "-";
        var p = score.Split('-');
        if (!int.TryParse(p[0], out int hg) || !int.TryParse(p[1], out int ag)) return "-";
        return hg > 0 && ag > 0 ? "YES" : "NO";
    }

    private string GetResultMarket(int homeGoals, int awayGoals)
    {
        if (homeGoals > awayGoals)
            return "1";

        if (homeGoals < awayGoals)
            return "2";

        return "X";
    }

    private BacktestRecord CreateBacktestRecord(SiteMatch prediction, string date, int homeGoals, int awayGoals)
    {
        string actualScore =
            $"{homeGoals}-{awayGoals}";

        string actualResult =
            GetResultMarket(homeGoals, awayGoals);

        string actualBtts =
            GetBttsMarket(actualScore);

        string actualGoals =
            GetGoalMarket(actualScore);

        return new BacktestRecord
        {
            Date = date,

            Source = prediction.SiteName,

            HomeTeam = prediction.HomeTeam,
            AwayTeam = prediction.AwayTeam,

            PredictedResult = prediction.Tip,
            PredictedBtts = prediction.BttsMarket,
            PredictedGoals = prediction.GoalsMarket,

            ActualHomeGoals = homeGoals,
            ActualAwayGoals = awayGoals,

            ActualResult = actualResult,
            ActualBtts = actualBtts,
            ActualGoals = actualGoals,

            CorrectResult =
                prediction.Tip == actualResult,

            CorrectBtts =
                prediction.BttsMarket == actualBtts,

            CorrectGoals =
                prediction.GoalsMarket == actualGoals
        };
    }

    private double GetSimilarity(string s, string t)
    {
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t)) return 0;
        int n = s.Length, m = t.Length;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return 1.0 - ((double)d[n, m] / Math.Max(s.Length, t.Length));
    }

    private double GetTokenSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) ||
            string.IsNullOrWhiteSpace(b))
        {
            return 0;
        }

        var tokensA = a
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();

        var tokensB = b
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();

        if (tokensA.Count == 0 || tokensB.Count == 0)
            return 0;

        int intersection =
            tokensA.Intersect(tokensB).Count();

        int union =
            tokensA.Union(tokensB).Count();

        return union == 0
            ? 0
            : (double)intersection / union;
    }

    private bool TeamsMatchDirect(
    string h1,
    string a1,
    string h2,
    string a2)
    {
        string nH1 = NormalizeName(h1);
        string nA1 = NormalizeName(a1);
        string nH2 = NormalizeName(h2);
        string nA2 = NormalizeName(a2);

        // 1. Perfect normalized match
        if (nH1 == nH2 && nA1 == nA2)
            return true;

        double homeLev =
            GetSimilarity(nH1, nH2);

        double awayLev =
            GetSimilarity(nA1, nA2);

        double homeToken =
            GetTokenSimilarity(nH1, nH2);

        double awayToken =
            GetTokenSimilarity(nA1, nA2);

        // ---------------------------------------
        // Exact one side + strong other side
        // ---------------------------------------

        bool exactHome = nH1 == nH2;
        bool exactAway = nA1 == nA2;

        if (exactHome &&
            (awayLev >= 0.80 || awayToken >= 0.50))
        {
            return true;
        }

        if (exactAway &&
            (homeLev >= 0.80 || homeToken >= 0.50))
        {
            return true;
        }

        // ---------------------------------------
        // Both names extremely similar
        // ---------------------------------------

        if (homeLev >= 0.90 &&
            awayLev >= 0.90)
        {
            return true;
        }

        // ---------------------------------------
        // Composite score
        // ---------------------------------------

        double homeScore =
            Math.Max(homeLev, homeToken);

        double awayScore =
            Math.Max(awayLev, awayToken);

        double fixtureScore =
            (homeScore + awayScore) / 2.0;

        if (fixtureScore >= 0.88 &&
            homeScore >= 0.78 &&
            awayScore >= 0.78)
        {
            return true;
        }

        return false;
    }

    private string GetOrCreateMatch(string home, string away)
    {
        string normHome = NormalizeName(home);
        string normAway = NormalizeName(away);

        foreach (var kvp in unifiedDb)
        {
            var parts = kvp.Key.Split('|');
            if (parts.Length == 2 && TeamsMatchDirect(normHome, normAway, parts[0], parts[1]))
            {
                return kvp.Key;
            }
        }

        string searchKey = $"{normHome}|{normAway}";
        unifiedDb.GetOrAdd(searchKey, _ => new UnifiedMatch { HomeOrig = home, AwayOrig = away });
        return searchKey;
    }

    private void MergeMatch(SiteMatch siteMatch)
    {
        if (siteMatch == null) return;
        
        string dbKey = GetOrCreateMatch(siteMatch.HomeTeam, siteMatch.AwayTeam);
        if (unifiedDb.TryGetValue(dbKey, out var match))
        {
            match.Sources[siteMatch.SiteName] = new MatchSourceData
            {
                Tip = siteMatch.Tip,
                Prob = siteMatch.Prob,
                Score = siteMatch.Score,
                Btts = siteMatch.Btts,
                BttsMarket = siteMatch.BttsMarket,
                GoalsMarket = siteMatch.GoalsMarket
            };
        }
    }

    public void ExecutePipeline(List<SiteMatch> forebet, List<SiteMatch> statarea, List<SiteMatch> predictz, List<SiteMatch> wdw)
    {
        Console.WriteLine("\n--- ФАЗА НА СПОЈУВАЊЕ (MERGE) ---");
        Console.WriteLine($"Forebet: {forebet.Count} | PredictZ: {predictz.Count} | Statarea: {statarea.Count} | WinDrawWin: {wdw.Count}");

        foreach (var m in forebet)   MergeMatch(m);
        foreach (var m in predictz)  MergeMatch(m);
        foreach (var m in statarea)  MergeMatch(m);
        foreach (var m in wdw)       MergeMatch(m);

        Console.WriteLine();
        Console.WriteLine("--- MERGE DIAGNOSTICS ---");

        var distribution = unifiedDb.Values
            .GroupBy(m => m.Sources.Values.Count(s => s != null))
            .OrderBy(x => x.Key);

        foreach (var group in distribution)
        {
            Console.WriteLine(
                $"{group.Key} source(s): {group.Count()} matches");
        }

        Console.WriteLine($"TOTAL unified fixtures: {unifiedDb.Count}");

        PrintPotentialMergeMisses();

        Console.WriteLine();
        Console.WriteLine("--- SINGLE SOURCE DIAGNOSTICS ---");

        var singleSourceBySite = unifiedDb.Values
            .Where(m => m.Sources.Values.Count(s => s != null) == 1)
            .Select(m => new
            {
                Match = m,
                Source = m.Sources
                    .First(x => x.Value != null)
                    .Key
            })
            .GroupBy(x => x.Source)
            .OrderByDescending(g => g.Count());

        foreach (var group in singleSourceBySite)
        {
            Console.WriteLine(
                $"{group.Key}: {group.Count()} single-source matches");
        }

        Console.WriteLine();
        Console.WriteLine("--- SINGLE SOURCE SAMPLES ---");

        foreach (var source in unifiedDb.Values
            .Where(m => m.Sources.Values.Count(s => s != null) == 1)
            .Select(m => m.Sources.First(x => x.Value != null).Key)
            .Distinct())
        {
            Console.WriteLine();
            Console.WriteLine($"[{source}]");

            var samples = unifiedDb.Values
                .Where(m =>
                    m.Sources.Values.Count(s => s != null) == 1 &&
                    m.Sources.Any(x =>
                        x.Key == source &&
                        x.Value != null))
                .Take(10);

            foreach (var match in samples)
            {
                Console.WriteLine(
                    $"  {match.HomeOrig} vs {match.AwayOrig}");
            }
        }
            
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
	
    public async Task<List<SiteMatch>> ParseStatareaAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();
        var page = await browser.NewPageAsync();

        try
        {
            string url =
                $"https://www.statarea.com/predictions/date/{targetDate}/";

            Console.WriteLine($"[Statarea PLAYWRIGHT] GET {url}");

            await page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

            string html = null;

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    Console.WriteLine(
                        $"[Statarea] Waiting for matches, attempt {attempt}/5");

                    await page
                        .Locator("div.match")
                        .First
                        .WaitForAsync(
                            new LocatorWaitForOptions
                            {
                                State = WaitForSelectorState.Attached,
                                Timeout = 15000
                            });

                    await page.WaitForTimeoutAsync(1000);

                    html = await page.ContentAsync();

                    Console.WriteLine(
                        $"[Statarea] HTML received: {html.Length} chars");

                    break;
                }
                catch (PlaywrightException ex)
                {
                    Console.WriteLine(
                        $"[Statarea] Attempt {attempt}/5 failed: {ex.Message}");

                    if (attempt < 5)
                        await page.WaitForTimeoutAsync(1500);
                }
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                Console.WriteLine("[Statarea] Empty HTML.");
                return matches;
            }

            var soup = new HtmlDocument();
            soup.LoadHtml(html);

            var headerRow =
                soup.DocumentNode.SelectSingleNode(
                    "//tr[contains(@class, 'had') or contains(@class, 'header')]");

            Dictionary<string, int> columnIndices =
                new Dictionary<string, int>
                {
                    { "1", 3 },
                    { "X", 4 },
                    { "2", 5 },
                    { "btts", 11 }
                };

            if (headerRow != null)
            {
                var headers = headerRow
                    .SelectNodes(".//td | .//th")
                    ?.Select(n => n.InnerText.Trim().ToLower())
                    .ToList();

                if (headers != null)
                {
                    var keys = new[]
                    {
                        ("1", "1"),
                        ("X", "x"),
                        ("2", "2"),
                        ("btts", "btts")
                    };

                    foreach (var k in keys)
                    {
                        if (headers.Contains(k.Item2))
                            columnIndices[k.Item1] =
                                headers.IndexOf(k.Item2);
                    }
                }
            }

            var matchRows =
                soup.DocumentNode.SelectNodes(
                    "//div[@class='match']");

            if (matchRows == null)
            {
                Console.WriteLine(
                    "[Statarea] No match rows found.");

                return matches;
            }

            foreach (var row in matchRows)
            {
                try
                {
                    var homeNode =
                        row.SelectSingleNode(
                            ".//div[@class='hostteam']//div[@class='name']/a");

                    var awayNode =
                        row.SelectSingleNode(
                            ".//div[@class='guestteam']//div[@class='name']/a");

                    if (homeNode == null || awayNode == null)
                        continue;

                    string home = homeNode.InnerText.Trim();
                    string away = awayNode.InnerText.Trim();

                    var values =
                        row.SelectNodes(
                            ".//*[contains(@class, 'value')] | .//td[contains(@class, 'value')]");

                    if (values == null ||
                        values.Count <= columnIndices.Values.Max())
                        continue;

                    if (!int.TryParse(
                            values[columnIndices["1"]]
                                .InnerText.Replace("%", "").Trim(),
                            out int p1))
                        continue;

                    if (!int.TryParse(
                            values[columnIndices["X"]]
                                .InnerText.Replace("%", "").Trim(),
                            out int pX))
                        continue;

                    if (!int.TryParse(
                            values[columnIndices["2"]]
                                .InnerText.Replace("%", "").Trim(),
                            out int p2))
                        continue;

                    string bttsYes =
                        values.Count > columnIndices["btts"]
                            ? values[columnIndices["btts"]]
                                .InnerText.Trim()
                            : "-";

                    int maxP =
                        Math.Max(p1, Math.Max(pX, p2));

                    string tip =
                        maxP == p1 ? "1" :
                        maxP == pX ? "X" : "2";

                    matches.Add(
                        new SiteMatch
                        {
                            SiteName = "Statarea",
                            HomeTeam = home,
                            AwayTeam = away,
                            Tip = tip,
                            Prob = maxP,
                            Btts = bttsYes,
                            BttsMarket =
                                int.TryParse(bttsYes, out int b)
                                    ? (b >= 50 ? "YES" : "NO")
                                    : "-"
                        });
                }
                catch
                {
                    continue;
                }
            }

            Console.WriteLine(
                $"[Statarea PLAYWRIGHT] Parsed: {matches.Count}");

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Statarea PLAYWRIGHT ERROR] {ex}");

            return matches;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task<HtmlDocument> GetPageWithPlaywrightAsync(
    IBrowser browser,
    string url,
    string selector,
    string sourceName)
    {
        var page = await browser.NewPageAsync();

        try
        {
            Console.WriteLine(
                $"[{sourceName} PLAYWRIGHT] GET {url}");

            await page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] Final URL: {page.Url}");

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] Title: {await page.TitleAsync()}");

                string diagnosticHtml = await page.ContentAsync();

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] HTML before wait: {diagnosticHtml.Length} chars");

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] pttr count: " +
                    $"{await page.Locator("div.pttr").CountAsync()}");

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] ptcnt count: " +
                    $"{await page.Locator("div.ptcnt").CountAsync()}");

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] combined count: " +
                    $"{await page.Locator("div.pttr.ptcnt").CountAsync()}");

            await page
                .Locator(selector)
                .First
                .WaitForAsync(
                    new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = 30000
                    });

            string html = await page.ContentAsync();

            if (string.IsNullOrWhiteSpace(html))
            {
                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] Empty HTML");

                return null;
            }

            var soup = new HtmlDocument();
            soup.LoadHtml(html);

            Console.WriteLine(
                $"[{sourceName} PLAYWRIGHT] HTML: {html.Length} chars");

            return soup;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[{sourceName} PLAYWRIGHT ERROR] {ex.Message}");

            return null;
        }
        finally
        {
            await page.CloseAsync();
        }
    }
    
    public async Task<List<SiteMatch>> ParsePredictzAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();
        var page = await browser.NewPageAsync();
        string url = (targetDate == todayStr) ? "https://www.predictz.com/predictions/" : $"https://www.predictz.com/predictions/{targetDate.Replace("-", "")}/";
        
       //var soup = await GetPageWithPlaywrightAsync(browser, url, "div.pttr.ptcnt", "PredictZ");
       // STARO
        var soup = await GetPageFromFlareSolverr(url);

        if (soup == null)
        {
            await page.CloseAsync();
            return matches;
        }

        var rows = soup.DocumentNode.SelectNodes("//div[contains(@class,'pttr') and contains(@class,'ptcnt')]");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                try
                {
                    var matchNode = row.SelectSingleNode(".//div[contains(@class,'ptgame')]//a");
                    var predNode = row.SelectSingleNode(".//div[contains(@class,'ptpredboxsml')]");
                    if (matchNode == null || predNode == null) continue;

                    string matchText = HtmlEntity.DeEntitize(matchNode.InnerText.Trim());
                    string predText = HtmlEntity.DeEntitize(predNode.InnerText.Trim());
                    var parts = Regex.Split(matchText, @"\s+v\s+", RegexOptions.IgnoreCase);
                    if (parts.Length != 2) continue;

                    string home = parts[0].Trim();
                    string away = parts[1].Trim();
                    string tip = predText.StartsWith("Home") ? "1" : (predText.StartsWith("Away") ? "2" : (predText.StartsWith("Draw") ? "X" : null));
                    if (tip == null) continue;

                    string score = "-";
                    var scoreMatch = Regex.Match(predText, @"(\d+\-\d+)");
                    if (scoreMatch.Success) score = scoreMatch.Groups[1].Value;

                    matches.Add(new SiteMatch
                    {
                        SiteName = "PredictZ",
                        HomeTeam = home,
                        AwayTeam = away,
                        Tip = tip,
                        Score = score,
                        BttsMarket = GetBttsMarket(score),
                        GoalsMarket = GetGoalMarket(score)
                    });
                }
                catch { continue; }
            }
        }
        await page.CloseAsync();
        return matches;
    }

    public async Task<List<SiteMatch>> ParseWinDrawWinAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();
        var page = await browser.NewPageAsync();
        string url = (targetDate == todayStr) ? "https://www.windrawwin.com/predictions/today/" : 
                     (targetDate == DateTime.Today.AddDays(1).ToString("yyyy-MM-dd")) ? "https://www.windrawwin.com/predictions/tomorrow/" : 
                     $"https://www.windrawwin.com/predictions/future/{targetDate.Replace("-", "")}/";

        var soup = await GetPageFromFlareSolverr(url);
        if (soup == null) { await page.CloseAsync(); return matches; }

        var rows = soup.DocumentNode.SelectNodes("//div[contains(@class,'wttr')]");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                try
                {
                    var matchLink = row.SelectSingleNode(".//a[contains(@class,'wtdesklnk')]");
                    if (matchLink == null) continue;

                    string matchText = HtmlEntity.DeEntitize(matchLink.InnerText).Trim();
                    var parts = Regex.Split(matchText, @"\s*v\s*", RegexOptions.IgnoreCase);
                    if (parts.Length != 2) continue;

                    string home = parts[0].Trim();
                    string away = parts[1].Trim();

                    var predNode = row.SelectSingleNode(".//div[contains(@class,'wtprd')]");
                    if (predNode == null) continue;

                    string predText = HtmlEntity.DeEntitize(predNode.InnerText).Trim().ToLower();
                    string tip = predText.Contains("home") ? "1" : (predText.Contains("away") ? "2" : (predText.Contains("draw") ? "X" : ""));
                    if (string.IsNullOrEmpty(tip)) continue;

                    var scoreNode = row.SelectSingleNode(".//div[contains(@class,'wtsc')]");
                    string score = scoreNode?.InnerText?.Trim() ?? "-";

                    matches.Add(new SiteMatch
                    {
                        SiteName = "WinDrawWin",
                        HomeTeam = home,
                        AwayTeam = away,
                        Tip = tip,
                        Score = score,
                        BttsMarket = GetBttsMarket(score),
                        GoalsMarket = GetGoalMarket(score)
                    });
                }
                catch { continue; }
            }
        }
        await page.CloseAsync();
        return matches;
    }

    
    private string GetConfidenceLevel(int matchingVotes, int totalVotes)
    {
        if (totalVotes == 4 && matchingVotes == 4)
            return "VERY STRONG";

        if (totalVotes == 3 && matchingVotes == 3)
            return "STRONG";

        if (totalVotes == 4 && matchingVotes == 3)
            return "STRONG";

        if (totalVotes == 2 && matchingVotes == 2)
            return "MEDIUM";

        return "WEAK";
    }
	
	/*public void PrintGuaranteedReport()
    {        
        // ПОПРАВЕНО: Текстот сега правилно го содржи датумот на местото на {targetDate}
        Console.WriteLine($"\n 📊 КУМУЛАТИВЕН ПРЕСЕК НА РЕАЛНИ ПОДАТОЦИ ЗА ДАТУМ: {targetDate}\n");
        Console.WriteLine(new string('-', 120));

        Console.WriteLine(
            $"{"Натпревар",-40} | " +
            $"{"Тип",-5} | " +
            $"{"Гласови",-10} | " +
            $"{"Confidence",-12} | " +
            $"{"BTTS",-16} | " +
            $"{"Goals",-16}");

        Console.WriteLine(new string('-', 120));

        var orderedMatches = unifiedDb.Values.Select(m =>
        {
            var tips = m.Sources.Values.Where(s => s != null && (s.Tip == "1" || s.Tip == "X" || s.Tip == "2")).Select(s => s.Tip).ToList();
            if (tips.Count < 2) return null;

            var groups = tips.GroupBy(t => t).OrderByDescending(g => g.Count()).ToList();
            int matchingVotes = groups.First().Count();

            // ОПЦИЈА А: Елиминирај ги сите натпревари каде што нема барем 2 усогласени гласа за ист тип
            if ((double)matchingVotes / tips.Count < 0.67)
                return null;

            return new { Match = m, Tips = tips, MatchingVotes = matchingVotes, TotalVotes = tips.Count };
        }).Where(x => x != null).OrderByDescending(x => x.MatchingVotes).ThenByDescending(x => x.TotalVotes).ToList();

        foreach (var item in orderedMatches)
        {
            var match = item.Match;
            var tips = item.Tips;

            var tipGroups = tips.GroupBy(x => x).OrderByDescending(g => g.Count()).ToList();
            
            // Дополнителна заштита: Ако имаме чист судир (на пр. 2 гласа за '1' и 2 гласа за '2'), го прескокнуваме
            if (tipGroups.Count > 1 && tipGroups[0].Count() == tipGroups[1].Count()) continue;
            
            string finalTip = tipGroups.First().Key;

            string confidence = GetConfidenceLevel(item.MatchingVotes, item.TotalVotes);

            if (confidence == "WEAK" || confidence == "LOW")
            {
                continue;
            }

            // ПОПРАВКА ЗА BTTS КОНСЕНЗУС (Ги зема предвид само реално дефинираните маркети)
            var bttsList = new List<string>();
            foreach (var src in match.Sources.Values)
            {
                if (src == null) continue;
                
                // Ако изворот е Statarea, ја користиме нејзината логика
                if (src.BttsMarket != "-") 
                {
                    bttsList.Add(src.BttsMarket);
                }
                // Ако изворот е Forebet и има процент во Btts
                else if (!string.IsNullOrEmpty(src.Btts) && src.Btts != "-")
                {
                    if (int.TryParse(src.Btts.Replace("%", "").Trim(), out int pct))
                    {
                        bttsList.Add(pct >= 50 ? "YES" : "NO");
                    }
                }
            }

            string bttsConsensus = "-";
            if (bttsList.Count > 0)
            {
                var bttsGroups = bttsList.GroupBy(x => x).OrderByDescending(g => g.Count()).ToList();
                bttsConsensus = bttsGroups.Count > 1 && bttsGroups[0].Count() == bttsGroups[1].Count() 
                    ? "ИЗЕДНАЧЕНО" 
                    : $"{bttsGroups[0].Key} ({bttsGroups[0].Count()}/{bttsList.Count})";
            }

            // ПОПРАВКА ЗА GOALS КОНСЕНЗУС
            var goalsList = match.Sources.Values
                .Where(x => x != null &&
                            x.GoalsMarket != "-")
                .Select(x => x.GoalsMarket)
                .ToList();

            string goalsConsensus = "-";
            if (goalsList.Count > 0)
            {
                var goalsGroups = goalsList.GroupBy(x => x).OrderByDescending(g => g.Count()).ToList();
                goalsConsensus = goalsGroups.Count > 1 && goalsGroups[0].Count() == goalsGroups[1].Count() 
                    ? "ИЗЕДНАЧЕНО" 
                    : $"{goalsGroups[0].Key} ({goalsGroups[0].Count()}/{goalsList.Count})";
            }

            string activeSourcesLabel = $"{item.MatchingVotes}/{tips.Count} active";
            string fixture = $"{match.HomeOrig} vs {match.AwayOrig}";
            //Console.WriteLine(fixture + " => " + string.Join(", ", match.Sources.Where(x => x.Value != null).Select(x => x.Key)));
            
            if (bttsConsensus == "ИЗЕДНАЧЕНО" && goalsConsensus == "ИЗЕДНАЧЕНО")
            {
                continue;
            }

            Console.WriteLine(
                $"{fixture,-40} | " +
                $"{finalTip,-5} | " +
                $"{activeSourcesLabel,-10} | " +
                $"{confidence,-12} | " +
                $"{bttsConsensus,-16} | " +
                $"{goalsConsensus,-16}");
        }
    }*/

    public void PrintGuaranteedReport()
    {
        Console.WriteLine(
            $"\n📊 СИТЕ ПОДАТОЦИ ЗА ДАТУМ: {targetDate}\n");

        Console.WriteLine(new string('-', 150));

        Console.WriteLine(
            $"{"Натпревар",-42} | " +
            $"{"Тип",-8} | " +
            $"{"Гласови",-9} | " +
            $"{"Confidence",-13} | " +
            $"{"Извори",-30} | " +
            $"{"BTTS",-14} | " +
            $"{"Goals",-14}");

        Console.WriteLine(new string('-', 150));

        var orderedMatches = unifiedDb.Values
            .Select(match =>
            {
                var activeSources = match.Sources
                    .Where(x =>
                        x.Value != null &&
                        (x.Value.Tip == "1" ||
                        x.Value.Tip == "X" ||
                        x.Value.Tip == "2"))
                    .ToList();

                return new
                {
                    Match = match,
                    ActiveSources = activeSources
                };
            })
            .Where(x => x.ActiveSources.Count > 0)
            .OrderByDescending(x => x.ActiveSources.Count)
            .ThenBy(x => x.Match.HomeOrig)
            .ThenBy(x => x.Match.AwayOrig)
            .ToList();

        foreach (var item in orderedMatches)
        {
            var match = item.Match;
            var activeSources = item.ActiveSources;

            var tips = activeSources
                .Select(x => x.Value.Tip)
                .ToList();

            var tipGroups = tips
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .ToList();

            int totalVotes = tips.Count;
            int matchingVotes = tipGroups.First().Count();

            bool conflict =
                tipGroups.Count > 1 &&
                tipGroups[0].Count() == tipGroups[1].Count();

            string finalTip;
            string confidence;

            if (totalVotes == 1)
            {
                finalTip = tips[0];
                confidence = "SINGLE SOURCE";
            }
            else if (conflict)
            {
                finalTip = "CONFLICT";
                confidence = "CONFLICT";
            }
            else
            {
                finalTip = tipGroups.First().Key;

                if (totalVotes == 4 && matchingVotes == 4)
                    confidence = "VERY STRONG";
                else if (totalVotes == 4 && matchingVotes == 3)
                    confidence = "STRONG";
                else if (totalVotes == 3 && matchingVotes == 3)
                    confidence = "STRONG";
                else if (totalVotes == 3 && matchingVotes == 2)
                    confidence = "MEDIUM";
                else if (totalVotes == 2 && matchingVotes == 2)
                    confidence = "MEDIUM";
                else
                    confidence = "WEAK";
            }

            string sourcesLabel =
                string.Join(",",
                    activeSources.Select(x => x.Key));

            // -----------------------------
            // BTTS consensus
            // -----------------------------

            var bttsList = new List<string>();

            foreach (var source in activeSources)
            {
                var src = source.Value;

                if (!string.IsNullOrWhiteSpace(src.BttsMarket) &&
                    src.BttsMarket != "-")
                {
                    bttsList.Add(src.BttsMarket);
                }
                else if (!string.IsNullOrWhiteSpace(src.Btts) &&
                        src.Btts != "-")
                {
                    if (int.TryParse(
                            src.Btts.Replace("%", "").Trim(),
                            out int pct))
                    {
                        bttsList.Add(
                            pct >= 50 ? "YES" : "NO");
                    }
                }
            }

            string bttsConsensus = "-";

            if (bttsList.Count > 0)
            {
                var bttsGroups = bttsList
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                if (bttsGroups.Count > 1 &&
                    bttsGroups[0].Count() ==
                    bttsGroups[1].Count())
                {
                    bttsConsensus = "CONFLICT";
                }
                else
                {
                    bttsConsensus =
                        $"{bttsGroups[0].Key} " +
                        $"({bttsGroups[0].Count()}/{bttsList.Count})";
                }
            }

            // -----------------------------
            // GOALS consensus
            // -----------------------------

            var goalsList = activeSources
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.Value.GoalsMarket) &&
                    x.Value.GoalsMarket != "-")
                .Select(x => x.Value.GoalsMarket)
                .ToList();

            string goalsConsensus = "-";

            if (goalsList.Count > 0)
            {
                var goalsGroups = goalsList
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                if (goalsGroups.Count > 1 &&
                    goalsGroups[0].Count() ==
                    goalsGroups[1].Count())
                {
                    goalsConsensus = "CONFLICT";
                }
                else
                {
                    goalsConsensus =
                        $"{goalsGroups[0].Key} " +
                        $"({goalsGroups[0].Count()}/{goalsList.Count})";
                }
            }

            string votesLabel =
                $"{matchingVotes}/{totalVotes}";

            string fixture =
                $"{match.HomeOrig} vs {match.AwayOrig}";

            Console.WriteLine(
                $"{fixture,-42} | " +
                $"{finalTip,-8} | " +
                $"{votesLabel,-9} | " +
                $"{confidence,-13} | " +
                $"{sourcesLabel,-30} | " +
                $"{bttsConsensus,-14} | " +
                $"{goalsConsensus,-14}");
        }

        Console.WriteLine(new string('-', 150));

        Console.WriteLine(
            $"TOTAL MATCHES: {orderedMatches.Count}");
    }
    
    private void PrintPotentialMergeMisses()
    {
        Console.WriteLine();
        Console.WriteLine("--- POTENTIAL MERGE MISSES ---");

        var singles = unifiedDb.Values
            .Where(m =>
                m.Sources.Values.Count(s => s != null) == 1)
            .Select(m => new
            {
                Match = m,

                Source = m.Sources
                    .First(x => x.Value != null)
                    .Key,

                Home = NormalizeName(m.HomeOrig),
                Away = NormalizeName(m.AwayOrig)
            })
            .ToList();

        var candidates = new List<Tuple<
            string,
            string,
            string,
            string,
            double>>();

        for (int i = 0; i < singles.Count; i++)
        {
            for (int j = i + 1; j < singles.Count; j++)
            {
                var a = singles[i];
                var b = singles[j];

                // Не споредуваме два натпревари
                // од истиот source.
                if (a.Source == b.Source)
                    continue;

                double homeSim =
                    GetSimilarity(a.Home, b.Home);

                double awaySim =
                    GetSimilarity(a.Away, b.Away);

                double avg =
                    (homeSim + awaySim) / 2.0;

                // ОВА НЕ ПРАВИ MERGE.
                // Само бара потенцијални кандидати.
                if (homeSim >= 0.60 &&
                    awaySim >= 0.60 &&
                    avg >= 0.70)
                {
                    candidates.Add(
                        Tuple.Create(
                            a.Source,
                            $"{a.Match.HomeOrig} vs {a.Match.AwayOrig}",

                            b.Source,
                            $"{b.Match.HomeOrig} vs {b.Match.AwayOrig}",

                            avg));
                }
            }
        }

        foreach (var x in candidates
            .OrderByDescending(x => x.Item5)
            .Take(50))
        {
            Console.WriteLine();

            Console.WriteLine(
                $"[{x.Item1}] {x.Item2}");

            Console.WriteLine(
                $"[{x.Item3}] {x.Item4}");

            Console.WriteLine(
                $"Similarity: {x.Item5:P1}");
        }

        Console.WriteLine();

        Console.WriteLine(
            $"Potential candidates: {candidates.Count}");
    }

    private string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

}


public class Program
{
    private static readonly SemaphoreSlim analysisLock =
        new SemaphoreSlim(1, 1);

    public static async Task Main(string[] args)
    {

        if (args.Length > 0 &&
    DateTime.TryParseExact(
        args[0],
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out DateTime cliDate))
        {
            string targetDate = cliDate.ToString("yyyy-MM-dd");

            Console.WriteLine($"[CLI] Starting pipeline for {targetDate}");

            var aggregator =
                new ProductionConsensusAggregator(targetDate);

            using (var playwright = await Playwright.CreateAsync())
            {
                var browser =
                    await playwright.Chromium.LaunchAsync(
                        new BrowserTypeLaunchOptions
                        {
                            Headless = true
                        });

                try
                {
                    var forebetTask =
                        aggregator.ParseForebetAsync(browser);

                    var statareaTask =
                        aggregator.ParseStatareaAsync(browser);

                    var predictzTask =
                        aggregator.ParsePredictzAsync(browser);

                    var wdwTask =
                        aggregator.ParseWinDrawWinAsync(browser);

                    await Task.WhenAll(
                        forebetTask,
                        statareaTask,
                        predictzTask,
                        wdwTask);

                    aggregator.ExecutePipeline(
                        await forebetTask,
                        await statareaTask,
                        await predictzTask,
                        await wdwTask);

                    aggregator.PrintGuaranteedReport();
                }
                finally
                {
                    await browser.CloseAsync();
                }
            }

            Console.WriteLine(
                $"[CLI] Analysis finished: {targetDate}");

            return;
        }

        var builder = WebApplication.CreateBuilder(args);

        var app = builder.Build();

        // Render go dava PORT kako environment variable.
        // Lokalno koristime 10000.
        string port =
            Environment.GetEnvironmentVariable("PORT") ?? "10000";

        app.Urls.Clear();
        app.Urls.Add($"http://0.0.0.0:{port}");

        // ------------------------------------------------
        // HOME PAGE
        // ------------------------------------------------

        app.MapGet("/", async context =>
        {
            context.Response.ContentType =
                "text/html; charset=utf-8";

            await context.Response.WriteAsync(GetHomePage());
        });

        // ------------------------------------------------
        // ANALYSE
        // ------------------------------------------------

        app.MapGet("/analyse", async context =>
        {
            string targetDate =
                context.Request.Query["date"].ToString();

            if (!DateTime.TryParseExact(
                    targetDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsedDate))
            {
                context.Response.StatusCode = 400;

                await context.Response.WriteAsync(
                    "Invalid date. Use yyyy-MM-dd.");

                return;
            }

            targetDate =
                parsedDate.ToString("yyyy-MM-dd");

            await analysisLock.WaitAsync();

            TextWriter originalOutput = Console.Out;

            var writer = new StringWriter();

            try
            {
                // Log to Render BEFORE redirecting Console.Out.
                Console.WriteLine(
                    $"[ANALYSE] Starting pipeline for {targetDate}");

                Console.SetOut(writer);

                Console.WriteLine(
                    $"[+] Starting pipeline for {targetDate}");

                var aggregator =
                    new ProductionConsensusAggregator(targetDate);

                using (var playwright =
                    await Playwright.CreateAsync())
                {
                    var browser =
                        await playwright.Chromium.LaunchAsync(
                            new BrowserTypeLaunchOptions
                            {
                                Headless = true
                            });

                    try
                    {
                        var forebetTask =
                            aggregator.ParseForebetAsync(browser);

                        var statareaTask =
                            aggregator.ParseStatareaAsync(browser);

                        var predictzTask =
                            aggregator.ParsePredictzAsync(browser);

                        var wdwTask =
                            aggregator.ParseWinDrawWinAsync(browser);

                        await Task.WhenAll(
                            forebetTask,
                            statareaTask,
                            predictzTask,
                            wdwTask);

                        aggregator.ExecutePipeline(
                            await forebetTask,
                            await statareaTask,
                            await predictzTask,
                            await wdwTask);

                        aggregator.PrintGuaranteedReport();
                    }
                    finally
                    {
                        await browser.CloseAsync();
                    }
                }

                string result = writer.ToString();

                Console.SetOut(originalOutput);

                Console.WriteLine(
                    $"Analysis finished: {targetDate}");

                context.Response.ContentType =
                    "text/html; charset=utf-8";

                await context.Response.WriteAsync(
                    GetResultPage(
                        targetDate,
                        result));
            }
            catch (Exception ex)
            {
                Console.SetOut(originalOutput);

                Console.WriteLine(ex);

                context.Response.StatusCode = 500;

                context.Response.ContentType =
                    "text/html; charset=utf-8";

                await context.Response.WriteAsync(
                    GetErrorPage(ex.Message));
            }
            finally
            {
                Console.SetOut(originalOutput);

                writer.Dispose();

                analysisLock.Release();
            }
        });

        // ------------------------------------------------
        // HEALTH CHECK
        // ------------------------------------------------

        app.MapGet("/health", () =>
            Results.Ok(new
            {
                status = "OK",
                service = "Football AI"
            }));

        await app.RunAsync();
    }


    private static string GetHomePage()
    {
        string today =
            DateTime.Today.ToString("yyyy-MM-dd");

        return $@"
<!DOCTYPE html>
<html>
<head>

<meta charset='utf-8'>
<meta name='viewport'
      content='width=device-width, initial-scale=1'>

<title>Football AI</title>

<style>

body {{
    margin: 0;
    font-family: Arial, sans-serif;
    background: #0f172a;
    color: white;
}}

.container {{
    max-width: 700px;
    margin: 70px auto;
    padding: 20px;
}}

.card {{
    background: #1e293b;
    padding: 35px;
    border-radius: 16px;
}}

h1 {{
    margin-top: 0;
    color: #38bdf8;
}}

input {{
    width: 100%;
    box-sizing: border-box;
    padding: 14px;
    margin-top: 10px;
    margin-bottom: 20px;
    border-radius: 8px;
    border: 1px solid #475569;
    background: #0f172a;
    color: white;
    font-size: 18px;
}}

button {{
    width: 100%;
    padding: 15px;
    border: 0;
    border-radius: 8px;
    background: #0284c7;
    color: white;
    font-size: 18px;
    font-weight: bold;
    cursor: pointer;
}}

button:hover {{
    background: #0369a1;
}}

.small {{
    color: #94a3b8;
    margin-top: 20px;
}}

</style>

</head>

<body>

<div class='container'>

<div class='card'>

<h1>⚽ Football AI</h1>

<p>
Select prediction date:
</p>

<input
    id='analysisDate'
    type='date'
    value='{today}'
    required>

<button
    type='button'
    onclick=""window.location.href='/analyse?date=' + document.getElementById('analysisDate').value"">
    ANALYSE
</button>

<div class='small'>
Forebet · Statarea · PredictZ · WinDrawWin
</div>

</div>

</div>

</body>
</html>";
    }


    private static string GetResultPage(
        string date,
        string result)
    {
        string encoded =
            WebUtility.HtmlEncode(result);

        return $@"
<!DOCTYPE html>

<html>

<head>

<meta charset='utf-8'>

<meta name='viewport'
      content='width=device-width, initial-scale=1'>

<title>Football AI - {date}</title>

<style>

body {{
    margin: 0;
    background: #0f172a;
    color: #e2e8f0;
    font-family: Arial, sans-serif;
}}

.container {{
    max-width: 1400px;
    margin: auto;
    padding: 25px;
}}

h1 {{
    color: #38bdf8;
}}

a {{
    color: #38bdf8;
    text-decoration: none;
}}

pre {{
    background: #020617;
    padding: 20px;
    border-radius: 12px;
    overflow-x: auto;
    font-size: 14px;
    line-height: 1.5;
}}

</style>

</head>

<body>

<div class='container'>

<h1>⚽ Football AI</h1>

<h2>
Prediction date: {date}
</h2>

<p>
/
← New analysis
</a>
</p>

<pre>{encoded}</pre>

</div>

</body>

</html>";
    }


    private static string GetErrorPage(
        string error)
    {
        return $@"
<!DOCTYPE html>

<html>

<head>

<meta charset='utf-8'>

<title>Football AI Error</title>

</head>

<body>

<h1>Football AI</h1>

<h2>Analysis failed</h2>

<pre>
{WebUtility.HtmlEncode(error)}
</pre>

/
Back
</a>

</body>

</html>";
    }
}