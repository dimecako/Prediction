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
            int dayOffset = (parsedDate.Date - today).Days;

            if (dayOffset < -1 || dayOffset > 5)
            {
                Console.WriteLine(
                    "[FootyStats] Only yesterday, today, and upcoming dates (up to +5 days) are available.");

                return matches;
            }

            void UpsertFootyStatsMatch(
                string home,
                string away,
                string tip,
                int? prob,
                string bttsMarket,
                string goalsMarket,
                ref int mergedCount)
            {
                SiteMatch? existing = matches.FirstOrDefault(match =>
                    string.Equals(
                        match.HomeTeam,
                        home,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        match.AwayTeam,
                        away,
                        StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    matches.Add(new SiteMatch
                    {
                        SiteName = "FootyStats",
                        HomeTeam = home,
                        AwayTeam = away,
                        Tip = tip,
                        Prob = prob,
                        Score = "-",
                        Btts = bttsMarket,
                        BttsMarket = bttsMarket,
                        GoalsMarket = goalsMarket
                    });
                    return;
                }

                if (existing.Tip == "-" && tip != "-")
                    existing.Tip = tip;

                if (!existing.Prob.HasValue && prob.HasValue)
                    existing.Prob = prob;

                if (existing.BttsMarket == "-" && bttsMarket != "-")
                {
                    existing.Btts = bttsMarket;
                    existing.BttsMarket = bttsMarket;
                }

                if (existing.GoalsMarket == "-" && goalsMarket != "-")
                    existing.GoalsMarket = goalsMarket;

                mergedCount++;
            }

            var urls = new List<string>();

            if (dayOffset == 0)
                urls.Add("https://footystats.org/predictions");
            else if (dayOffset == 1)
                urls.Add("https://footystats.org/predictions/tomorrow");

            urls.Add("https://footystats.org/predictions/1x2");
            urls.Add("https://footystats.org/predictions/home-wins");
            urls.Add("https://footystats.org/predictions/away-wins");
            urls.Add("https://footystats.org/predictions/btts");
            urls.Add("https://footystats.org/predictions/over-25-goals");
            urls.Add("https://footystats.org/predictions/mathematical");

            string? fixtureFeedUrl = dayOffset switch
            {
                -1 => "https://footystats.org/yesterday",
                0 => "https://footystats.org/",
                1 => "https://footystats.org/tomorrow",
                _ => null
            };

            int wrongDateRows = 0;
            int invalidRows = 0;
            int mergedRows = 0;

            if (fixtureFeedUrl != null)
            {
                Console.WriteLine();
                Console.WriteLine($"[FootyStats] Loading fixture feed {fixtureFeedUrl}");

                HtmlDocument? feedDoc = await GetPageFromFlareSolverr(fixtureFeedUrl);
                var feedRows = feedDoc?.DocumentNode.SelectNodes(
                    "//a[contains(concat(' ', normalize-space(@class), ' '), ' match ') and contains(concat(' ', normalize-space(@class), ' '), ' row ')]");

                if (feedRows != null)
                {
                    foreach (HtmlNode matchRow in feedRows)
                    {
                        try
                        {
                            var oddsBox = matchRow.SelectSingleNode(
                                ".//div[contains(@class,'stat') and contains(@class,'odds')]");

                            if (oddsBox == null ||
                                oddsBox.SelectSingleNode(".//i[contains(@class,'fa-lock')]") != null)
                            {
                                continue;
                            }

                            var teamSpans = matchRow.SelectNodes(
                                ".//span[contains(@class,'hover-modal-ajax-team')]");

                            if (teamSpans == null || teamSpans.Count < 2)
                                continue;

                            string home = CleanPredictionText(teamSpans[0].InnerText);
                            string away = CleanPredictionText(teamSpans[1].InnerText);

                            if (string.IsNullOrWhiteSpace(home) ||
                                string.IsNullOrWhiteSpace(away))
                            {
                                continue;
                            }

                            var oddSpans = oddsBox.SelectNodes(
                                "./span[contains(@class,'hover-modal-parent')]");

                            if (oddSpans == null || oddSpans.Count < 3)
                                continue;

                            if (!TryParseFirstDouble(oddSpans[0].InnerText, out double odd1) ||
                                !TryParseFirstDouble(oddSpans[1].InnerText, out double oddX) ||
                                !TryParseFirstDouble(oddSpans[2].InnerText, out double odd2) ||
                                odd1 <= 1.0 || oddX <= 1.0 || odd2 <= 1.0)
                            {
                                continue;
                            }

                            double minOdd = Math.Min(odd1, Math.Min(oddX, odd2));
                            string tip = minOdd == odd1 ? "1" : minOdd == oddX ? "X" : "2";
                            int prob = Math.Clamp((int)Math.Round(100.0 / minOdd), 1, 99);

                            UpsertFootyStatsMatch(
                                home,
                                away,
                                tip,
                                prob,
                                "-",
                                "-",
                                ref mergedRows);
                        }
                        catch
                        {
                            invalidRows++;
                        }
                    }
                }
            }

            foreach (string url in urls)
            {
                Console.WriteLine();
                Console.WriteLine($"[FootyStats] Loading {url}");

                HtmlDocument? soup =
                    await GetPageFromFlareSolverr(url);

                if (soup == null)
                {
                    Console.WriteLine($"[FootyStats] Failed to load page: {url}");
                    continue;
                }

                var rows =
                    soup.DocumentNode.SelectNodes(
                        "//div[contains(concat(' ', normalize-space(@class), ' '), ' betWrapper ') and not(@data-id='-1')]");

                if (rows != null)
                {
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

                            if (string.IsNullOrWhiteSpace(fixture) ||
                                string.IsNullOrWhiteSpace(dateText))
                            {
                                continue;
                            }

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

                            string upperMarket = market.ToUpperInvariant();

                            string tip = upperMarket switch
                            {
                                "HOME WIN" => "1",
                                "DRAW" => "X",
                                "AWAY WIN" => "2",
                                _ => "-"
                            };

                            string bttsMarket = upperMarket switch
                            {
                                "BTTS YES" => "YES",
                                "BTTS NO" => "NO",
                                _ => "-"
                            };

                            string goalsMarket = upperMarket switch
                            {
                                "OVER 1.5 GOALS" => "O1.5",
                                "UNDER 1.5 GOALS" => "U1.5",
                                "OVER 2.5 GOALS" => "O2.5",
                                "UNDER 2.5 GOALS" => "U2.5",
                                "OVER 3.5 GOALS" => "O3.5",
                                "UNDER 3.5 GOALS" => "U3.5",
                                _ => "-"
                            };

                            if (tip == "-" &&
                                bttsMarket == "-" &&
                                goalsMarket == "-")
                            {
                                continue;
                            }

                            UpsertFootyStatsMatch(
                                teams[0].Trim(),
                                teams[1].Trim(),
                                tip,
                                null,
                                bttsMarket,
                                goalsMarket,
                                ref mergedRows);
                        }
                        catch (Exception ex)
                        {
                            invalidRows++;
                            Console.WriteLine(
                                $"[FootyStats] Row error: {ex.Message}");
                        }
                    }
                }

                // Also parse mathematical fixture-item rows (present on /predictions/mathematical and bottom of prediction pages)
                var mathItems = soup.DocumentNode.SelectNodes(
                    "//li[contains(concat(' ', normalize-space(@class), ' '), ' fixture-item ')]");

                if (mathItems != null)
                {
                    foreach (HtmlNode item in mathItems)
                    {
                        try
                        {
                            string fixture = CleanPredictionText(
                                item.SelectSingleNode(".//div[contains(@class,'match-name')]")?.InnerText);
                            string dateText = CleanPredictionText(
                                item.SelectSingleNode(".//div[contains(@class,'match-time')]")?.InnerText);

                            if (string.IsNullOrWhiteSpace(fixture) ||
                                string.IsNullOrWhiteSpace(dateText))
                            {
                                continue;
                            }

                            if (!FootyStatsDateMatches(dateText, parsedDate))
                            {
                                wrongDateRows++;
                                continue;
                            }

                            string[] teams = Regex.Split(
                                fixture,
                                @"\s+vs\.?\s+",
                                RegexOptions.IgnoreCase);

                            if (teams.Length != 2 ||
                                string.IsNullOrWhiteSpace(teams[0]) ||
                                string.IsNullOrWhiteSpace(teams[1]))
                            {
                                continue;
                            }

                            string betText = CleanPredictionText(
                                item.SelectSingleNode(".//ul[contains(@class,'bet-items')]/li[1]")?.InnerText)
                                .ToUpperInvariant();

                            string bttsMarket = "-";
                            string goalsMarket = "-";

                            if (betText.Contains("BTTS"))
                            {
                                var pctMatch = Regex.Match(betText, @"(\d{1,3})%");
                                if (pctMatch.Success && int.TryParse(pctMatch.Groups[1].Value, out int pct))
                                    bttsMarket = pct >= 50 ? "YES" : "NO";
                            }
                            else if (betText.Contains("OVER 2.5"))
                                goalsMarket = "O2.5";
                            else if (betText.Contains("UNDER 2.5"))
                                goalsMarket = "U2.5";
                            else if (betText.Contains("OVER 1.5"))
                                goalsMarket = "O1.5";
                            else if (betText.Contains("UNDER 1.5"))
                                goalsMarket = "U1.5";
                            else if (betText.Contains("OVER 3.5"))
                                goalsMarket = "O3.5";
                            else if (betText.Contains("UNDER 3.5"))
                                goalsMarket = "U3.5";

                            if (bttsMarket == "-" && goalsMarket == "-")
                                continue;

                            UpsertFootyStatsMatch(
                                teams[0].Trim(),
                                teams[1].Trim(),
                                "-",
                                null,
                                bttsMarket,
                                goalsMarket,
                                ref mergedRows);
                        }
                        catch
                        {
                            invalidRows++;
                        }
                    }
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
            @"(?<day>\d{1,2})(?:st|nd|rd|th)?\s+(?<month>[A-Za-z]+)|(?<month>[A-Za-z]+)\s+(?<day>\d{1,2})\b",
            RegexOptions.IgnoreCase);

        if (!match.Success ||
            !int.TryParse(
                match.Groups["day"].Value,
                out int day) ||
            !DateTime.TryParseExact(
                match.Groups["month"].Value,
                new[] { "MMMM", "MMM" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime month))
        {
            return false;
        }

        return day == target.Day &&
               month.Month == target.Month;
    }

    private static bool TryParseFirstDouble(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = Regex.Match(text, @"(\d+\.\d+)");
        return match.Success &&
               double.TryParse(
                   match.Groups[1].Value,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out value);
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
