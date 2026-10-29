using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prediction.Models;

namespace Prediction.Services;

public sealed class SoccerVistaResultProvider(HttpClient httpClient) : IMatchResultProvider
{
    public string Name => "SoccerVista";

    public async Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        string feed = await GetAsync(
            $"events/by/date/{date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}/",
            cancellationToken);
        var tournaments = JArray.Parse(feed);
        var results = new List<ExternalMatchResult>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tournament in tournaments)
        {
            if (tournament is not JObject group || group["events"] is not JArray events)
                throw new JsonException("SoccerVista tournament is missing the events array.");

            foreach (var item in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item is not JObject row)
                    throw new JsonException("SoccerVista event must be an object.");
                if (!IsFinished(row, includeDetailFlags: false))
                    continue;

                string id = RequiredString(row, "id");
                string home = RequiredString(row, "homeTeam");
                string away = RequiredString(row, "awayTeam");
                if (row["score"]?.Type == JTokenType.Null)
                {
                    Console.WriteLine($"[SOCCERVISTA] {id} {home} vs {away}: finished but no actual score; left unresolved.");
                    continue;
                }
                var score = ReadScore(row, "score");
                if (!seen.Add(id))
                    throw new JsonException($"SoccerVista duplicate finished fixture ID: {id}.");

                // The daily feed does not distinguish regulation time from extra-time outcomes.
                JObject detail;
                try
                {
                    detail = await GetDetailAsync(row, id, cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                    ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    string error = $"{id} {home} vs {away}: {ex.Message}";
                    Console.WriteLine($"[SOCCERVISTA] {error}; left unresolved.");
                    errors.Add(error);
                    continue;
                }
                if (RequiredString(detail, "id") != id ||
                    detail["homeParticipant"] is not JObject homeParticipant ||
                    detail["awayParticipant"] is not JObject awayParticipant ||
                    RequiredString(homeParticipant, "name") != home ||
                    RequiredString(awayParticipant, "name") != away)
                    throw new JsonException($"SoccerVista fixture identity changed for {id}.");

                if (!DateOnly.TryParseExact(RequiredString(detail, "date"),
                    ["dddd dd MMMM yyyy", "dddd d MMMM yyyy"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var sourceDate) || sourceDate != date)
                    throw new JsonException($"SoccerVista fixture {id} does not match requested date {date:yyyy-MM-dd}.");

                if (detail["status"] is not JObject status)
                    throw new JsonException($"SoccerVista fixture {id} is missing its status.");
                string statusCode = RequiredString(status, "short_name");
                if (!IsFinished(detail, includeDetailFlags: true) || statusCode != "FIN")
                {
                    Console.WriteLine($"[SOCCERVISTA] {id} {home} vs {away}: status={statusCode}; not a confirmed regulation-time FT.");
                    continue;
                }

                if (ReadScore(detail, "finalScore") != score)
                    throw new JsonException($"SoccerVista conflicting daily/detail final score for {id}.");
                if (row["timeStart"]?.Type != JTokenType.Integer ||
                    row["timeStart"]!.Value<long>() is < -62135596800 or > 253402300799)
                    throw new JsonException($"SoccerVista fixture {id} has an invalid kickoff timestamp.");

                results.Add(new ExternalMatchResult
                {
                    ExternalId = $"soccervista:{id}",
                    HomeTeam = home,
                    AwayTeam = away,
                    KickoffUtc = DateTimeOffset.FromUnixTimeSeconds(row["timeStart"]!.Value<long>()).UtcDateTime,
                    HomeGoals = score.Home,
                    AwayGoals = score.Away,
                    Source = Name
                });
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count > 0)
            throw new PartialMatchResultException(results, errors);
        return results;
    }

    private async Task<JObject> GetDetailAsync(JObject row, string id, CancellationToken cancellationToken)
    {
        string detailPath = $"events/{Uri.EscapeDataString(id)}/live-updates/";
        try
        {
            return JObject.Parse(await GetAsync(detailPath, cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException ||
            ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"[SOCCERVISTA] {id}: {detailPath} failed: {ex.Message}; checking match page.");
        }

        string homeSlug = Uri.EscapeDataString(RequiredString(row, "homeTeamUrl"));
        string awaySlug = Uri.EscapeDataString(RequiredString(row, "awayTeamUrl"));
        string pagePath = $"event/{homeSlug}-{awaySlug}/{Uri.EscapeDataString(id)}/";
        var document = new HtmlDocument();
        document.LoadHtml(await GetAsync(pagePath, cancellationToken));
        var scripts = document.DocumentNode.SelectNodes("//script");
        if (scripts == null)
            throw new JsonException($"SoccerVista {pagePath} has no embedded fixture data.");

        var matches = scripts.SelectMany(script => Regex.Matches(script.InnerText,
            @"\bgetEventOdds\s*\(\s*JSON\.parse\s*\(\s*'(?<json>(?:\\.|[^'\\])*)'\s*\)\s*\)",
            RegexOptions.None, TimeSpan.FromSeconds(1)).Cast<Match>()).ToList();
        if (matches.Count != 1)
            throw new JsonException($"SoccerVista {pagePath} must contain exactly one embedded fixture object.");

        // Decode the JavaScript string's JSON escapes, then parse the fixture object itself.
        string? json = JsonConvert.DeserializeObject<string>($"\"{matches[0].Groups["json"].Value}\"");
        if (json == null)
            throw new JsonException($"SoccerVista {pagePath} has null fixture data.");
        return JObject.Parse(json);
    }

    private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.soccervista.com/{path}");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"GET {request.RequestUri}: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null, response.StatusCode);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static bool IsFinished(JObject row, bool includeDetailFlags)
    {
        bool finished = RequiredBoolean(row, "isFinished");
        bool live = RequiredBoolean(row, "isLive");
        bool scheduled = RequiredBoolean(row, "isScheduled");
        bool paused = RequiredBoolean(row, "isPaused");
        bool postponed = RequiredBoolean(row, "isPostponed");
        bool cancelled = RequiredBoolean(row, "isCancelled");
        bool otherPeriod = includeDetailFlags &&
            (RequiredBoolean(row, "isPenaltyShootout") | RequiredBoolean(row, "isHalfTime"));
        return finished && !live && !scheduled && !paused && !postponed && !cancelled && !otherPeriod;
    }

    private static bool RequiredBoolean(JObject row, string field) =>
        row[field]?.Type == JTokenType.Boolean
            ? row[field]!.Value<bool>()
            : throw new JsonException($"SoccerVista is missing boolean field {field}.");

    private static string RequiredString(JObject row, string field) =>
        row[field]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(row[field]!.Value<string>())
            ? row[field]!.Value<string>()!.Trim()
            : throw new JsonException($"SoccerVista is missing string field {field}.");

    private static (int Home, int Away) ReadScore(JObject row, string field)
    {
        string[] parts = RequiredString(row, field).Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int home) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int away))
            throw new JsonException($"SoccerVista has an invalid {field}.");
        return (home, away);
    }
}
