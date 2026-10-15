using System.Globalization;
using HtmlAgilityPack;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParseOddslotAsync(
        IBrowser browser)
    {
        var matches = new List<SiteMatch>();

        try
        {
            if (!DateTime.TryParseExact(
                    targetDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsedDate))
            {
                Console.WriteLine(
                    $"[Oddslot] Invalid target date: {targetDate}");

                return matches;
            }

            const string baseUrl = "https://oddslot.com";

            Console.WriteLine();
            Console.WriteLine($"[Oddslot] Loading {baseUrl}/");

            HtmlDocument? index =
                await GetPageFromFlareSolverr($"{baseUrl}/");

            if (index == null)
            {
                Console.WriteLine("[Oddslot] Failed to load page.");
                return matches;
            }

            string dateSlug =
                parsedDate.ToString(
                    "dd-MMM-yyyy",
                    CultureInfo.InvariantCulture)
                .ToLowerInvariant();

            var matchLinks =
                index.DocumentNode
                    .SelectNodes(
                        $"//a[starts-with(@href,'/football/match/') and contains(@href,'/{dateSlug}/')]")
                    ?.Select(node =>
                        node.GetAttributeValue("href", ""))
                    .Where(href =>
                        !string.IsNullOrWhiteSpace(href))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                ?? new List<string>();

            if (matchLinks.Count == 0)
            {
                Console.WriteLine(
                    $"[Oddslot] No match links found for {targetDate}.");

                return matches;
            }

            Console.WriteLine(
                $"[Oddslot] Match pages: {matchLinks.Count}");

            int invalidPages = 0;
            const int pageBatchSize = 4;

            for (int offset = 0; offset < matchLinks.Count; offset += pageBatchSize)
            {
                Task<HtmlDocument>[] pageTasks =
                    matchLinks
                        .Skip(offset)
                        .Take(pageBatchSize)
                        .Select(link =>
                            GetPageFromFlareSolverr(
                                new Uri(
                                    new Uri(baseUrl),
                                    link)
                                .ToString()))
                        .ToArray();

                HtmlDocument[] pages =
                    await Task.WhenAll(pageTasks);

                foreach (HtmlDocument? page in pages)
                {
                    try
                    {
                        SiteMatch? match =
                            ParseOddslotMatchPage(page);

                        if (match == null)
                        {
                            invalidPages++;
                            continue;
                        }

                        bool exists = matches.Any(existing =>
                            string.Equals(
                                existing.HomeTeam,
                                match.HomeTeam,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                existing.AwayTeam,
                                match.AwayTeam,
                                StringComparison.OrdinalIgnoreCase));

                        if (!exists)
                            matches.Add(match);
                    }
                    catch (Exception ex)
                    {
                        invalidPages++;
                        Console.WriteLine(
                            $"[Oddslot] Match page error: {ex.Message}");
                    }
                }
            }

            Console.WriteLine(
                $"[Oddslot] Parsed matches: {matches.Count}");
            Console.WriteLine(
                $"[Oddslot] Invalid pages: {invalidPages}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Oddslot ERROR] {ex}");
        }

        return matches;
    }

    private static SiteMatch? ParseOddslotMatchPage(
        HtmlDocument? page)
    {
        if (page == null)
            return null;

        HtmlNode? eventNode =
            page.DocumentNode
                .SelectNodes("//script[@type='application/ld+json']")
                ?.FirstOrDefault(node =>
                    node.InnerText.Contains(
                        "\"@type\":\"SportsEvent\"",
                        StringComparison.Ordinal));

        if (eventNode == null)
            return null;

        JObject sportsEvent =
            JObject.Parse(
                HtmlEntity.DeEntitize(
                    eventNode.InnerText));

        string home =
            sportsEvent["homeTeam"]?["name"]
                ?.ToString()
                .Trim()
            ?? "";

        string away =
            sportsEvent["awayTeam"]?["name"]
                ?.ToString()
                .Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(home) ||
            string.IsNullOrWhiteSpace(away))
        {
            return null;
        }

        string prediction =
            CleanPredictionText(
                page.DocumentNode
                    .SelectSingleNode(
                        "//div[contains(@class,'match-prediction')]/strong")
                    ?.InnerText)
            .ToUpperInvariant();

        string tip = prediction switch
        {
            "HOME WIN" => "1",
            "DRAW" => "X",
            "AWAY WIN" => "2",
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(tip))
            return null;

        int? probability = null;

        var chanceNodes =
            page.DocumentNode.SelectNodes(
                "//div[contains(concat(' ', normalize-space(@class), ' '), ' match-chance ')]");

        if (chanceNodes != null)
        {
            string expectedLabel = tip switch
            {
                "1" => "HOME WIN",
                "X" => "DRAW",
                _ => "AWAY WIN"
            };

            HtmlNode? predictedChance =
                chanceNodes.FirstOrDefault(node =>
                    string.Equals(
                        CleanPredictionText(
                            node.SelectSingleNode(
                                ".//span[contains(@class,'match-chance__label')]")
                            ?.InnerText),
                        expectedLabel,
                        StringComparison.OrdinalIgnoreCase));

            string percentText =
                CleanPredictionText(
                    predictedChance
                        ?.SelectSingleNode(
                            ".//span[contains(@class,'match-chance__value')]")
                        ?.InnerText)
                .TrimEnd('%');

            if (int.TryParse(percentText, out int percent) &&
                percent > 0 &&
                percent <= 100)
            {
                probability = percent;
            }
        }

        string goalsMarket = "-";

        var marketLabels =
            page.DocumentNode.SelectNodes(
                "//div[normalize-space()='Under/Over']/following-sibling::div[1]");

        if (marketLabels != null)
        {
            string market =
                marketLabels
                    .Select(node =>
                        CleanPredictionText(node.InnerText))
                    .FirstOrDefault(value =>
                        string.Equals(
                            value,
                            "Over 2.5",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            value,
                            "Under 2.5",
                            StringComparison.OrdinalIgnoreCase))
                ?? "";

            goalsMarket =
                market.Equals(
                    "Over 2.5",
                    StringComparison.OrdinalIgnoreCase)
                    ? "O2.5"
                    : market.Equals(
                        "Under 2.5",
                        StringComparison.OrdinalIgnoreCase)
                        ? "U2.5"
                        : "-";
        }

        return new SiteMatch
        {
            SiteName = "Oddslot",
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
}
