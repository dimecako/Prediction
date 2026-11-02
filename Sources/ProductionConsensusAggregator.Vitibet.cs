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
    public async Task<List<SiteMatch>> ParseVitibetAsync(
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
                    $"[Vitibet] Invalid target date: {targetDate}");

                return matches;
            }

            string url =
                $"https://www.vitibet.com/index.php?clanek=quicktips&sekce=fotbal&lang=en&date={parsedDate:yyyy-MM-dd}";

            Console.WriteLine();
            Console.WriteLine($"[Vitibet] Loading {url}");

            var soup = await GetPageFromFlareSolverr(url);

            if (soup == null)
            {
                Console.WriteLine("[Vitibet] Failed to load page.");
                return matches;
            }

            var rows =
                soup.DocumentNode.SelectNodes(
                    "//a[contains(concat(' ', normalize-space(@class), ' '), ' livescore-match-row ')]");

            if (rows == null || rows.Count == 0)
            {
                Console.WriteLine("[Vitibet] Match rows not found.");
                return matches;
            }

            Console.WriteLine(
                $"[Vitibet] Raw match rows: {rows.Count}");

            int invalidRows = 0;
            int duplicateRows = 0;
            int wrongDateRows = 0;

            foreach (var row in rows)
            {
                try
                {
                    // -----------------------------
                    // DATE
                    // -----------------------------

                    var timeNode =
                        row.SelectSingleNode(
                            ".//span[contains(@class,'local-time')]");

                    string dataTime =
                        timeNode?.GetAttributeValue(
                            "data-time",
                            "") ?? "";

                    if (!string.IsNullOrWhiteSpace(dataTime) &&
                        DateTimeOffset.TryParse(
                            dataTime,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out DateTimeOffset matchDateTime))
                    {
                        if (matchDateTime.Date != parsedDate.Date)
                        {
                            wrongDateRows++;
                            continue;
                        }
                    }

                    // -----------------------------
                    // TEAMS
                    // -----------------------------

                    var teamNodes =
                        row.SelectNodes(
                            ".//span[contains(@class,'livescore-team-name')]");

                    if (teamNodes == null ||
                        teamNodes.Count < 2)
                    {
                        invalidRows++;
                        continue;
                    }

                    string home =
                        CleanVitibetText(
                            teamNodes[0].InnerText);

                    string away =
                        CleanVitibetText(
                            teamNodes[1].InnerText);

                    if (string.IsNullOrWhiteSpace(home) ||
                        string.IsNullOrWhiteSpace(away))
                    {
                        invalidRows++;
                        continue;
                    }

                    // -----------------------------
                    // 1 / X / 2 PROBABILITIES
                    // -----------------------------

                    var probabilityItems =
                        row.SelectNodes(
                            ".//div[contains(@class,'prob-item') and contains(@class,'pct-item')]");

                    if (probabilityItems == null)
                    {
                        invalidRows++;
                        continue;
                    }

                    int? p1 = null;
                    int? px = null;
                    int? p2 = null;

                    foreach (var item in probabilityItems)
                    {
                        string head =
                            CleanVitibetText(
                                item.SelectSingleNode(
                                    ".//div[contains(@class,'prob-head')]")
                                    ?.InnerText);

                        string value =
                            CleanVitibetText(
                                item.SelectSingleNode(
                                    ".//div[contains(@class,'prob-val')]")
                                    ?.InnerText);

                        if (!TryParseVitibetPercent(
                                value,
                                out int percent))
                        {
                            continue;
                        }

                        if (head == "1")
                            p1 = percent;
                        else if (head == "X")
                            px = percent;
                        else if (head == "2")
                            p2 = percent;
                    }

                    if (!p1.HasValue ||
                        !px.HasValue ||
                        !p2.HasValue)
                    {
                        invalidRows++;
                        continue;
                    }

                    // -----------------------------
                    // RESULT TIP
                    // -----------------------------

                    int maxProb =
                        Math.Max(
                            p1.Value,
                            Math.Max(
                                px.Value,
                                p2.Value));

                    string tip;

                    if (maxProb == p1.Value)
                        tip = "1";
                    else if (maxProb == px.Value)
                        tip = "X";
                    else
                        tip = "2";

                    // -----------------------------
                    // PREDICTED SCORE
                    // Desktop version only
                    // -----------------------------

                    var scoreNode =
                        row.SelectSingleNode(
                            ".//div[contains(@class,'livescore-match-score-col')]//div[contains(@class,'livescore-score-combined')]");

                    string score =
                        CleanVitibetText(
                            scoreNode?.InnerText);

                    string btts = "-";
                    string goalsMarket = "-";

                    if (TryParseVitibetScore(
                            score,
                            out int homeGoals,
                            out int awayGoals))
                    {
                        btts =
                            homeGoals > 0 &&
                            awayGoals > 0
                                ? "YES"
                                : "NO";

                        int totalGoals =
                            homeGoals + awayGoals;

                        goalsMarket =
                            totalGoals > 2
                                ? "O2.5"
                                : "U2.5";
                    }
                    else
                    {
                        score = "-";
                    }

                    // -----------------------------
                    // SITE MATCH
                    // -----------------------------

                    var match =
                        new SiteMatch
                        {
                            SiteName = "Vitibet",

                            HomeTeam = home,
                            AwayTeam = away,

                            Tip = tip,
                            Prob = maxProb,

                            Score = score,
                            Btts = btts,

                            BttsMarket = btts,
                            GoalsMarket = goalsMarket
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
                        $"[Vitibet] Row error: {ex.Message}");
                }
            }

            Console.WriteLine(
                $"[Vitibet] Other dates ignored: {wrongDateRows}");

            Console.WriteLine(
                $"[Vitibet] Invalid rows ignored: {invalidRows}");

            Console.WriteLine(
                $"[Vitibet] Duplicate rows ignored: {duplicateRows}");

            Console.WriteLine(
                $"[Vitibet] FINAL MATCHES: {matches.Count}");

            foreach (var match in matches.Take(20))
            {
                Console.WriteLine(
                    $"[Vitibet] " +
                    $"{match.HomeTeam} vs {match.AwayTeam} " +
                    $"=> {match.Tip} ({match.Prob}%) " +
                    $"Score={match.Score} " +
                    $"BTTS={match.Btts} " +
                    $"Goals={match.GoalsMarket}");
            }

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Vitibet ERROR] {ex}");

            return matches;
        }
    }

    private static string CleanVitibetText(
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

    private static bool TryParseVitibetPercent(
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

        return int.TryParse(
                   text,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out percent) &&
               percent >= 0 &&
               percent <= 100;
    }

    private static bool TryParseVitibetScore(
        string value,
        out int homeGoals,
        out int awayGoals)
    {
        homeGoals = 0;
        awayGoals = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match =
            Regex.Match(
                value.Trim(),
                @"^(\d+)\s*-\s*(\d+)$");

        if (!match.Success)
            return false;

        return
            int.TryParse(
                match.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out homeGoals)
            &&
            int.TryParse(
                match.Groups[2].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out awayGoals);
    }
}