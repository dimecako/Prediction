using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Playwright;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParseFootyStatsAsync(
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
                    $"[FootyStats] Invalid target date: {targetDate}");

                return matches;
            }

            DateTime today = DateTime.Today;

            string url;

            if (parsedDate.Date == today)
            {
                url = "https://footystats.org/predictions";
            }
            else if (parsedDate.Date == today.AddDays(1))
            {
                url = "https://footystats.org/predictions/tomorrow";
            }
            else
            {
                Console.WriteLine(
                    "[FootyStats] Only today and tomorrow are available.");

                return matches;
            }

            Console.WriteLine();
            Console.WriteLine($"[FootyStats] Loading {url}");

            HtmlDocument? soup =
                await GetPageFromFlareSolverr(url);

            if (soup == null)
            {
                Console.WriteLine("[FootyStats] Failed to load page.");
                return matches;
            }

            var rows =
                soup.DocumentNode.SelectNodes(
                    "//div[contains(concat(' ', normalize-space(@class), ' '), ' betWrapper ') and not(@data-id='-1')]");

            if (rows == null || rows.Count == 0)
            {
                Console.WriteLine(
                    "[FootyStats] Prediction rows not found.");

                return matches;
            }

            int wrongDateRows = 0;
            int invalidRows = 0;
            int mergedRows = 0;

            foreach (HtmlNode row in rows)
            {
                try
                {
                    HtmlNode? marketNode =
                        row.SelectSingleNode(
                            ".//div[contains(@class,'betHeaderTitle')]/span[contains(@class,'market')]");

                    HtmlNode? fixtureNode =
                        row.SelectSingleNode(
                            ".//ul[contains(@class,'matchData')]/li[i[contains(@class,'fa-info-circle')]]/div[contains(@class,'data')]");

                    HtmlNode? dateNode =
                        row.SelectSingleNode(
                            ".//ul[contains(@class,'matchData')]/li[i[contains(@class,'fa-calendar')]]/div[contains(@class,'data')]");

                    string market =
                        CleanPredictionText(marketNode?.InnerText);

                    string fixture =
                        CleanPredictionText(fixtureNode?.InnerText);

                    string dateText =
                        CleanPredictionText(dateNode?.InnerText);

                    if (!FootyStatsDateMatches(
                            dateText,
                            parsedDate))
                    {
                        wrongDateRows++;
                        continue;
                    }

                    string[] teams =
                        Regex.Split(
                            fixture,
                            @"\s+vs\.?\s+",
                            RegexOptions.IgnoreCase);

                    if (teams.Length != 2 ||
                        string.IsNullOrWhiteSpace(teams[0]) ||
                        string.IsNullOrWhiteSpace(teams[1]))
                    {
                        invalidRows++;
                        continue;
                    }

                    string tip = market.ToUpperInvariant() switch
                    {
                        "HOME WIN" => "1",
                        "DRAW" => "X",
                        "AWAY WIN" => "2",
                        _ => "-"
                    };

                    string bttsMarket =
                        market.ToUpperInvariant() switch
                        {
                            "BTTS YES" => "YES",
                            "BTTS NO" => "NO",
                            _ => "-"
                        };

                    string goalsMarket =
                        market.ToUpperInvariant() switch
                        {
                            "OVER 2.5 GOALS" => "O2.5",
                            "UNDER 2.5 GOALS" => "U2.5",
                            _ => "-"
                        };

                    if (tip == "-" &&
                        bttsMarket == "-" &&
                        goalsMarket == "-")
                    {
                        continue;
                    }

                    SiteMatch? existing = matches.FirstOrDefault(match =>
                        string.Equals(
                            match.HomeTeam,
                            teams[0].Trim(),
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            match.AwayTeam,
                            teams[1].Trim(),
                            StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        matches.Add(new SiteMatch
                        {
                            SiteName = "FootyStats",
                            HomeTeam = teams[0].Trim(),
                            AwayTeam = teams[1].Trim(),
                            Tip = tip,
                            Score = "-",
                            Btts = bttsMarket,
                            BttsMarket = bttsMarket,
                            GoalsMarket = goalsMarket
                        });

                        continue;
                    }

                    if (existing.Tip == "-" && tip != "-")
                        existing.Tip = tip;

                    if (existing.BttsMarket == "-" &&
                        bttsMarket != "-")
                    {
                        existing.Btts = bttsMarket;
                        existing.BttsMarket = bttsMarket;
                    }

                    if (existing.GoalsMarket == "-" &&
                        goalsMarket != "-")
                    {
                        existing.GoalsMarket = goalsMarket;
                    }

                    mergedRows++;
                }
                catch (Exception ex)
                {
                    invalidRows++;
                    Console.WriteLine(
                        $"[FootyStats] Row error: {ex.Message}");
                }
            }

            Console.WriteLine(
                $"[FootyStats] Parsed matches: {matches.Count}");
            Console.WriteLine(
                $"[FootyStats] Other dates ignored: {wrongDateRows}");
            Console.WriteLine(
                $"[FootyStats] Invalid rows: {invalidRows}");
            Console.WriteLine(
                $"[FootyStats] Markets merged: {mergedRows}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FootyStats ERROR] {ex}");
        }

        return matches;
    }

    private static bool FootyStatsDateMatches(
        string dateText,
        DateTime target)
    {
        Match match = Regex.Match(
            dateText,
            @"(?<day>\d{1,2})(?:st|nd|rd|th)?\s+(?<month>[A-Za-z]+)",
            RegexOptions.IgnoreCase);

        if (!match.Success ||
            !int.TryParse(
                match.Groups["day"].Value,
                out int day) ||
            !DateTime.TryParseExact(
                match.Groups["month"].Value,
                "MMMM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime month))
        {
            return false;
        }

        return day == target.Day &&
               month.Month == target.Month;
    }

    private static string CleanPredictionText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return Regex.Replace(
                HtmlEntity.DeEntitize(text),
                @"\s+",
                " ")
            .Trim();
    }
}
