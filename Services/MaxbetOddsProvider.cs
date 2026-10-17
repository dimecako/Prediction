using System.Net;
using System.Text.Json;

namespace Prediction.Services;

public sealed record BookmakerMatch(
    string Home,
    string Away,
    DateTime KickoffUtc,
    IReadOnlyDictionary<string, double> Odds);

// Reads the public Maxbet MK football offer. Odds are keyed by Maxbet tip type code:
// 1/2/3 = 1X2, 7/8/9 = 1X/12/X2, 272/273 = GG/NG, 22 = 0-2 goals, 24 = 3+ goals,
// 242 = 2+ goals, 25 = 4+ goals.
public static class MaxbetOddsProvider
{
    private const string OfferUrl = "https://www.maxbet.mk/restapi/offer/mk/sport/S/mob";

    public static async Task<IReadOnlyList<BookmakerMatch>> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        await using var stream = await client.GetStreamAsync(OfferUrl, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var matches = new List<BookmakerMatch>();

        if (!document.RootElement.TryGetProperty("esMatches", out var items))
            return matches;

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("blocked", out var blocked) &&
                blocked.ValueKind == JsonValueKind.True)
                continue;

            string? home = item.GetProperty("home").GetString();
            string? away = item.GetProperty("away").GetString();

            if (string.IsNullOrWhiteSpace(home) ||
                string.IsNullOrWhiteSpace(away) ||
                !item.TryGetProperty("kickOffTime", out var kickoff) ||
                !item.TryGetProperty("odds", out var oddsElement))
                continue;

            var odds = new Dictionary<string, double>();
            foreach (var odd in oddsElement.EnumerateObject())
            {
                if (odd.Value.ValueKind == JsonValueKind.Number &&
                    odd.Value.TryGetDouble(out double value) &&
                    value > 1.0)
                    odds[odd.Name] = value;
            }

            matches.Add(new BookmakerMatch(
                home,
                away,
                DateTimeOffset.FromUnixTimeMilliseconds(kickoff.GetInt64()).UtcDateTime,
                odds));
        }

        return matches;
    }

    public static double? GetOdds(BookmakerMatch match, string market, string pick)
    {
        string? key = (market, pick) switch
        {
            ("1X2", "1") => "1",
            ("1X2", "X") => "2",
            ("1X2", "2") => "3",
            ("DC", "1X") => "7",
            ("DC", "X2") => "9",
            ("BTTS", "YES") => "272",
            ("BTTS", "NO") => "273",
            ("Goals", "U2.5") => "22",
            ("Goals", "O2.5") => "24",
            ("Goals", "O1.5") => "242",
            ("Goals", "O3.5") => "25",
            _ => null
        };

        return key != null && match.Odds.TryGetValue(key, out double value)
            ? value
            : null;
    }
}
