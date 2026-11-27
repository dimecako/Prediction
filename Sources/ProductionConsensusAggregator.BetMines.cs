using System.Globalization;
using Newtonsoft.Json.Linq;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParseBetMinesAsync()
    {
        var url = BuildBetMinesUrl(targetDate);
        Console.WriteLine();
        Console.WriteLine($"[BetMines] Loading {url}");

        string raw = await GetRawFromFlareSolverr(url);
        var matches = ParseBetMinesApiResponse(raw, targetDate);

        Console.WriteLine($"[BetMines] Parsed matches: {matches.Count}");
        return matches;
    }

    private static string BuildBetMinesUrl(string targetDate)
    {
        DateTime date = DateTime.ParseExact(
            targetDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        string from = date.ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture);
        string to = date.AddDays(1).ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture);

        return $"https://api.betmines.com/betmines/v1/fixtures/web" +
               $"?dateFormat=extended&platform=website&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
    }

    public static List<SiteMatch> ParseBetMinesApiResponse(string response, string targetDate)
    {
        var matches = new List<SiteMatch>();
        if (string.IsNullOrWhiteSpace(response))
            return matches;

        JArray rows;
        string payload = response.Trim();
        if (payload.StartsWith("[", StringComparison.Ordinal))
        {
            rows = JArray.Parse(payload);
        }
        else
        {
            int start = payload.IndexOf('[');
            int end = payload.LastIndexOf(']');
            if (start < 0 || end <= start)
                return matches;
            rows = JArray.Parse(payload.Substring(start, end - start + 1));
        }

        foreach (JToken row in rows)
        {
            string home = row["localTeam"]?["name"]?.ToString().Trim() ?? "";
            string away = row["visitorTeam"]?["name"]?.ToString().Trim() ?? "";
            string country = row["league"]?["country"]?["name"]?.ToString().Trim() ?? "";
            string league = row["league"]?["name"]?.ToString().Trim() ?? "";
            if (string.IsNullOrWhiteSpace(home) ||
                string.IsNullOrWhiteSpace(away) ||
                string.IsNullOrWhiteSpace(league))
            {
                continue;
            }

            string prediction = NormalizePrediction(row["prediction"]?.ToString());
            JObject probability = row["probability"] as JObject ?? new JObject();
            int? tipProbability = GetTipProbability(prediction, probability);

            string bttsMarket = "-";
            int btts = ReadProbability(probability, "btts");
            int noBtts = ReadProbability(probability, "btts_no");
            if (btts > 0 || noBtts > 0)
                bttsMarket = btts >= noBtts ? "GG" : "NG";

            string goalsMarket = "-";
            int over25 = ReadProbability(probability, "over_2_5");
            int under25 = ReadProbability(probability, "under_2_5");
            if (over25 > 0 || under25 > 0)
                goalsMarket = over25 >= under25 ? "O2.5" : "U2.5";

            matches.Add(new SiteMatch
            {
                SiteName = "BetMines",
                SourceMatchId = row["id"] == null
                    ? null
                    : $"betmines:{row["id"]!.ToString().Trim()}",
                League = string.IsNullOrWhiteSpace(country)
                    ? league
                    : $"{country.ToUpperInvariant()} {league.ToUpperInvariant()}",
                HomeTeam = home,
                AwayTeam = away,
                Tip = prediction,
                Prob = tipProbability,
                Score = "-",
                Btts = bttsMarket,
                BttsMarket = bttsMarket,
                GoalsMarket = goalsMarket
            });
        }

        return matches;
    }

    private static string NormalizePrediction(string? prediction)
    {
        if (string.IsNullOrWhiteSpace(prediction))
            return "-";

        return prediction.Trim().ToUpperInvariant() switch
        {
            "1" => "1",
            "X" => "X",
            "2" => "2",
            "12" => "12",
            "1X" => "1X",
            "X2" => "X2",
            "GG" => "-",
            "NG" => "-",
            "O25" or "OVER25" or "O2.5" => "-",
            "U25" or "UNDER25" or "U2.5" => "-",
            _ => "-"
        };
    }

    private static int? GetTipProbability(string tip, JObject probability)
    {
        int value = tip switch
        {
            "1" => ReadProbability(probability, "home"),
            "X" => ReadProbability(probability, "draw"),
            "2" => ReadProbability(probability, "away"),
            "12" => ReadProbability(probability, "home_away"),
            "1X" => ReadProbability(probability, "home_draw"),
            "X2" => ReadProbability(probability, "draw_away"),
            _ => 0
        };

        return value > 0 ? value : null;
    }

    private static int ReadProbability(JObject probability, string name)
    {
        JToken? value = probability[name];
        if (value != null && int.TryParse(
                value.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed))
        {
            return parsed;
        }

        return 0;
    }
}
