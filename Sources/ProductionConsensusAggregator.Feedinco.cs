using Microsoft.Playwright;
using System.Text.RegularExpressions;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParseFeedincoAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();
        var page = await browser.NewPageAsync();

        try
        {
            var bttsMatches = await ParseFeedincoBttsAsync(browser);
            var goalsMatches = await ParseFeedincoGoalsAsync(browser);


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

           var previewUrl = await rows
                .Nth(0)
                .Locator("a[href*='-prediction']")
                .First
                .GetAttributeAsync("href");

            Console.WriteLine($"URL={previewUrl}");

            
            var matchPage = await browser.NewPageAsync();

            await matchPage.GotoAsync(
                "https://www.feedinco.com" + previewUrl,
                new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

            var html = await matchPage.ContentAsync();

            Console.WriteLine(
                html.Contains("Recommended Betting Tips"));

            Console.WriteLine(
                html.Contains("Both teams to score"));

            Console.WriteLine(
                html.Contains("Prediction"));

            await matchPage.CloseAsync();

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

                    var tip = "-";
                    int? probability = null;

                    if (oddsCount > 0)
                    {
                        var tipBlock = oddsBlocks.Nth(0)
                            .Locator("div")
                            .First;

                        if (await tipBlock.CountAsync() > 0)
                            tip = (await tipBlock.InnerTextAsync()).Trim();
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

                    var siteMatch = new SiteMatch
                    {
                        SiteName = "Feedinco",
                        HomeTeam = home,
                        AwayTeam = away,
                        Tip = tip,
                        Prob = probability
                    };

                    MapFeedincoMarket(siteMatch, tip);

                    var btts = bttsMatches.FirstOrDefault(x =>
                        string.Equals(
                            x.HomeTeam,
                            siteMatch.HomeTeam,
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        string.Equals(
                            x.AwayTeam,
                            siteMatch.AwayTeam,
                            StringComparison.OrdinalIgnoreCase));

                    if (btts != null)
                    {
                        siteMatch.BttsMarket = btts.BttsMarket;
                    }

                    var goals = goalsMatches.FirstOrDefault(x =>
                        string.Equals(
                            x.HomeTeam,
                            siteMatch.HomeTeam,
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        string.Equals(
                            x.AwayTeam,
                            siteMatch.AwayTeam,
                            StringComparison.OrdinalIgnoreCase));

                    if (goals != null)
                    {
                        siteMatch.GoalsMarket = goals.GoalsMarket;
                    }

                    matches.Add(siteMatch);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Feedinco] Row {i} skipped: {ex.Message}");
                }
            }

            var withBtts = matches.Count(x =>
                !string.IsNullOrWhiteSpace(x.BttsMarket));

            var withGoals = matches.Count(x =>
                !string.IsNullOrWhiteSpace(x.GoalsMarket));

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

    public async Task<List<SiteMatch>> ParseFeedincoBttsAsync(
    IBrowser browser)
    {
        var matches = new List<SiteMatch>();

        var page = await browser.NewPageAsync();

        try
        {
            await page.GotoAsync(
                "https://www.feedinco.com/tips/tomorrow/btts",
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

                    var home = await row
                        .Locator("[itemprop='homeTeam']")
                        .InnerTextAsync();

                    var away = await row
                        .Locator("[itemprop='awayTeam']")
                        .InnerTextAsync();

                    var tipLocator = row.Locator("[id$='_btts_tip']");

                    if (await tipLocator.CountAsync() == 0)
                        continue;

                    var tip = await tipLocator.First.InnerTextAsync();


                    var chanceLocator = row.Locator(".yellowpercentage");

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
                        HomeTeam = home.Trim(),
                        AwayTeam = away.Trim(),
                        BttsMarket = tip.StartsWith("GG")
                            ? "YES"
                            : "NO",
                        Prob = probability
                    });
                }

            Console.WriteLine(
                $"[Feedinco BTTS] Parsed: {matches.Count}");

            return matches;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

        public async Task<List<SiteMatch>> ParseFeedincoGoalsAsync(
        IBrowser browser)
            {
                var matches = new List<SiteMatch>();

                var page = await browser.NewPageAsync();

                try
                {
                    await page.GotoAsync(
                        "https://www.feedinco.com/tips/tomorrow/overunder",
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

                            var home = (await row
                                .Locator("[itemprop='homeTeam']")
                                .InnerTextAsync()).Trim();

                            var away = (await row
                                .Locator("[itemprop='awayTeam']")
                                .InnerTextAsync()).Trim();

                            var tipLocator = row.Locator("[id$='_ou_tip']");

                            if (await tipLocator.CountAsync() == 0)
                                continue;

                            var tip = (await tipLocator.First.InnerTextAsync())
                                .Trim();

                            var chanceLocator = row.Locator(".greensm");

                            if (await chanceLocator.CountAsync() == 0)
                            {
                                chanceLocator = row.Locator(".yellowpercentage");
                            }

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
                                Tip = tip,
                                Prob = probability,
                                GoalsMarket =
                                    tip.StartsWith("Over 1.5", StringComparison.OrdinalIgnoreCase) ? "O1.5" :
                                    tip.StartsWith("Over 2.5", StringComparison.OrdinalIgnoreCase) ? "O2.5" :
                                    tip.StartsWith("Over 3.5", StringComparison.OrdinalIgnoreCase) ? "O3.5" :
                                    tip.StartsWith("Under 1.5", StringComparison.OrdinalIgnoreCase) ? "U1.5" :
                                    tip.StartsWith("Under 2.5", StringComparison.OrdinalIgnoreCase) ? "U2.5" :
                                    tip.StartsWith("Under 3.5", StringComparison.OrdinalIgnoreCase) ? "U3.5" :
                                    null
                            });

                            Console.WriteLine(
                                $"{home} | {away} | {tip} | {chance}");
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
                finally
                {
                    await page.CloseAsync();
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

        if (normalized.Equals(
                "btts",
                StringComparison.OrdinalIgnoreCase))
        {
            match.BttsMarket = "YES";
            return;
        }

        if (normalized.Equals(
                "btts_No",
                StringComparison.OrdinalIgnoreCase))
        {
            match.BttsMarket = "NO";
            return;
        }

        if (normalized.Equals(
                "O2.5",
                StringComparison.OrdinalIgnoreCase))
        {
            match.GoalsMarket = "OVER";
            return;
        }

        if (normalized.Equals(
                "U2.5",
                StringComparison.OrdinalIgnoreCase))
        {
            match.GoalsMarket = "UNDER";
        }
    }
}

