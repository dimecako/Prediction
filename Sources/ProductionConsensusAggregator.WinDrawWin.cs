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
    public async Task<List<SiteMatch>> ParseWinDrawWinAsync(IBrowser browser)
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

        string primaryUrl = GetWinDrawWinListingUrl(parsedDate);
        Console.WriteLine();
        Console.WriteLine($"[WinDrawWin] Loading {primaryUrl}");

        var soup = await GetPageFromFlareSolverr(primaryUrl);

        // WinDrawWin server date rolls over on UK/UTC time (1-2 hours after CET/CEST midnight).
        // Verify the page date via title / future-date nav links, and fall back to the adjacent no-filter page if needed.
        if (!WinDrawWinPageMatchesDate(soup, primaryUrl, parsedDate))
        {
            string? fallbackUrl = GetWinDrawWinFallbackListingUrl(parsedDate, primaryUrl);
            if (fallbackUrl != null)
            {
                Console.WriteLine(
                    $"[WinDrawWin] {primaryUrl} did not match {targetDate}; checking {fallbackUrl}");

                var fallbackSoup = await GetPageFromFlareSolverr(fallbackUrl);
                if (WinDrawWinPageMatchesDate(fallbackSoup, fallbackUrl, parsedDate) || soup == null)
                {
                    soup = fallbackSoup;
                }
            }
        }

        if (soup == null)
            return matches;

        var rows = soup.DocumentNode.SelectNodes("//div[contains(@class,'wttr')]");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                try
                {
                    string home = "";
                    string away = "";

                    var matchLink = row.SelectSingleNode(".//a[contains(@class,'wtdesklnk')]");
                    if (matchLink != null)
                    {
                        string matchText = HtmlEntity.DeEntitize(matchLink.InnerText).Trim();
                        var parts = Regex.Split(matchText, @"\s+v\s+", RegexOptions.IgnoreCase);
                        if (parts.Length == 2)
                        {
                            home = parts[0].Trim();
                            away = parts[1].Trim();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
                    {
                        var mobTeams = row.SelectNodes(".//div[contains(@class,'wtmoblnk')]");
                        if (mobTeams != null && mobTeams.Count >= 2)
                        {
                            home = HtmlEntity.DeEntitize(mobTeams[0].InnerText).Trim();
                            away = HtmlEntity.DeEntitize(mobTeams[1].InnerText).Trim();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
                        continue;

                    var predNode = row.SelectSingleNode(".//div[contains(@class,'wtprd')]");
                    if (predNode == null) continue;

                    string predText = HtmlEntity.DeEntitize(predNode.InnerText).Trim().ToLowerInvariant();
                    string tip = predText.Contains("home") ? "1" : (predText.Contains("away") ? "2" : (predText.Contains("draw") ? "X" : ""));
                    if (string.IsNullOrEmpty(tip)) continue;

                    var scoreNode = row.SelectSingleNode(".//div[contains(@class,'wtsc')]");
                    string score = scoreNode?.InnerText?.Trim() ?? "-";

                    bool exists = matches.Any(existing =>
                        string.Equals(existing.HomeTeam, home, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.AwayTeam, away, StringComparison.OrdinalIgnoreCase));

                    if (exists)
                        continue;

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

        Console.WriteLine($"[WinDrawWin] Parsed matches: {matches.Count}");
        return matches;
    }

    private static string GetWinDrawWinListingUrl(DateTime target)
    {
        int dayOffset = (target.Date - DateTime.Today).Days;

        return dayOffset switch
        {
            -1 => "https://www.windrawwin.com/predictions/yesterday/",
            0 => "https://www.windrawwin.com/predictions/today/no-filter/",
            1 => "https://www.windrawwin.com/predictions/tomorrow/no-filter/",
            _ => $"https://www.windrawwin.com/predictions/future/{target:yyyyMMdd}/no-filter/"
        };
    }

    private static string? GetWinDrawWinFallbackListingUrl(
        DateTime target,
        string primaryUrl)
    {
        int dayOffset = (target.Date - DateTime.Today).Days;

        string? candidate = dayOffset switch
        {
            0 => "https://www.windrawwin.com/predictions/tomorrow/no-filter/",
            1 => "https://www.windrawwin.com/predictions/today/no-filter/",
            2 => "https://www.windrawwin.com/predictions/tomorrow/no-filter/",
            _ => null
        };

        return candidate != null &&
               !string.Equals(candidate, primaryUrl, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static bool WinDrawWinPageMatchesDate(
        HtmlDocument? doc,
        string loadedUrl,
        DateTime target)
    {
        if (doc == null)
            return false;

        string title = HtmlEntity.DeEntitize(
            doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "");

        string monthName = target.ToString("MMMM", CultureInfo.InvariantCulture);
        if (Regex.IsMatch(
                title,
                $@"\b{Regex.Escape(monthName)}\s+{target.Day}(?:st|nd|rd|th)?,\s*{target.Year}\b",
                RegexOptions.IgnoreCase))
        {
            return true;
        }

        // On /predictions/today/no-filter/, the title does not include the calendar date,
        // but the navigation bar always links to /predictions/future/{yyyyMMdd}/ starting at siteToday + 2 days.
        var futureDates = doc.DocumentNode
            .SelectNodes("//a[contains(@href,'/predictions/future/')]")
            ?.Select(node =>
            {
                string href = node.GetAttributeValue("href", "");
                var m = Regex.Match(href, @"/predictions/future/(\d{8})/");
                return m.Success &&
                       DateTime.TryParseExact(
                           m.Groups[1].Value,
                           "yyyyMMdd",
                           CultureInfo.InvariantCulture,
                           DateTimeStyles.None,
                           out DateTime d)
                    ? (DateTime?)d.Date
                    : null;
            })
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .OrderBy(d => d)
            .ToList();

        if (futureDates != null && futureDates.Count > 0)
        {
            DateTime siteToday = futureDates[0].AddDays(-2);

            if (loadedUrl.Contains("/today/", StringComparison.OrdinalIgnoreCase))
                return siteToday == target.Date;

            if (loadedUrl.Contains("/tomorrow/", StringComparison.OrdinalIgnoreCase))
                return siteToday.AddDays(1) == target.Date;
        }

        return false;
    }
}