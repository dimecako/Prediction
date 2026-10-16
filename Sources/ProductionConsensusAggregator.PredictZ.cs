using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Playwright;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParsePredictzAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();

        if (!DateTime.TryParseExact(
                targetDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsedDate))
        {
            return matches;
        }

        string primaryUrl = GetPredictzListingUrl(parsedDate);
        Console.WriteLine();
        Console.WriteLine($"[PredictZ] Loading {primaryUrl}");

        var soup = await GetPageFromFlareSolverr(primaryUrl);

        // PredictZ server date rolls over on UK/UTC time (1-2 hours after CET/CEST midnight).
        // If the returned page title does not match targetDate, check the adjacent all-games page.
        if (!PredictzPageMatchesDate(soup, parsedDate))
        {
            string? fallbackUrl = GetPredictzFallbackListingUrl(parsedDate, primaryUrl);
            if (fallbackUrl != null)
            {
                Console.WriteLine(
                    $"[PredictZ] {primaryUrl} did not match {targetDate}; checking {fallbackUrl}");

                var fallbackSoup = await GetPageFromFlareSolverr(fallbackUrl);
                if (PredictzPageMatchesDate(fallbackSoup, parsedDate) || soup == null)
                {
                    soup = fallbackSoup;
                }
            }
        }

        if (soup == null)
        {
            return matches;
        }

        var rows = soup.DocumentNode.SelectNodes("//div[contains(@class,'pttr') and contains(@class,'ptcnt')]");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                try
                {
                    var predNode = row.SelectSingleNode(".//div[contains(@class,'ptpredboxsml')]");
                    if (predNode == null) continue;

                    string home = "";
                    string away = "";

                    var matchNode = row.SelectSingleNode(".//div[contains(@class,'ptgame')]//a")
                        ?? row.SelectSingleNode(".//div[contains(@class,'ptgame')]");
                    if (matchNode != null)
                    {
                        string matchText = HtmlEntity.DeEntitize(matchNode.InnerText.Trim());
                        var parts = Regex.Split(matchText, @"\s+v\s+", RegexOptions.IgnoreCase);
                        if (parts.Length == 2)
                        {
                            home = parts[0].Trim();
                            away = parts[1].Trim();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
                    {
                        var mobH = row.SelectSingleNode(".//div[contains(@class,'ptmobh')]");
                        var mobA = row.SelectSingleNode(".//div[contains(@class,'ptmoba')]");
                        if (mobH != null && mobA != null)
                        {
                            home = HtmlEntity.DeEntitize(mobH.InnerText.Trim());
                            away = HtmlEntity.DeEntitize(mobA.InnerText.Trim());
                        }
                    }

                    if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
                        continue;

                    string predText = HtmlEntity.DeEntitize(predNode.InnerText.Trim());
                    string tip = predText.StartsWith("Home", StringComparison.OrdinalIgnoreCase) ? "1" :
                                 predText.StartsWith("Away", StringComparison.OrdinalIgnoreCase) ? "2" :
                                 predText.StartsWith("Draw", StringComparison.OrdinalIgnoreCase) ? "X" : null;
                    if (tip == null) continue;

                    string score = "-";
                    var scoreMatch = Regex.Match(predText, @"(\d+\-\d+)");
                    if (scoreMatch.Success) score = scoreMatch.Groups[1].Value;

                    bool exists = matches.Any(existing =>
                        string.Equals(existing.HomeTeam, home, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.AwayTeam, away, StringComparison.OrdinalIgnoreCase));

                    if (exists)
                        continue;

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

        Console.WriteLine($"[PredictZ] Parsed matches: {matches.Count}");
        return matches;
    }

    private static string GetPredictzListingUrl(DateTime target)
    {
        int dayOffset = (target.Date - DateTime.Today).Days;

        return dayOffset switch
        {
            0 => "https://www.predictz.com/predictions/today/all-games/",
            1 => "https://www.predictz.com/predictions/tomorrow/all-games/",
            _ => $"https://www.predictz.com/predictions/{target:yyyyMMdd}/"
        };
    }

    private static string? GetPredictzFallbackListingUrl(
        DateTime target,
        string primaryUrl)
    {
        int dayOffset = (target.Date - DateTime.Today).Days;

        string? candidate = dayOffset switch
        {
            0 => "https://www.predictz.com/predictions/tomorrow/all-games/",
            1 => "https://www.predictz.com/predictions/today/all-games/",
            _ => null
        };

        return candidate != null &&
               !string.Equals(candidate, primaryUrl, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static bool PredictzPageMatchesDate(
        HtmlDocument? doc,
        DateTime target)
    {
        if (doc == null)
            return false;

        string title = HtmlEntity.DeEntitize(
            doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "");

        string monthName = target.ToString("MMMM", CultureInfo.InvariantCulture);
        return Regex.IsMatch(
            title,
            $@"\b{Regex.Escape(monthName)}\s+{target.Day}(?:st|nd|rd|th)?,\s*{target.Year}\b",
            RegexOptions.IgnoreCase);
    }
}