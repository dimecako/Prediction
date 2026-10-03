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

                    matches.Add(siteMatch);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Feedinco] Row {i} skipped: {ex.Message}");
                }
            }

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

