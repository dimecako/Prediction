using System.Globalization;
using HtmlAgilityPack;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParseSoccerVistaAsync(
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
                    $"[SoccerVista] Invalid target date: {targetDate}");

                return matches;
            }

            string soccerVistaDate =
                parsedDate.ToString(
                    "dd-MM-yyyy",
                    CultureInfo.InvariantCulture);

            string url =
                $"https://www.soccervista.com/events/by/date/{soccerVistaDate}/";

            Console.WriteLine();
            Console.WriteLine($"[SoccerVista] Loading {url}");

            string json = await GetRawFromFlareSolverr(url);

            if (string.IsNullOrWhiteSpace(json))
            {
                Console.WriteLine("[SoccerVista] Empty response.");
                return matches;
            }

            json = ExtractSoccerVistaJson(json);

            var tournaments = JArray.Parse(json);
            int invalidRows = 0;
            int duplicateRows = 0;

            foreach (JToken tournament in tournaments)
            {
                if (tournament["events"] is not JArray events)
                    continue;

                foreach (JToken item in events)
                {
                    try
                    {
                        string home =
                            item["homeTeam"]?.ToString().Trim() ?? "";

                        string away =
                            item["awayTeam"]?.ToString().Trim() ?? "";

                        string tip =
                            item["prediction1x2"]?.ToString().Trim()
                            ?? "";

                        if (string.IsNullOrWhiteSpace(home) ||
                            string.IsNullOrWhiteSpace(away) ||
                            (tip != "1" && tip != "X" && tip != "2"))
                        {
                            invalidRows++;
                            continue;
                        }

                        string score =
                            NormalizeSoccerVistaScore(
                                item["predictionScore"]?.ToString());

                        string goalsMarket =
                            item["predictionOu"]?.ToString()
                                .Trim()
                                .ToUpperInvariant() switch
                            {
                                "O" => "O2.5",
                                "U" => "U2.5",
                                _ => GetGoalMarket(score)
                            };

                        var match = new SiteMatch
                        {
                            SiteName = "SoccerVista",
                            SourceMatchId = string.IsNullOrWhiteSpace(item["id"]?.ToString())
                                ? null : $"soccervista:{item["id"]!.ToString().Trim()}",
                            HomeTeam = home,
                            AwayTeam = away,
                            Tip = tip,
                            Score = score,
                            Btts = GetBttsMarket(score),
                            BttsMarket = GetBttsMarket(score),
                            GoalsMarket = goalsMarket
                        };

                        bool exists = matches.Any(existing =>
                            string.Equals(
                                existing.HomeTeam,
                                match.HomeTeam,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                existing.AwayTeam,
                                match.AwayTeam,
                                StringComparison.OrdinalIgnoreCase));

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
                            $"[SoccerVista] Event error: {ex.Message}");
                    }
                }
            }

            Console.WriteLine(
                $"[SoccerVista] Parsed matches: {matches.Count}");
            Console.WriteLine(
                $"[SoccerVista] Invalid rows: {invalidRows}");
            Console.WriteLine(
                $"[SoccerVista] Duplicate rows ignored: {duplicateRows}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SoccerVista ERROR] {ex}");
        }

        return matches;
    }

    private static string NormalizeSoccerVistaScore(string? score)
    {
        if (string.IsNullOrWhiteSpace(score))
            return "-";

        string normalized = score.Trim().Replace(':', '-');

        string[] parts = normalized.Split('-');

        return parts.Length == 2 &&
               int.TryParse(parts[0], out _) &&
               int.TryParse(parts[1], out _)
            ? normalized
            : "-";
    }

    private static string ExtractSoccerVistaJson(string response)
    {
        string json = response.Trim();

        if (json.StartsWith("<", StringComparison.Ordinal))
        {
            var document = new HtmlDocument();
            document.LoadHtml(json);

            json = HtmlEntity.DeEntitize(
                    document.DocumentNode.InnerText)
                .Trim();
        }

        int arrayStart = json.IndexOf('[');
        int arrayEnd = json.LastIndexOf(']');

        if (arrayStart < 0 || arrayEnd < arrayStart)
        {
            throw new FormatException(
                "SoccerVista response does not contain a JSON array.");
        }

        return json.Substring(
            arrayStart,
            arrayEnd - arrayStart + 1);
    }
}
