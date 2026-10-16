using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;

public partial class ProductionConsensusAggregator
{
    private const string SokapediaApiBaseUrl = "https://api-staging.sokapedia.com";
    private const string SokapediaApiKey = "klci2zYXeaSooRwwdH=nu7Nqkbfmy?0rDQznTKIo2V=wuoP-O3P";
    private const string SokapediaDefaultLeaguesOrder = "39,2,3,61,140,135,78,45,276,19,36,848";

    public async Task<List<SiteMatch>> ParseFeedincoAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();
        var page = await browser.NewPageAsync();

        try
        {
            // 1. Load all paginated fixtures from Sokapedia API (Load More cursors 1..N)
            var sokapediaMatches = await ParseSokapediaApiAsync();
            foreach (var sm in sokapediaMatches)
            {
                matches.Add(sm);
            }

            // 2. Enrich / merge with Feedinco tips & predictions pages
            string? tipsPrefix = GetFeedincoTipsPrefix();

            var fulltimeMatches = tipsPrefix != null
                ? await ParseFeedincoFulltimeAsync(page, $"{tipsPrefix}/fulltime")
                : new List<SiteMatch>();

            List<SiteMatch> bttsMatches = new();
            List<SiteMatch> goalsMatches = new();

            if (tipsPrefix != null)
            {
                await Task.Delay(1200);
                bttsMatches = await ParseFeedincoBttsAsync(page, $"{tipsPrefix}/btts");
                await Task.Delay(1200);
                goalsMatches = await ParseFeedincoGoalsAsync(page, $"{tipsPrefix}/overunder");
                await Task.Delay(1200);
            }

            foreach (var ft in fulltimeMatches)
            {
                UpsertFeedincoMatch(matches, ft);
            }

            var date = targetDate;
            var url = $"https://www.feedinco.com/predictions/{date}";

            Console.WriteLine($"[Feedinco] Loading {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000
            });

            var rows = page.Locator("div[itemprop='broadcastOfEvent']");
            var count = await rows.CountAsync();

            Console.WriteLine($"[Feedinco] DOM rows: {count}");

            for (var i = 0; i < count; i++)
            {
                try
                {
                    var row = rows.Nth(i);

                    var homeLocator = row.Locator("[itemprop='homeTeam']").First;
                    var awayLocator = row.Locator("[itemprop='awayTeam']").First;

                    if (await homeLocator.CountAsync() == 0 ||
                        await awayLocator.CountAsync() == 0)
                        continue;

                    var home = CleanHomeTeam(await homeLocator.InnerTextAsync());
                    var away = CleanAwayTeam(await awayLocator.InnerTextAsync());

                    if (string.IsNullOrWhiteSpace(home) ||
                        string.IsNullOrWhiteSpace(away))
                        continue;

                    var oddsBlocks = row.Locator(".tips_odds");
                    var oddsCount = await oddsBlocks.CountAsync();

                    var rawTip = "-";
                    int? probability = null;

                    if (oddsCount > 0)
                    {
                        var tipBlock = oddsBlocks.Nth(0)
                            .Locator("div")
                            .First;

                        if (await tipBlock.CountAsync() > 0)
                            rawTip = (await tipBlock.InnerTextAsync()).Trim();
                    }

                    for (var j = 0; j < oddsCount; j++)
                    {
                        var text = (await oddsBlocks.Nth(j).InnerTextAsync())
                            .Trim();

                        var match = Regex.Match(text, @"(\d{1,3})%");

                        if (match.Success &&
                            int.TryParse(match.Groups[1].Value, out var p))
                        {
                            probability = p;
                            break;
                        }
                    }

                    var existing = FindFeedincoMatch(matches, home, away);

                    if (existing == null)
                    {
                        existing = new SiteMatch
                        {
                            SiteName = "Feedinco",
                            HomeTeam = home,
                            AwayTeam = away,
                            Tip = "-",
                            Prob = probability,
                            Score = "-",
                            Btts = "-",
                            BttsMarket = "-",
                            GoalsMarket = "-"
                        };
                        matches.Add(existing);
                    }
                    else if (!existing.Prob.HasValue && probability.HasValue)
                    {
                        existing.Prob = probability;
                    }

                    MapFeedincoMarket(existing, rawTip);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Feedinco] Row {i} skipped: {ex.Message}");
                }
            }

            foreach (var btts in bttsMatches)
            {
                var existing = FindFeedincoMatch(matches, btts.HomeTeam, btts.AwayTeam);

                if (existing != null)
                {
                    existing.Btts = btts.BttsMarket;
                    existing.BttsMarket = btts.BttsMarket;
                }
                else
                {
                    matches.Add(btts);
                }
            }

            foreach (var goals in goalsMatches)
            {
                var existing = FindFeedincoMatch(matches, goals.HomeTeam, goals.AwayTeam);

                if (existing != null && !string.IsNullOrWhiteSpace(goals.GoalsMarket))
                {
                    existing.GoalsMarket = goals.GoalsMarket;
                }
                else if (existing == null && !string.IsNullOrWhiteSpace(goals.GoalsMarket))
                {
                    matches.Add(goals);
                }
            }

            var withBtts = matches.Count(x =>
                !string.IsNullOrWhiteSpace(x.BttsMarket) && x.BttsMarket != "-");

            var withGoals = matches.Count(x =>
                !string.IsNullOrWhiteSpace(x.GoalsMarket) && x.GoalsMarket != "-");

            Console.WriteLine(
                $"[Feedinco Merge] Result={matches.Count} BTTS={withBtts} Goals={withGoals}");

            Console.WriteLine($"[Feedinco] Parsed: {matches.Count}");

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Feedinco] ERROR: {ex.Message}");
            return matches;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task<List<SiteMatch>> ParseSokapediaApiAsync()
    {
        var results = new List<SiteMatch>();

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            client.DefaultRequestHeaders.Add("X-API-KEY", SokapediaApiKey);

            string? cursor = "1";
            int pagesLoaded = 0;
            const int maxPages = 25;

            while (!string.IsNullOrWhiteSpace(cursor) && pagesLoaded < maxPages)
            {
                pagesLoaded++;
                string apiUrl =
                    $"{SokapediaApiBaseUrl}/api/v2/grouped-fixtures" +
                    $"?date={Uri.EscapeDataString(targetDate)}" +
                    $"&cursor={Uri.EscapeDataString(cursor)}" +
                    $"&per_page=100" +
                    $"&order_by_leagues={SokapediaDefaultLeaguesOrder}";

                string json = await client.GetStringAsync(apiUrl);
                if (string.IsNullOrWhiteSpace(json))
                    break;

                JObject root = JObject.Parse(json);
                if (root["data"] is not JArray leagues || leagues.Count == 0)
                    break;

                foreach (JToken league in leagues)
                {
                    if (league["fixtures"] is not JArray fixtures)
                        continue;

                    foreach (JToken item in fixtures)
                    {
                        SiteMatch? parsed = ParseSokapediaFixture(item);
                        if (parsed != null)
                            UpsertFeedincoMatch(results, parsed);
                    }
                }

                string? nextCursor = root["next_cursor"]?.ToString();
                if (string.IsNullOrWhiteSpace(nextCursor) ||
                    string.Equals(nextCursor, cursor, StringComparison.Ordinal))
                {
                    break;
                }

                cursor = nextCursor;
            }

            Console.WriteLine(
                $"[Sokapedia/Feedinco API] Pages={pagesLoaded} | Matches={results.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sokapedia/Feedinco API] Warning: {ex.Message}");
        }

        return results;
    }

    private static SiteMatch? ParseSokapediaFixture(JToken item)
    {
        string home = item["home_team_name"]?.ToString().Trim() ?? "";
        string away = item["away_team_name"]?.ToString().Trim() ?? "";

        if (string.IsNullOrWhiteSpace(home) ||
            string.IsNullOrWhiteSpace(away))
        {
            return null;
        }

        int p1 = 0;
        int px = 0;
        int p2 = 0;

        if (item["prediction_computation"] is JArray computations &&
            computations.Count > 0)
        {
            var comp = computations[0];
            TryParsePercentValue(comp["percent_pred_home"]?.ToString(), out p1);
            TryParsePercentValue(comp["percent_pred_draw"]?.ToString(), out px);
            TryParsePercentValue(comp["percent_pred_away"]?.ToString(), out p2);
        }

        int maxP = Math.Max(p1, Math.Max(px, p2));
        if (maxP <= 0)
            return null;

        // Exact Sokapedia tie-breaking rule from 2568 chunk:
        string tip;
        if (p1 > px && p1 > p2)
            tip = "1";
        else if (px > p1 && px > p2)
            tip = "X";
        else if (p2 > px && p2 > p1)
            tip = "2";
        else if (px == p1 && p2 < p1)
            tip = "1";
        else if ((px == p1 && p2 > px) || (px == p2 && p1 < p2))
            tip = "2";
        else if (p1 == p2 && p1 > px)
            tip = "1";
        else
            tip = "X";

        int probability = tip switch
        {
            "1" => p1,
            "X" => px,
            _ => p2
        };

        string goalsMarket = "-";
        if (item["predictions"] is JArray preds &&
            preds.Count > 0 &&
            double.TryParse(
                preds[0]["avg_performance"]?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double avgGoals) &&
            avgGoals > 0)
        {
            goalsMarket = avgGoals < 2.5 ? "U2.5" : "O2.5";
        }

        return new SiteMatch
        {
            SiteName = "Feedinco",
            HomeTeam = home,
            AwayTeam = away,
            Tip = tip,
            Prob = probability,
            Score = "-",
            Btts = "-",
            BttsMarket = "-",
            GoalsMarket = goalsMarket
        };
    }

    private SiteMatch? FindFeedincoMatch(
        List<SiteMatch> list,
        string home,
        string away)
    {
        return list.FirstOrDefault(x =>
            (string.Equals(x.HomeTeam, home, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(x.AwayTeam, away, StringComparison.OrdinalIgnoreCase)) ||
            (NormalizeName(x.HomeTeam) == NormalizeName(home) &&
             NormalizeName(x.AwayTeam) == NormalizeName(away)));
    }

    private void UpsertFeedincoMatch(
        List<SiteMatch> list,
        SiteMatch candidate)
    {
        var existing = FindFeedincoMatch(list, candidate.HomeTeam, candidate.AwayTeam);
        if (existing == null)
        {
            list.Add(candidate);
            return;
        }

        if ((existing.Tip == "-" || string.IsNullOrWhiteSpace(existing.Tip)) &&
            !string.IsNullOrWhiteSpace(candidate.Tip) &&
            candidate.Tip != "-")
        {
            existing.Tip = candidate.Tip;
        }

        if (!existing.Prob.HasValue && candidate.Prob.HasValue)
        {
            existing.Prob = candidate.Prob;
        }

        if ((existing.Score == "-" || string.IsNullOrWhiteSpace(existing.Score)) &&
            !string.IsNullOrWhiteSpace(candidate.Score) &&
            candidate.Score != "-")
        {
            existing.Score = candidate.Score;
        }

        if ((existing.BttsMarket == "-" || string.IsNullOrWhiteSpace(existing.BttsMarket)) &&
            !string.IsNullOrWhiteSpace(candidate.BttsMarket) &&
            candidate.BttsMarket != "-")
        {
            existing.Btts = candidate.BttsMarket;
            existing.BttsMarket = candidate.BttsMarket;
        }

        if ((existing.GoalsMarket == "-" || string.IsNullOrWhiteSpace(existing.GoalsMarket)) &&
            !string.IsNullOrWhiteSpace(candidate.GoalsMarket) &&
            candidate.GoalsMarket != "-")
        {
            existing.GoalsMarket = candidate.GoalsMarket;
        }
    }

    private string? GetFeedincoTipsPrefix()
    {
        if (!DateTime.TryParseExact(
                targetDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsedDate))
        {
            return null;
        }

        int dayOffset = (parsedDate.Date - DateTime.Today).Days;
        return dayOffset switch
        {
            0 => "https://www.feedinco.com/tips",
            1 => "https://www.feedinco.com/tips/tomorrow",
            _ => null
        };
    }

    private async Task<List<SiteMatch>> ParseFeedincoFulltimeAsync(
        IPage page,
        string url)
    {
        var matches = new List<SiteMatch>();

        try
        {
            Console.WriteLine($"[Feedinco Fulltime] Loading {url}");

            await page.GotoAsync(
                url,
                new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

            var rows = page.Locator("div[itemprop='broadcastOfEvent']");
            var count = await rows.CountAsync();

            for (var i = 0; i < count; i++)
            {
                try
                {
                    var row = rows.Nth(i);

                    var homeLocator = row.Locator("[itemprop='homeTeam']").First;
                    var awayLocator = row.Locator("[itemprop='awayTeam']").First;

                    if (await homeLocator.CountAsync() == 0 ||
                        await awayLocator.CountAsync() == 0)
                        continue;

                    var home = CleanHomeTeam(await homeLocator.InnerTextAsync());
                    var away = CleanAwayTeam(await awayLocator.InnerTextAsync());

                    if (string.IsNullOrWhiteSpace(home) ||
                        string.IsNullOrWhiteSpace(away))
                        continue;

                    var tipLocator = row.Locator("[id$='_ft_tip']");
                    if (await tipLocator.CountAsync() == 0)
                        continue;

                    var rawTip = (await tipLocator.First.InnerTextAsync()).Trim();
                    string tip = rawTip.ToUpperInvariant() switch
                    {
                        "HOME WIN" or "1" => "1",
                        "DRAW" or "X" => "X",
                        "AWAY WIN" or "2" => "2",
                        _ => "-"
                    };

                    var chanceLocator = row.Locator(".yellowpercentage, .greensm, .redpercentage");
                    int? probability = null;

                    if (await chanceLocator.CountAsync() > 0)
                    {
                        var chance = await chanceLocator.First.InnerTextAsync();
                        var m = Regex.Match(chance, @"(\d{1,3})");
                        if (m.Success && int.TryParse(m.Groups[1].Value, out int p))
                            probability = p;
                    }

                    string score = "-";
                    var scoreLocator = row.Locator("[id$='_livescore']");
                    if (await scoreLocator.CountAsync() > 0)
                    {
                        var scoreText = (await scoreLocator.First.InnerTextAsync()).Trim();
                        var sm = Regex.Match(scoreText, @"(\d+)\s*:\s*(\d+)");
                        if (sm.Success)
                            score = $"{sm.Groups[1].Value}-{sm.Groups[2].Value}";
                    }

                    matches.Add(new SiteMatch
                    {
                        SiteName = "Feedinco",
                        HomeTeam = home,
                        AwayTeam = away,
                        Tip = tip,
                        Prob = probability,
                        Score = score,
                        Btts = GetBttsMarket(score),
                        BttsMarket = GetBttsMarket(score),
                        GoalsMarket = GetGoalMarket(score)
                    });
                }
                catch
                {
                    continue;
                }
            }

            Console.WriteLine($"[Feedinco Fulltime] Parsed: {matches.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Feedinco Fulltime] ERROR: {ex.Message}");
        }

        return matches;
    }

    public async Task<List<SiteMatch>> ParseFeedincoBttsAsync(
        IPage page,
        string url)
    {
        var matches = new List<SiteMatch>();

        try
        {
            await page.GotoAsync(
                url,
                new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

            var rows = page.Locator("div[itemprop='broadcastOfEvent']");
            var count = await rows.CountAsync();

            for (var i = 0; i < count; i++)
            {
                var row = rows.Nth(i);

                var home = CleanHomeTeam(await row
                    .Locator("[itemprop='homeTeam']")
                    .First
                    .InnerTextAsync());

                var away = CleanAwayTeam(await row
                    .Locator("[itemprop='awayTeam']")
                    .First
                    .InnerTextAsync());

                var tipLocator = row.Locator("[id$='_btts_tip']");

                if (await tipLocator.CountAsync() == 0)
                    continue;

                var tip = (await tipLocator.First.InnerTextAsync()).Trim();

                var chanceLocator = row.Locator(".yellowpercentage, .greensm, .redpercentage");

                var chance = "";

                if (await chanceLocator.CountAsync() > 0)
                {
                    chance = await chanceLocator.First.InnerTextAsync();
                }

                int? probability = null;

                if (!string.IsNullOrWhiteSpace(chance))
                {
                    var m = Regex.Match(chance, @"(\d+)");

                    if (m.Success)
                        probability = int.Parse(m.Groups[1].Value);
                }

                matches.Add(new SiteMatch
                {
                    SiteName = "Feedinco",
                    HomeTeam = home,
                    AwayTeam = away,
                    Tip = "-",
                    Btts = tip.StartsWith("GG", StringComparison.OrdinalIgnoreCase) ? "YES" : "NO",
                    BttsMarket = tip.StartsWith("GG", StringComparison.OrdinalIgnoreCase)
                        ? "YES"
                        : "NO",
                    GoalsMarket = "-",
                    Prob = probability
                });
            }

            Console.WriteLine(
                $"[Feedinco BTTS] Parsed: {matches.Count}");

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Feedinco BTTS] ERROR: {ex.Message}");
            return matches;
        }
    }

    public async Task<List<SiteMatch>> ParseFeedincoGoalsAsync(
        IPage page,
        string url)
    {
        var matches = new List<SiteMatch>();

        try
        {
            await page.GotoAsync(
                url,
                new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

            var rows = page.Locator("div[itemprop='broadcastOfEvent']");
            var count = await rows.CountAsync();

            for (var i = 0; i < count; i++)
            {
                try
                {
                    var row = rows.Nth(i);

                    var home = CleanHomeTeam(await row
                        .Locator("[itemprop='homeTeam']")
                        .First
                        .InnerTextAsync());

                    var away = CleanAwayTeam(await row
                        .Locator("[itemprop='awayTeam']")
                        .First
                        .InnerTextAsync());

                    var tipLocator = row.Locator("[id$='_ou_tip']");

                    if (await tipLocator.CountAsync() == 0)
                        continue;

                    var tip = (await tipLocator.First.InnerTextAsync())
                        .Trim();

                    var chanceLocator = row.Locator(".greensm, .yellowpercentage, .redpercentage");

                    string chance = "";

                    if (await chanceLocator.CountAsync() > 0)
                    {
                        chance = await chanceLocator.First.InnerTextAsync();
                    }

                    int? probability = null;

                    if (!string.IsNullOrWhiteSpace(chance))
                    {
                        var match = Regex.Match(chance, @"(\d+)");

                        if (match.Success)
                        {
                            probability =
                                int.Parse(match.Groups[1].Value);
                        }
                    }

                    matches.Add(new SiteMatch
                    {
                        SiteName = "Feedinco",
                        HomeTeam = home,
                        AwayTeam = away,
                        Tip = "-",
                        Prob = probability,
                        Btts = "-",
                        BttsMarket = "-",
                        GoalsMarket =
                            tip.StartsWith("Over 1.5", StringComparison.OrdinalIgnoreCase) || tip.Equals("O1.5", StringComparison.OrdinalIgnoreCase) ? "O1.5" :
                            tip.StartsWith("Over 2.5", StringComparison.OrdinalIgnoreCase) || tip.Equals("O2.5", StringComparison.OrdinalIgnoreCase) ? "O2.5" :
                            tip.StartsWith("Over 3.5", StringComparison.OrdinalIgnoreCase) || tip.Equals("O3.5", StringComparison.OrdinalIgnoreCase) ? "O3.5" :
                            tip.StartsWith("Under 1.5", StringComparison.OrdinalIgnoreCase) || tip.Equals("U1.5", StringComparison.OrdinalIgnoreCase) ? "U1.5" :
                            tip.StartsWith("Under 2.5", StringComparison.OrdinalIgnoreCase) || tip.Equals("U2.5", StringComparison.OrdinalIgnoreCase) ? "U2.5" :
                            tip.StartsWith("Under 3.5", StringComparison.OrdinalIgnoreCase) || tip.Equals("U3.5", StringComparison.OrdinalIgnoreCase) ? "U3.5" :
                            "-"
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Feedinco Goals] Row {i} skipped: {ex.Message}");
                }
            }

            Console.WriteLine(
                $"[Feedinco Goals] Parsed: {matches.Count}");

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Feedinco Goals] ERROR: {ex.Message}");
            return matches;
        }
    }

    private static string CleanHomeTeam(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return Regex.Replace(
            value.Trim(),
            @"\s+\d+\s*$",
            "").Trim();
    }

    private static string CleanAwayTeam(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return Regex.Replace(
            value.Trim(),
            @"^\s*\d+\s+",
            "").Trim();
    }

    private static void MapFeedincoMarket(
        SiteMatch match,
        string tip)
    {
        if (string.IsNullOrWhiteSpace(tip))
            return;

        var normalized = tip.Trim();

        if (normalized is "1" or "X" or "2")
        {
            if (match.Tip == "-" || string.IsNullOrWhiteSpace(match.Tip))
                match.Tip = normalized;
            return;
        }

        if (normalized.Equals("1X", StringComparison.OrdinalIgnoreCase))
        {
            if (match.Tip == "-" || string.IsNullOrWhiteSpace(match.Tip))
                match.Tip = "1";
            return;
        }

        if (normalized.Equals("X2", StringComparison.OrdinalIgnoreCase))
        {
            if (match.Tip == "-" || string.IsNullOrWhiteSpace(match.Tip))
                match.Tip = "2";
            return;
        }

        if (normalized.Equals(
                "btts",
                StringComparison.OrdinalIgnoreCase))
        {
            match.Btts = "YES";
            match.BttsMarket = "YES";
            return;
        }

        if (normalized.Equals(
                "btts_No",
                StringComparison.OrdinalIgnoreCase))
        {
            match.Btts = "NO";
            match.BttsMarket = "NO";
            return;
        }

        if (normalized.Equals(
                "O2.5",
                StringComparison.OrdinalIgnoreCase))
        {
            match.GoalsMarket = "O2.5";
            return;
        }

        if (normalized.Equals(
                "U2.5",
                StringComparison.OrdinalIgnoreCase))
        {
            match.GoalsMarket = "U2.5";
        }
    }
}

