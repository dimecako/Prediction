using System.Globalization;
using System.Text.Json;
using Prediction.Models;

namespace Prediction.Services;

public sealed class OpenFootballResultProvider : IMatchResultProvider
{
    private readonly HttpClient _httpClient;

    private static readonly string[] LeagueFiles =
    {
        "en.1.json",
        "de.1.json",
        "es.1.json",
        "it.1.json",
        "fr.1.json",
        "nl.1.json",
        "pt.1.json"
    };

    public string Name => "OpenFootball";

    public OpenFootballResultProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExternalMatchResult>();

        string season = GetSeason(date);

        foreach (string leagueFile in LeagueFiles)
        {
            string url =
                $"https://raw.githubusercontent.com/openfootball/football.json/master/{season}/{leagueFile}";

            try
            {
                using var response = await _httpClient.GetAsync(
                    url,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    continue;

                await using var stream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                using var document =
                    await JsonDocument.ParseAsync(
                        stream,
                        cancellationToken: cancellationToken);

                if (!document.RootElement.TryGetProperty(
                        "matches",
                        out var matches))
                {
                    continue;
                }

                foreach (var match in matches.EnumerateArray())
                {
                    if (!TryParseMatch(match, date, leagueFile, out var result))
                        continue;

                    results.Add(result);
                }
            }
            catch (Exception ex) when (
                ex is HttpRequestException ||
                ex is JsonException)
            {
                Console.WriteLine(
                    $"[OPENFOOTBALL] Failed {leagueFile}: {ex.Message}");
            }
        }

        return results;
    }

    private static bool TryParseMatch(
        JsonElement match,
        DateOnly requestedDate,
        string leagueFile,
        out ExternalMatchResult result)
    {
        result = null!;

        if (match.ValueKind != JsonValueKind.Object)
            return false;

        if (!match.TryGetProperty("date", out var dateElement))
            return false;

        string? dateText = dateElement.GetString();

        if (!DateOnly.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var matchDate))
        {
            return false;
        }

        if (matchDate != requestedDate)
            return false;

        if (!match.TryGetProperty("team1", out var homeElement) ||
            !match.TryGetProperty("team2", out var awayElement) ||
            !match.TryGetProperty("score", out var scoreElement))
        {
            return false;
        }

        JsonElement ftElement;

        if (scoreElement.ValueKind == JsonValueKind.Object)
        {
            if (!scoreElement.TryGetProperty("ft", out ftElement))
                return false;
        }
        else if (scoreElement.ValueKind == JsonValueKind.Array)
        {
            ftElement = scoreElement;
        }
        else
        {
            return false;
        }

        if (ftElement.ValueKind != JsonValueKind.Array ||
            ftElement.GetArrayLength() < 2 ||
            ftElement[0].ValueKind != JsonValueKind.Number ||
            ftElement[1].ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        int homeGoals = ftElement[0].GetInt32();
        int awayGoals = ftElement[1].GetInt32();

        string homeTeam = homeElement.GetString() ?? string.Empty;
        string awayTeam = awayElement.GetString() ?? string.Empty;

        DateTime? kickoffUtc = null;

        if (match.TryGetProperty("time", out var timeElement))
        {
            string? timeText = timeElement.GetString();

            if (TimeOnly.TryParseExact(
                    timeText,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var time))
            {
                kickoffUtc = requestedDate
                    .ToDateTime(time)
                    .SpecifyUtc();
            }
        }

        result = new ExternalMatchResult
        {
            ExternalId =
                $"openfootball:{leagueFile}:{requestedDate:yyyyMMdd}:{Normalize(homeTeam)}:{Normalize(awayTeam)}",

            HomeTeam = homeTeam,
            AwayTeam = awayTeam,

            KickoffUtc = kickoffUtc,

            HomeGoals = homeGoals,
            AwayGoals = awayGoals,

            Source = "OpenFootball"
        };

        return true;
    }

    private static string GetSeason(DateOnly date)
    {
        int startYear =
            date.Month >= 7
                ? date.Year
                : date.Year - 1;

        int endYear = (startYear + 1) % 100;

        return $"{startYear}-{endYear:00}";
    }

    private static string Normalize(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", "-");
    }
}

internal static class DateTimeExtensions
{
    public static DateTime SpecifyUtc(this DateTime value)
    {
        return DateTime.SpecifyKind(
            value,
            DateTimeKind.Utc);
    }
}