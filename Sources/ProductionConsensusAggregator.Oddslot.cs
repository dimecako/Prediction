using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;

public partial class ProductionConsensusAggregator
{
    private static readonly string[] OddslotDiscoveryPaths =
    {
        "/",
        "/tips/",
        "/odds/",
        "/dropping/",
        "/scores/",
        "/premier-league/",
        "/bundesliga/",
        "/serie-a/",
        "/la-liga/",
        "/ligue-1/",
        "/liga-portugal/",
        "/super-lig/",
        "/eredivisie/",
        "/jupiler-pro-league/"
    };

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

            string dateSlugPadded =
                parsedDate.ToString(
                    "dd-MMM-yyyy",
                    CultureInfo.InvariantCulture)
                .ToLowerInvariant();

            string dateSlugUnpadded =
                parsedDate.ToString(
                    "d-MMM-yyyy",
                    CultureInfo.InvariantCulture)
                .ToLowerInvariant();

            bool MatchesTargetDateSlug(string? href)
            {
                if (string.IsNullOrWhiteSpace(href))
                    return false;

                return href.Contains(
                           $"/{dateSlugPadded}/",
                           StringComparison.OrdinalIgnoreCase) ||
                       href.Contains(
                           $"/{dateSlugUnpadded}/",
                           StringComparison.OrdinalIgnoreCase);
            }

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            async Task<HtmlDocument?> LoadOddslotDocAsync(string url, bool allowFlareFallback = false)
            {
                try
                {
                    string html = await httpClient.GetStringAsync(url);
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        var doc = new HtmlDocument();
                        doc.LoadHtml(html);
                        return doc;
                    }
                }
                catch
                {
                    // Fallback below if enabled.
                }

                return allowFlareFallback
                    ? await GetPageFromFlareSolverr(url)
                    : null;
            }

            void UpsertMatch(SiteMatch candidate)
            {
                var existing = matches.FirstOrDefault(m =>
                    string.Equals(
                        m.HomeTeam,
                        candidate.HomeTeam,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        m.AwayTeam,
                        candidate.AwayTeam,
                        StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    matches.Add(candidate);
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

                if ((existing.GoalsMarket == "-" || string.IsNullOrWhiteSpace(existing.GoalsMarket)) &&
                    !string.IsNullOrWhiteSpace(candidate.GoalsMarket) &&
                    candidate.GoalsMarket != "-")
                {
                    existing.GoalsMarket = candidate.GoalsMarket;
                }

                if ((existing.BttsMarket == "-" || string.IsNullOrWhiteSpace(existing.BttsMarket)) &&
                    !string.IsNullOrWhiteSpace(candidate.BttsMarket) &&
                    candidate.BttsMarket != "-")
                {
                    existing.Btts = candidate.BttsMarket;
                    existing.BttsMarket = candidate.BttsMarket;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"[Oddslot] Discovering fixtures for {targetDate}...");

            var allMatchLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var discoverySemaphore = new SemaphoreSlim(6);
            var discoveryTasks = OddslotDiscoveryPaths.Select(async path =>
            {
                await discoverySemaphore.WaitAsync();
                try
                {
                    return (Path: path, Doc: await LoadOddslotDocAsync($"{baseUrl}{path}", allowFlareFallback: path == "/"));
                }
                finally
                {
                    discoverySemaphore.Release();
                }
            });

            var discoveryResults = await Task.WhenAll(discoveryTasks);

            void ProcessDocument(HtmlDocument? doc)
            {
                if (doc == null)
                    return;

                // 1. Parse inline .odds-match rows (e.g. on /odds/)
                var oddsRows = doc.DocumentNode.SelectNodes(
                    "//div[contains(concat(' ', normalize-space(@class), ' '), ' odds-match ')]");

                if (oddsRows != null)
                {
                    foreach (var row in oddsRows)
                    {
                        var parsed = ParseOddslotOddsRow(row, MatchesTargetDateSlug);
                        if (parsed != null)
                            UpsertMatch(parsed);
                    }
                }

                // 2. Parse inline table rows (e.g. on / and /tips/?page=N)
                var tableRows = doc.DocumentNode.SelectNodes("//tbody/tr");
                if (tableRows != null)
                {
                    foreach (var row in tableRows)
                    {
                        var parsed = ParseOddslotTableRow(row, MatchesTargetDateSlug);
                        if (parsed != null)
                            UpsertMatch(parsed);
                    }
                }

                // 3. Collect all match page links for targetDate
                var links = doc.DocumentNode
                    .SelectNodes("//a[starts-with(@href,'/football/match/')]")
                    ?.Select(node => HtmlEntity.DeEntitize(node.GetAttributeValue("href", "")))
                    .Where(MatchesTargetDateSlug);

                if (links != null)
                {
                    foreach (string link in links)
                        allMatchLinks.Add(link);
                }
            }

            foreach (var (_, doc) in discoveryResults)
            {
                ProcessDocument(doc);
            }

            // Paginate through /tips/?page=2, 3, 4... while rows are >= parsedDate.Date
            const int maxTipsPages = 100;
            int tipsPagesLoaded = 1;

            for (int pageNumber = 2; pageNumber <= maxTipsPages; pageNumber++)
            {
                string tipsPageUrl = $"{baseUrl}/tips/?page={pageNumber}";
                HtmlDocument? tipsDoc = await LoadOddslotDocAsync(tipsPageUrl);
                if (tipsDoc == null)
                    break;

                var tableRows = tipsDoc.DocumentNode.SelectNodes("//tbody/tr");
                if (tableRows == null || tableRows.Count == 0)
                    break;

                tipsPagesLoaded++;
                ProcessDocument(tipsDoc);

                bool anyRowOnOrAfterTargetDate = false;
                int datedRows = 0;

                foreach (var row in tableRows)
                {
                    string? href = HtmlEntity.DeEntitize(
                        row.SelectSingleNode(".//a[starts-with(@href,'/football/match/')]")
                            ?.GetAttributeValue("href", ""));

                    if (TryExtractOddslotLinkDate(href, out DateTime rowDate))
                    {
                        datedRows++;
                        if (rowDate.Date >= parsedDate.Date)
                            anyRowOnOrAfterTargetDate = true;
                    }
                }

                // /tips/?page=N is ordered newest-to-oldest; stop once all rows are older than targetDate.
                if (datedRows > 0 && !anyRowOnOrAfterTargetDate)
                    break;
            }

            // 4. Discover all match links from sitemaps (/sitemap-index.xml -> /sitemap/*.xml)
            // This captures all 250-400+ matches for tomorrow and upcoming dates even before 00:00 UTC.
            try
            {
                string sitemapIndexXml = await httpClient.GetStringAsync($"{baseUrl}/sitemap-index.xml");
                var sitemapUrls = Regex.Matches(
                        sitemapIndexXml,
                        @"<loc>(https://oddslot\.com/sitemap/\d+\.xml)</loc>",
                        RegexOptions.IgnoreCase)
                    .Select(m => m.Groups[1].Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var sitemapTasks = sitemapUrls.Select(async sitemapUrl =>
                {
                    await discoverySemaphore.WaitAsync();
                    try
                    {
                        return await httpClient.GetStringAsync(sitemapUrl);
                    }
                    catch
                    {
                        return "";
                    }
                    finally
                    {
                        discoverySemaphore.Release();
                    }
                });

                string[] sitemapContents = await Task.WhenAll(sitemapTasks);

                foreach (string xml in sitemapContents)
                {
                    if (string.IsNullOrWhiteSpace(xml))
                        continue;

                    var locMatches = Regex.Matches(
                        xml,
                        @"<loc>https://oddslot\.com(/football/match/[^<]+)</loc>",
                        RegexOptions.IgnoreCase);

                    foreach (Match locMatch in locMatches)
                    {
                        string path = HtmlEntity.DeEntitize(locMatch.Groups[1].Value);
                        if (MatchesTargetDateSlug(path))
                            allMatchLinks.Add(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Oddslot] Sitemap discovery warning: {ex.Message}");
            }

            Console.WriteLine(
                $"[Oddslot] /tips/ pages scanned: {tipsPagesLoaded}");

            Console.WriteLine(
                $"[Oddslot] Inline matches: {matches.Count} | Match links found: {allMatchLinks.Count}");

            int invalidPages = 0;
            using var pageSemaphore = new SemaphoreSlim(12);

            var pageTasks = allMatchLinks.Select(async link =>
            {
                await pageSemaphore.WaitAsync();
                try
                {
                    string fullUrl = new Uri(new Uri(baseUrl), link).ToString();
                    HtmlDocument? pageDoc = await LoadOddslotDocAsync(fullUrl);
                    return ParseOddslotMatchPage(pageDoc);
                }
                catch
                {
                    return null;
                }
                finally
                {
                    pageSemaphore.Release();
                }
            });

            SiteMatch?[] pageMatches = await Task.WhenAll(pageTasks);

            foreach (SiteMatch? match in pageMatches)
            {
                if (match == null)
                {
                    invalidPages++;
                    continue;
                }

                UpsertMatch(match);
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

    private static SiteMatch? ParseOddslotOddsRow(
        HtmlNode row,
        Func<string?, bool> matchesDateSlug)
    {
        var homeLink = row.SelectSingleNode(
            ".//a[contains(@class,'odds-match__team--home')]");
        var awayLink = row.SelectSingleNode(
            ".//a[contains(@class,'odds-match__team--away')]");

        string? href = HtmlEntity.DeEntitize(
            homeLink?.GetAttributeValue("href", "") ??
            awayLink?.GetAttributeValue("href", ""));

        if (!matchesDateSlug(href))
            return null;

        string home = CleanPredictionText(
            homeLink?.SelectSingleNode("./span[1]")?.InnerText);
        string away = CleanPredictionText(
            awayLink?.SelectSingleNode("./span[1]")?.InnerText);

        if (string.IsNullOrWhiteSpace(home) ||
            string.IsNullOrWhiteSpace(away))
        {
            return null;
        }

        var chanceNodes = row.SelectNodes(
            ".//span[contains(@class,'odds-match__chance')]");

        int? p1 = null;
        int? p2 = null;

        if (chanceNodes != null && chanceNodes.Count >= 2)
        {
            if (TryParsePercentValue(chanceNodes[0].InnerText, out int parsedP1))
                p1 = parsedP1;
            if (TryParsePercentValue(chanceNodes[1].InnerText, out int parsedP2))
                p2 = parsedP2;
        }

        int? px = p1.HasValue && p2.HasValue
            ? Math.Max(0, 100 - p1.Value - p2.Value)
            : null;

        string predShort = CleanPredictionText(
            row.SelectSingleNode(".//span[contains(@class,'odds-pred-short')]")?.InnerText)
            .ToUpperInvariant();

        string tip = "";
        if (predShort.Contains("HOME WIN"))
            tip = "1";
        else if (predShort.Contains("AWAY WIN"))
            tip = "2";
        else if (predShort.Contains("DRAW"))
            tip = "X";
        else if (predShort.Contains("HOME DC"))
            tip = (p1.HasValue && px.HasValue && px.Value > p1.Value) ? "X" : "1";
        else if (predShort.Contains("AWAY DC"))
            tip = (p2.HasValue && px.HasValue && px.Value > p2.Value) ? "X" : "2";
        else if (p1.HasValue && p2.HasValue && px.HasValue)
        {
            int maxP = Math.Max(p1.Value, Math.Max(px.Value, p2.Value));
            tip = maxP == p1.Value ? "1" : maxP == px.Value ? "X" : "2";
        }

        if (string.IsNullOrWhiteSpace(tip))
            return null;

        int? probability = tip switch
        {
            "1" => p1,
            "X" => px,
            "2" => p2,
            _ => null
        };

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
            GoalsMarket = "-"
        };
    }

    private static SiteMatch? ParseOddslotTableRow(
        HtmlNode row,
        Func<string?, bool> matchesDateSlug)
    {
        var teamCells = row.SelectNodes(".//a[contains(@class,'team-cell')]");
        if (teamCells == null || teamCells.Count < 2)
            return null;

        string href = HtmlEntity.DeEntitize(
            teamCells[0].GetAttributeValue("href", ""));

        if (!matchesDateSlug(href))
            return null;

        string home = CleanPredictionText(teamCells[0].InnerText);
        string away = CleanPredictionText(teamCells[1].InnerText);

        if (string.IsNullOrWhiteSpace(home) ||
            string.IsNullOrWhiteSpace(away))
        {
            return null;
        }

        string badge = CleanPredictionText(
            row.SelectSingleNode(".//span[contains(@class,'prediction-badge')]")?.InnerText)
            .ToUpperInvariant();

        string tip = MapOddslotPredictionToTip(badge);
        if (string.IsNullOrWhiteSpace(tip))
            return null;

        int? probability = null;
        var chanceNode = row.SelectSingleNode(".//span[contains(@class,'chance-badge')]");
        if (chanceNode != null &&
            TryParsePercentValue(chanceNode.InnerText, out int pct))
        {
            probability = pct;
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
            GoalsMarket = "-"
        };
    }

    private static string MapOddslotPredictionToTip(string prediction)
    {
        if (string.IsNullOrWhiteSpace(prediction))
            return "";

        if (prediction.Contains("HOME WIN") || prediction.Contains("HOME DC"))
            return "1";
        if (prediction.Contains("AWAY WIN") || prediction.Contains("AWAY DC"))
            return "2";
        if (prediction.Contains("DRAW"))
            return "X";

        return "";
    }

    private static bool TryExtractOddslotLinkDate(string? href, out DateTime parsedDate)
    {
        parsedDate = default;
        if (string.IsNullOrWhiteSpace(href))
            return false;

        var match = Regex.Match(
            href,
            @"/(\d{1,2}-[a-z]{3}-\d{4})/?$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return false;

        return DateTime.TryParseExact(
            match.Groups[1].Value,
            new[] { "dd-MMM-yyyy", "d-MMM-yyyy" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsedDate);
    }

    private static bool TryParsePercentValue(string? text, out int percent)
    {
        percent = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = Regex.Match(text, @"(\d{1,3})");
        return match.Success &&
               int.TryParse(match.Groups[1].Value, out percent) &&
               percent > 0 &&
               percent <= 100;
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

        string tip = MapOddslotPredictionToTip(prediction);

        var pollOddsNodes = page.DocumentNode.SelectNodes(
            "//button[contains(@class,'vote-poll__btn')]//span[contains(@class,'vote-poll__btn-odd')]");

        int? oddP1 = null;
        int? oddPx = null;
        int? oddP2 = null;

        if (pollOddsNodes != null && pollOddsNodes.Count >= 3)
        {
            oddP1 = TryOddToPercent(pollOddsNodes[0].InnerText);
            oddPx = TryOddToPercent(pollOddsNodes[1].InnerText);
            oddP2 = TryOddToPercent(pollOddsNodes[2].InnerText);
        }

        if (string.IsNullOrWhiteSpace(tip) &&
            oddP1.HasValue && oddPx.HasValue && oddP2.HasValue)
        {
            int maxP = Math.Max(oddP1.Value, Math.Max(oddPx.Value, oddP2.Value));
            tip = maxP == oddP1.Value ? "1" : maxP == oddPx.Value ? "X" : "2";
        }

        if (string.IsNullOrWhiteSpace(tip))
        {
            string winnerText = CleanPredictionText(
                page.DocumentNode
                    .SelectSingleNode("//div[normalize-space()='Winner']/following-sibling::div[1]")
                    ?.InnerText);

            if (!string.IsNullOrWhiteSpace(winnerText))
            {
                if (string.Equals(winnerText, home, StringComparison.OrdinalIgnoreCase))
                    tip = "1";
                else if (string.Equals(winnerText, away, StringComparison.OrdinalIgnoreCase))
                    tip = "2";
                else if (string.Equals(winnerText, "Draw", StringComparison.OrdinalIgnoreCase))
                    tip = "X";
            }
        }

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

        if (!probability.HasValue)
        {
            probability = tip switch
            {
                "1" => oddP1,
                "X" => oddPx,
                "2" => oddP2,
                _ => null
            };
        }

        string goalsMarket = "-";

        var marketLabels =
            page.DocumentNode.SelectNodes(
                "//div[normalize-space()='Under/Over']/following-sibling::div[1]");

        if (marketLabels != null)
        {
            foreach (var node in marketLabels)
            {
                string val = CleanPredictionText(node.InnerText).ToUpperInvariant();
                goalsMarket = val switch
                {
                    "OVER 0.5" => "O0.5",
                    "UNDER 0.5" => "U0.5",
                    "OVER 1.5" => "O1.5",
                    "UNDER 1.5" => "U1.5",
                    "OVER 2.5" => "O2.5",
                    "UNDER 2.5" => "U2.5",
                    "OVER 3.5" => "O3.5",
                    "UNDER 3.5" => "U3.5",
                    _ => "-"
                };

                if (goalsMarket != "-")
                    break;
            }
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

    private static int? TryOddToPercent(string? oddText)
    {
        if (string.IsNullOrWhiteSpace(oddText))
            return null;

        if (double.TryParse(
                CleanPredictionText(oddText),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double odd) &&
            odd > 1.0)
        {
            int pct = (int)Math.Round(100.0 / odd);
            if (pct > 0 && pct <= 100)
                return pct;
        }

        return null;
    }
}
