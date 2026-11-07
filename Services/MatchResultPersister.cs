using System.ClientModel;
using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Infrastructure;
using Prediction.Models;

namespace Prediction.Services;

public sealed record MatchResultPersistenceSummary(
    int Matched, int Inserted, int Existing, int Unmatched, int HalfTimeUpdated, int Changes)
{
    public int IdentityConflicts { get; init; }
    public int ScoreConflicts { get; init; }
}

public static class MatchResultPersister
{
    public static async Task<MatchResultPersistenceSummary> PersistAsync(
        FootballDbContext db, DateOnly date, IReadOnlyCollection<ExternalMatchResult> sourceResults,
        Action<string> log)
    {
        int matched = 0, inserted = 0, existing = 0, unmatched = 0, halfTimeUpdated = 0;
        int identityConflicts = 0, scoreConflicts = 0;
        int aiSuggestions = 0;
        bool aiConfigurationNoticeWritten = false;
        const int maximumAiSuggestions = 20;
        string? aiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var resultMatches = await db.Matches.Include(x => x.Result)
            .Where(x => x.MatchDate == date).ToListAsync();
        var sourceSnapshots = await db.PredictionSnapshots.AsNoTracking()
            .Where(x => x.Match.MatchDate == date && x.SourceMatchId != null).ToListAsync();

        foreach (var external in sourceResults)
        {
            var resolution = ResultMatchResolver.Resolve(date, external, resultMatches, sourceSnapshots);
            if (resolution.Conflict)
            {
                identityConflicts++;
                log($"[RESULTS] Identity conflict: {external.Source} {external.ExternalId} | " +
                    $"{external.HomeTeam} vs {external.AwayTeam}; {resolution.Method}.");
                WriteConflictDiagnostics(date, external, resolution.Candidates, sourceSnapshots, log);
                if (!string.IsNullOrWhiteSpace(aiApiKey) && resolution.Candidates.Count > 0 &&
                    aiSuggestions < maximumAiSuggestions)
                {
                    aiSuggestions++;
                    try
                    {
                        var suggestion = await ResultMatchAiSuggester.SuggestAsync(
                            date, external, resolution.Candidates, sourceSnapshots, aiApiKey);
                        if (suggestion.MatchId is long suggestedId)
                        {
                            var candidate = resolution.Candidates.Single(x => x.Id == suggestedId);
                            log($"[RESULTS-AI] SUGGESTION ONLY: consider DB #{suggestedId} " +
                                $"({CleanAiLog(candidate.HomeTeam)} vs {CleanAiLog(candidate.AwayTeam)}); " +
                                $"{suggestion.Reason} " +
                                "No records changed; verify manually.");
                        }
                        else
                            log($"[RESULTS-AI] ABSTAINED: {suggestion.Reason} No records changed.");
                    }
                    catch (Exception ex) when (ex is ClientResultException or HttpRequestException or
                        JsonException or InvalidDataException or OperationCanceledException)
                    {
                        log($"[RESULTS-AI] Suggestion unavailable ({ex.GetType().Name}): {CleanAiLog(ex.Message)}");
                    }
                }
                else if (resolution.Candidates.Count > 0 && string.IsNullOrWhiteSpace(aiApiKey) &&
                         !aiConfigurationNoticeWritten)
                {
                    log("[RESULTS-AI] Suggestions are disabled; set OPENAI_API_KEY to request optional AI suggestions. " +
                        "No match will be applied automatically.");
                    aiConfigurationNoticeWritten = true;
                }
                else if (resolution.Candidates.Count > 0 && aiSuggestions >= maximumAiSuggestions)
                    log($"[RESULTS-AI] Suggestion limit ({maximumAiSuggestions}) reached; remaining conflicts require manual review.");
                CommandExecution.Fail();
            }
            else if (resolution.Matches.Count > 0 && resolution.Method.Contains("source ID"))
                log($"[RESULTS] Linked by {resolution.Method}: {external.Source} {external.ExternalId} -> " +
                    string.Join(", ", resolution.Matches.Select(x => $"#{x.Id}")) + ".");

            if (resolution.Matches.Count == 0)
            {
                log($"[UNMATCHED] {external.HomeTeam} | {external.AwayTeam} | " +
                    $"{external.HomeGoals}-{external.AwayGoals}");
                unmatched++;
                continue;
            }

            foreach (var match in resolution.Matches)
            {
                matched++;
                if (match.Result != null)
                {
                    if (match.Result.HomeGoals != external.HomeGoals ||
                        match.Result.AwayGoals != external.AwayGoals)
                    {
                        scoreConflicts++;
                        log($"[RESULTS] Conflicting stored final score for #{match.Id}: " +
                            $"stored={match.Result.HomeGoals}-{match.Result.AwayGoals} " +
                            $"(source={match.Result.Source}, captured UTC={match.Result.CollectedAtUtc:O}), " +
                            $"{external.Source} {external.ExternalId} regulation FT={external.HomeGoals}-{external.AwayGoals}; " +
                            "not overwritten. Verify source fixture identity and regulation-time evidence; " +
                            "score differences alone do not prove extra time or penalties.");
                        CommandExecution.Fail();
                        existing++;
                        continue;
                    }

                    if (external.HalfTimeHomeGoals.HasValue && match.Result.HalfTimeHomeGoals.HasValue &&
                            external.HalfTimeHomeGoals != match.Result.HalfTimeHomeGoals ||
                        external.HalfTimeAwayGoals.HasValue && match.Result.HalfTimeAwayGoals.HasValue &&
                            external.HalfTimeAwayGoals != match.Result.HalfTimeAwayGoals)
                    {
                        scoreConflicts++;
                        log($"[RESULTS] Conflicting stored half-time score for #{match.Id}; not overwritten.");
                        CommandExecution.Fail();
                        existing++;
                        continue;
                    }
                    if (external.HalfTimeHomeGoals.HasValue && external.HalfTimeAwayGoals.HasValue &&
                        (!match.Result.HalfTimeHomeGoals.HasValue || !match.Result.HalfTimeAwayGoals.HasValue))
                    {
                        match.Result.HalfTimeHomeGoals = external.HalfTimeHomeGoals;
                        match.Result.HalfTimeAwayGoals = external.HalfTimeAwayGoals;
                        halfTimeUpdated++;
                    }
                    log($"[EXISTS] #{match.Id} | {match.HomeTeam} | {match.AwayTeam} | " +
                        $"{match.Result.HomeGoals}-{match.Result.AwayGoals}");
                    existing++;
                    continue;
                }

                string result = external.HomeGoals > external.AwayGoals ? "1" :
                    external.HomeGoals < external.AwayGoals ? "2" : "X";
                match.Result = new MatchResult
                {
                    MatchId = match.Id,
                    HomeGoals = external.HomeGoals,
                    AwayGoals = external.AwayGoals,
                    HalfTimeHomeGoals = external.HalfTimeHomeGoals,
                    HalfTimeAwayGoals = external.HalfTimeAwayGoals,
                    Result = result,
                    Btts = external.HomeGoals > 0 && external.AwayGoals > 0,
                    TotalGoals = external.HomeGoals + external.AwayGoals,
                    Source = external.Source,
                    CollectedAtUtc = DateTime.UtcNow
                };
                inserted++;
                log($"[SAVED] #{match.Id} | {match.HomeTeam} | {match.AwayTeam} | " +
                    $"{external.HomeGoals}-{external.AwayGoals} | Result={result} | " +
                    $"BTTS={(external.HomeGoals > 0 && external.AwayGoals > 0 ? "YES" : "NO")} | " +
                    $"Goals={external.HomeGoals + external.AwayGoals}");
            }
        }

        int changes = await db.SaveChangesAsync();
        return new(matched, inserted, existing, unmatched, halfTimeUpdated, changes)
        {
            IdentityConflicts = identityConflicts,
            ScoreConflicts = scoreConflicts
        };
    }

    private static string CleanAiLog(string value)
    {
        string clean = string.Concat(value.Where(c => !char.IsControl(c))).Trim();
        return clean.Length <= 300 ? clean : clean[..300];
    }

    public static void WriteConflictDiagnostics(DateOnly date, ExternalMatchResult incoming,
        IReadOnlyCollection<Match> candidates, IReadOnlyCollection<PredictionSnapshot> snapshots,
        Action<string> log)
    {
        static string Clean(string? value) =>
            string.Concat((value ?? "n/a").Select(c => char.IsControl(c) ||
                char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator or
                    System.Globalization.UnicodeCategory.ParagraphSeparator or
                    System.Globalization.UnicodeCategory.Format ? ' ' : c));
        static string Kickoff(DateTime? value) => value?.ToString("O") ?? "n/a";
        log($"[RESULT-CONFLICT] Incoming {Clean(incoming.Source)} {Clean(incoming.ExternalId)} | " +
            $"{date:yyyy-MM-dd} | {Clean(incoming.HomeTeam)} vs {Clean(incoming.AwayTeam)} | " +
            $"kickoff UTC={Kickoff(incoming.KickoffUtc)} | FT={incoming.HomeGoals}-{incoming.AwayGoals} | " +
            $"HT={incoming.HalfTimeHomeGoals?.ToString() ?? "?"}-{incoming.HalfTimeAwayGoals?.ToString() ?? "?"}");
        foreach (var match in candidates.OrderBy(x => x.Id))
        {
            string links = string.Join(", ", snapshots.Where(x => x.MatchId == match.Id)
                .Select(x => $"{Clean(x.Source)}:{Clean(x.SourceMatchId)}").Distinct().Order());
            string score = match.Result == null ? "n/a" : $"{match.Result.HomeGoals}-{match.Result.AwayGoals}";
            string half = match.Result == null ? "n/a" :
                $"{match.Result.HalfTimeHomeGoals?.ToString() ?? "?"}-{match.Result.HalfTimeAwayGoals?.ToString() ?? "?"}";
            log($"[RESULT-CONFLICT] DB #{match.Id} | {match.MatchDate:yyyy-MM-dd} | " +
                $"{Clean(match.HomeTeam)} vs {Clean(match.AwayTeam)} | league={Clean(match.League)} | " +
                $"kickoff UTC={Kickoff(match.KickoffUtc)} | FT={score} HT={half} | source IDs={links}");
        }
        log("[RESULT-CONFLICT] Unresolved; no rows changed for this result. " +
            "Use merge-duplicate-matches dry-run to review proven shared-provider duplicates; " +
            "name similarity alone never authorizes a merge.");
    }
}
