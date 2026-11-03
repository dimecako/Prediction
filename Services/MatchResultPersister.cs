using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Infrastructure;
using Prediction.Models;

namespace Prediction.Services;

public sealed record MatchResultPersistenceSummary(
    int Matched, int Inserted, int Existing, int Unmatched, int HalfTimeUpdated, int Changes);

public static class MatchResultPersister
{
    public static async Task<MatchResultPersistenceSummary> PersistAsync(
        FootballDbContext db, DateOnly date, IReadOnlyCollection<ExternalMatchResult> sourceResults,
        Action<string> log)
    {
        int matched = 0, inserted = 0, existing = 0, unmatched = 0, halfTimeUpdated = 0;
        var resultMatches = await db.Matches.Include(x => x.Result)
            .Where(x => x.MatchDate == date).ToListAsync();
        var sourceSnapshots = await db.PredictionSnapshots.AsNoTracking()
            .Where(x => x.Match.MatchDate == date && x.SourceMatchId != null).ToListAsync();

        foreach (var external in sourceResults)
        {
            var resolution = ResultMatchResolver.Resolve(date, external, resultMatches, sourceSnapshots);
            if (resolution.Conflict)
            {
                log($"[RESULTS] Identity conflict: {external.Source} {external.ExternalId} | " +
                    $"{external.HomeTeam} vs {external.AwayTeam}; {resolution.Method}.");
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

                    if (external.HalfTimeHomeGoals.HasValue && external.HalfTimeAwayGoals.HasValue &&
                        (!match.Result.HalfTimeHomeGoals.HasValue || !match.Result.HalfTimeAwayGoals.HasValue))
                    {
                        match.Result.HalfTimeHomeGoals = external.HalfTimeHomeGoals;
                        match.Result.HalfTimeAwayGoals = external.HalfTimeAwayGoals;
                        halfTimeUpdated++;
                    }
                    else if (external.HalfTimeHomeGoals.HasValue && external.HalfTimeAwayGoals.HasValue &&
                        (match.Result.HalfTimeHomeGoals != external.HalfTimeHomeGoals ||
                         match.Result.HalfTimeAwayGoals != external.HalfTimeAwayGoals))
                    {
                        log($"[RESULTS] Conflicting stored half-time score for #{match.Id}; not overwritten.");
                        CommandExecution.Fail();
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
        return new(matched, inserted, existing, unmatched, halfTimeUpdated, changes);
    }
}
