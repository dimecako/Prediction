using System.Globalization;
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

        string dateSlugPadded =
            parsedDate.ToString(
                "dd-MMMM-yyyy",
                CultureInfo.InvariantCulture)
            .ToLowerInvariant();

        string dateSlugUnpadded =
            parsedDate.ToString(
                "d-MMMM-yyyy",
                CultureInfo.InvariantCulture)
            .ToLowerInvariant();

        List<HtmlNode>? SelectTargetCards(HtmlDocument? document)
        {
            return document?.DocumentNode
                .SelectNodes("//a[contains(concat(' ', normalize-space(@class), ' '), ' tm__card ')]")
                ?.Where(node =>
                {
                    string href = HtmlEntity.DeEntitize(node.GetAttributeValue("href", ""));
                    return href.Contains($"-{dateSlugPadded}/", StringComparison.OrdinalIgnoreCase) ||
                           href.Contains($"-{dateSlugUnpadded}/", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
        }

        try
        {
            Console.WriteLine();
            Console.WriteLine($"[TheDatabetics] Loading {url}");

            HtmlDocument? doc =
                await GetPageFromFlareSolverr(url, waitInSeconds: 12);

            var cardNodes = SelectTargetCards(doc);

            if (cardNodes == null || cardNodes.Count == 0)
            {
                string? fallbackUrl = GetTheDatabeticsFallbackListingUrl(parsedDate, url);
                if (fallbackUrl != null)
                {
                    Console.WriteLine(
                        $"[TheDatabetics] No cards for {targetDate} on {url}; checking {fallbackUrl}");

                    HtmlDocument? fallbackDoc =
                        await GetPageFromFlareSolverr(fallbackUrl, waitInSeconds: 12);

                    var fallbackCards = SelectTargetCards(fallbackDoc);
                    if (fallbackCards != null && fallbackCards.Count > 0)
                    {
                        cardNodes = fallbackCards;
                    }
                }
            }

            if (cardNodes == null || cardNodes.Count == 0)
            {
                Console.WriteLine(
                    $"[TheDatabetics] No prediction cards found for {targetDate}.");

                return matches;
            }

            int invalidCards = 0;

            foreach (var card in cardNodes)
            {
                try
                {
                    var teams = card.SelectNodes(
                        ".//*[contains(concat(' ', normalize-space(@class), ' '), ' tm__team-name ')]");

                    if (teams == null || teams.Count < 2)
                    {
                        invalidCards++;
                        continue;
                    }

                    string home = CleanPredictionText(teams[0].InnerText);
                    string away = CleanPredictionText(teams[1].InnerText);

                    string prediction = CleanPredictionText(
                        card.SelectSingleNode(
                            ".//*[contains(concat(' ', normalize-space(@class), ' '), ' tm__pred ')]")
                        ?.InnerText);

                    string tip = prediction.ToUpperInvariant() switch
                    {
                        "HOME WIN" or "HOME OR DRAW" or "1X" => "1",
                        "DRAW" => "X",
                        "AWAY WIN" or "AWAY OR DRAW" or "X2" => "2",
                        _ => "-"
                    };

                    string probabilityText = CleanPredictionText(
                        card.SelectSingleNode(
                            ".//*[contains(concat(' ', normalize-space(@class), ' '), ' tm__pct ')]")
                        ?.InnerText);

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

                    string detailUrl = HtmlEntity.DeEntitize(
                        card.GetAttributeValue("href", ""));

                    if (string.IsNullOrWhiteSpace(home) ||
                        string.IsNullOrWhiteSpace(away) ||
                        tip == "-" ||
                        string.IsNullOrWhiteSpace(detailUrl))
                    {
                        invalidCards++;
                        continue;
                    }

                    bool exists = matches.Any(existing =>
                        string.Equals(
                            existing.HomeTeam,
                            home,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            existing.AwayTeam,
                            away,
                            StringComparison.OrdinalIgnoreCase));

                    if (exists)
                        continue;

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
                }
                catch (Exception ex)
                {
                    invalidCards++;
                    Console.WriteLine(
                        $"[TheDatabetics] Card skipped: {ex.Message}");
                }
            }

            Console.WriteLine(
                $"[TheDatabetics] Parsed matches: {matches.Count}");
            Console.WriteLine(
                $"[TheDatabetics] Invalid cards: {invalidCards}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TheDatabetics ERROR] {ex}");
        }

        return matches;
    }

    private static string? GetTheDatabeticsListingUrl(
        DateTime target)
    {
        const string baseUrl =
            "https://thedatabetics.com/football-predictions/";

        int dayOffset =
            (target.Date - DateTime.Today).Days;

        return dayOffset switch
        {
            -1 => $"{baseUrl}yesterday/",
            0 => baseUrl,
            1 => $"{baseUrl}tomorrow/",
            >= 2 and <= 5 => $"{baseUrl}upcoming/",
            _ => null
        };
    }

    private static string? GetTheDatabeticsFallbackListingUrl(
        DateTime target,
        string primaryUrl)
    {
        const string baseUrl =
            "https://thedatabetics.com/football-predictions/";

        int localOffset = (target.Date - DateTime.Today).Days;

        string? candidate = localOffset switch
        {
            0 => $"{baseUrl}tomorrow/",
            1 => baseUrl,
            -1 => baseUrl,
            _ => null
        };

        return candidate != null &&
               !string.Equals(candidate, primaryUrl, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static async Task PopulateTheDatabeticsMarketsAsync(
        IAPIRequestContext apiRequest,
        SemaphoreSlim requestSemaphore,
        SiteMatch match,
        string detailUrl)
    {
        await requestSemaphore.WaitAsync();

        try
        {
            var response = await apiRequest.GetAsync(detailUrl);
            if (!response.Ok)
                return;

            string html = await response.TextAsync();
            if (string.IsNullOrWhiteSpace(html) ||
                !html.Contains("mp-mpred", StringComparison.Ordinal))
            {
                return;
            }

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
