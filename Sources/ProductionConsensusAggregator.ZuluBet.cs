using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Playwright;
using System.Globalization;
using System.Text.RegularExpressions;

public partial class ProductionConsensusAggregator
{
    	    

    public async Task<List<SiteMatch>> ParseZuluBetAsync(
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
                    $"[ZuluBet] Invalid target date: {targetDate}");

                return matches;
            }

            string zuluDate =
                parsedDate.ToString(
                    "dd-MM-yyyy",
                    CultureInfo.InvariantCulture);

            string targetShortDate =
                parsedDate.ToString(
                    "dd-MM",
                    CultureInfo.InvariantCulture);

            string url =
                $"https://www.zulubet.com/tips-{zuluDate}.html";

            Console.WriteLine();
            Console.WriteLine(
                $"[ZuluBet] Loading {url}");

            var soup =
                await GetPageFromFlareSolverr(url);

            if (soup == null)
            {
                Console.WriteLine(
                    "[ZuluBet] Failed to load page.");

                return matches;
            }

            // ZuluBet match data lives in:
            // table.content_table > tr
            //
            // Direct TD layout:
            // 0  = date/time
            // 1  = Home - Away
            // 2  = compact probability table
            // 3  = probability 1
            // 4  = probability X
            // 5  = probability 2
            // 6  = ZuluBet tip
            // 7  = strength/rating
            // 8+ = odds/results

            var tables =
                soup.DocumentNode.SelectNodes(
                    "//table[contains(concat(' ', normalize-space(@class), ' '), ' content_table ')]");

            if (tables == null || tables.Count == 0)
            {
                Console.WriteLine(
                    "[ZuluBet] content_table not found.");

                return matches;
            }

            Console.WriteLine(
                $"[ZuluBet] Content tables: {tables.Count}");

            int rawRows = 0;
            int wrongDate = 0;
            int invalidRows = 0;
            int duplicateRows = 0;

            foreach (var table in tables)
            {
                var rows =
                    table.SelectNodes(
                        ".//tr");

                if (rows == null)
                    continue;

                foreach (var row in rows)
                {
                    try
                    {
                        // IMPORTANT:
                        // direct TD children only
                        // so nested prob_table TDs are not counted here.
                        var cells =
                            row.SelectNodes(
                                "./td");

                        if (cells == null ||
                            cells.Count < 7)
                        {
                            continue;
                        }

                        string dateText =
                            CleanZuluBetText(
                                cells[0].InnerText);

                        if (!dateText.Contains(
                                targetShortDate,
                                StringComparison.Ordinal))
                        {
                            // Ignore headers and rows for other dates.
                            if (Regex.IsMatch(
                                    dateText,
                                    @"\d{2}-\d{2}"))
                            {
                                wrongDate++;
                            }

                            continue;
                        }

                        rawRows++;

                        string fixture =
                            CleanZuluBetText(
                                cells[1].InnerText);

                        if (string.IsNullOrWhiteSpace(
                                fixture))
                        {
                            invalidRows++;
                            continue;
                        }

                        int separator =
                            fixture.IndexOf(
                                " - ",
                                StringComparison.Ordinal);

                        if (separator <= 0)
                        {
                            invalidRows++;
                            continue;
                        }

                        string home =
                            fixture
                                .Substring(
                                    0,
                                    separator)
                                .Trim();

                        string away =
                            fixture
                                .Substring(
                                    separator + 3)
                                .Trim();

                        if (string.IsNullOrWhiteSpace(home) ||
                            string.IsNullOrWhiteSpace(away))
                        {
                            invalidRows++;
                            continue;
                        }

                        string p1Text =
                            CleanZuluBetText(
                                cells[3].InnerText);

                        string pxText =
                            CleanZuluBetText(
                                cells[4].InnerText);

                        string p2Text =
                            CleanZuluBetText(
                                cells[5].InnerText);

                        if (!TryParseZuluBetPercent(
                                p1Text,
                                out int p1) ||
                            !TryParseZuluBetPercent(
                                pxText,
                                out int px) ||
                            !TryParseZuluBetPercent(
                                p2Text,
                                out int p2))
                        {
                            invalidRows++;
                            continue;
                        }

                        int maxProb =
                            Math.Max(
                                p1,
                                Math.Max(
                                    px,
                                    p2));

                        string tip;

                        if (maxProb == p1)
                        {
                            tip = "1";
                        }
                        else if (maxProb == px)
                        {
                            tip = "X";
                        }
                        else
                        {
                            tip = "2";
                        }

                        var match =
                            new SiteMatch
                            {
                                SiteName = "ZuluBet",

                                HomeTeam = home,
                                AwayTeam = away,

                                Tip = tip,
                                Prob = maxProb,

                                Score = "-",
                                Btts = "-",
                                BttsMarket = "-",
                                GoalsMarket = "-"
                            };

                        bool exists =
                            matches.Any(x =>
                                NormalizeName(x.HomeTeam) ==
                                NormalizeName(match.HomeTeam) &&
                                NormalizeName(x.AwayTeam) ==
                                NormalizeName(match.AwayTeam));

                        if (exists)
                        {
                            duplicateRows++;
                            continue;
                        }

                        matches.Add(match);
                    }
                    catch (Exception ex)
                    {
                        invalidRows++;

                        Console.WriteLine(
                            $"[ZuluBet] Row error: {ex.Message}");
                    }
                }
            }

            Console.WriteLine(
                $"[ZuluBet] Raw target-date rows: {rawRows}");

            Console.WriteLine(
                $"[ZuluBet] Other dates ignored: {wrongDate}");

            Console.WriteLine(
                $"[ZuluBet] Invalid rows ignored: {invalidRows}");

            Console.WriteLine(
                $"[ZuluBet] Duplicate rows ignored: {duplicateRows}");

            Console.WriteLine(
                $"[ZuluBet] FINAL MATCHES: {matches.Count}");

            foreach (var match in matches.Take(20))
            {
                Console.WriteLine(
                    $"[ZuluBet] " +
                    $"{match.HomeTeam} vs {match.AwayTeam} " +
                    $"=> {match.Tip} ({match.Prob}%)");
            }

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ZuluBet ERROR] {ex}");

            return matches;
        }
    }

    private static string CleanZuluBetText(
    string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string text =
            HtmlEntity.DeEntitize(value);

        text =
            Regex.Replace(
                text,
                @"\s+",
                " ");

        return text.Trim();
    }

    private static bool TryParseZuluBetPercent(
    string value,
    out int percent)
    {
        percent = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text =
            value
                .Replace("%", "")
                .Trim();

        if (!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out percent))
        {
            return false;
        }

        return percent >= 0 &&
            percent <= 100;
    }
}