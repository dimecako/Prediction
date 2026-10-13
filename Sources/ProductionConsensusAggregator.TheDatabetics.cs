using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Playwright;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParseTheDatabeticsAsync(
        IBrowser browser)
    {
        var matches = new List<SiteMatch>();

        if (!DateTime.TryParseExact(
                targetDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsedDate))
        {
            Console.WriteLine(
                $"[TheDatabetics] Invalid target date: {targetDate}");

            return matches;
        }

        string? url = GetTheDatabeticsListingUrl(parsedDate);

        if (url == null)
        {
            Console.WriteLine(
                "[TheDatabetics] Only yesterday, today, tomorrow, and upcoming dates are available.");

            return matches;
        }

        string dateSlug =
            parsedDate.ToString(
                "dd-MMMM-yyyy",
                CultureInfo.InvariantCulture)
            .ToLowerInvariant();

        var page = await browser.NewPageAsync();

        try
        {
            Console.WriteLine();
            Console.WriteLine($"[TheDatabetics] Loading {url}");

            await page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

            var cards = page.Locator(
                $"a.tm__card[href*='-{dateSlug}/']");

            int count = await cards.CountAsync();

            if (count == 0)
            {
                Console.WriteLine(
                    $"[TheDatabetics] No prediction cards found for {targetDate}.");

                return matches;
            }

            int invalidCards = 0;
            var detailUrls =
                new Dictionary<SiteMatch, string>();

            for (int index = 0; index < count; index++)
            {
                try
                {
                    var card = cards.Nth(index);
                    var teams = card.Locator(".tm__team-name");

                    if (await teams.CountAsync() != 2)
                    {
                        invalidCards++;
                        continue;
                    }

                    string home =
                        CleanPredictionText(
                            await teams.Nth(0).InnerTextAsync());

                    string away =
                        CleanPredictionText(
                            await teams.Nth(1).InnerTextAsync());

                    string prediction =
                        CleanPredictionText(
                            await card
                                .Locator(".tm__pred")
                                .InnerTextAsync());

                    string tip = prediction.ToUpperInvariant() switch
                    {
                        "HOME WIN" => "1",
                        "DRAW" => "X",
                        "AWAY WIN" => "2",
                        _ => "-"
                    };

                    string probabilityText =
                        CleanPredictionText(
                            await card
                                .Locator(".tm__pct")
                                .InnerTextAsync());

                    Match probabilityMatch =
                        Regex.Match(
                            probabilityText,
                            @"\d{1,3}");

                    int? probability =
                        probabilityMatch.Success &&
                        int.TryParse(
                            probabilityMatch.Value,
                            out int parsedProbability)
                            ? parsedProbability
                            : null;

                    string? detailUrl =
                        await card.GetAttributeAsync("href");

                    if (string.IsNullOrWhiteSpace(home) ||
                        string.IsNullOrWhiteSpace(away) ||
                        tip == "-" ||
                        string.IsNullOrWhiteSpace(detailUrl))
                    {
                        invalidCards++;
                        continue;
                    }

                    var match = new SiteMatch
                    {
                        SiteName = "TheDatabetics",
                        HomeTeam = home,
                        AwayTeam = away,
                        Tip = tip,
                        Prob = probability,
                        Score = "-",
                        Btts = "-",
                        BttsMarket = "-",
                        GoalsMarket = "-"
                    };

                    matches.Add(match);
                    detailUrls.Add(match, detailUrl);
                }
                catch (Exception ex)
                {
                    invalidCards++;
                    Console.WriteLine(
                        $"[TheDatabetics] Card {index} skipped: {ex.Message}");
                }
            }

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(
                    "PredictionAggregator",
                    "1.0"));

            using var requestSemaphore = new SemaphoreSlim(6);

            await Task.WhenAll(
                matches.Select(match =>
                    PopulateTheDatabeticsMarketsAsync(
                        client,
                        requestSemaphore,
                        match,
                        detailUrls[match])));

            int withBtts =
                matches.Count(match =>
                    match.BttsMarket != "-");

            int withGoals =
                matches.Count(match =>
                    match.GoalsMarket != "-");

            Console.WriteLine(
                $"[TheDatabetics] Parsed matches: {matches.Count}");
            Console.WriteLine(
                $"[TheDatabetics] Invalid cards: {invalidCards}");
            Console.WriteLine(
                $"[TheDatabetics] Markets: BTTS={withBtts} Goals={withGoals}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TheDatabetics ERROR] {ex}");
        }
        finally
        {
            await page.CloseAsync();
        }

        return matches;
    }

    private static string? GetTheDatabeticsListingUrl(
        DateTime target)
    {
        const string baseUrl =
            "https://thedatabetics.com/football-predictions/";

        int dayOffset =
            (target.Date - DateTime.UtcNow.Date).Days;

        return dayOffset switch
        {
            -1 => $"{baseUrl}yesterday/",
            0 => baseUrl,
            1 => $"{baseUrl}tomorrow/",
            >= 2 and <= 5 => $"{baseUrl}upcoming/",
            _ => null
        };
    }

    private static async Task PopulateTheDatabeticsMarketsAsync(
        HttpClient client,
        SemaphoreSlim requestSemaphore,
        SiteMatch match,
        string detailUrl)
    {
        await requestSemaphore.WaitAsync();

        try
        {
            string html =
                await client.GetStringAsync(detailUrl);

            var document = new HtmlDocument();
            document.LoadHtml(html);

            var markets =
                document.DocumentNode.SelectNodes(
                    "//div[contains(concat(' ', normalize-space(@class), ' '), ' mp-mpred ')]");

            if (markets == null)
                return;

            foreach (HtmlNode market in markets)
            {
                string label =
                    CleanPredictionText(
                        market.SelectSingleNode(
                            ".//*[contains(concat(' ', normalize-space(@class), ' '), ' mp-mpred__label ')]")
                        ?.InnerText);

                string pick =
                    CleanPredictionText(
                        market.SelectSingleNode(
                            ".//*[contains(concat(' ', normalize-space(@class), ' '), ' mp-mpred__pick ')]")
                        ?.InnerText)
                    .ToUpperInvariant();

                if (label.Equals(
                        "Both Teams Score",
                        StringComparison.OrdinalIgnoreCase))
                {
                    match.BttsMarket = pick switch
                    {
                        "YES" => "YES",
                        "NO" => "NO",
                        _ => match.BttsMarket
                    };

                    match.Btts = match.BttsMarket;
                }
                else if (label.Equals(
                             "Over 2.5 Goals",
                             StringComparison.OrdinalIgnoreCase))
                {
                    match.GoalsMarket = pick switch
                    {
                        "YES" => "O2.5",
                        "NO" => "U2.5",
                        _ => match.GoalsMarket
                    };
                }
                else if (label.Equals(
                             "Under 2.5 Goals",
                             StringComparison.OrdinalIgnoreCase))
                {
                    match.GoalsMarket = pick switch
                    {
                        "YES" => "U2.5",
                        "NO" => "O2.5",
                        _ => match.GoalsMarket
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[TheDatabetics] Markets unavailable for {match.HomeTeam} vs {match.AwayTeam}: {ex.Message}");
        }
        finally
        {
            requestSemaphore.Release();
        }
    }
}
