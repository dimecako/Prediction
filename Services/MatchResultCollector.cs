using Prediction.Models;
using Prediction.Entities;

namespace Prediction.Services;

public sealed record CollectedMatchResults(
    IReadOnlyList<ExternalMatchResult> Results, int FailedProviders, int RejectedRows)
{
    public IReadOnlyList<ExternalMatchResult> SourceResults { get; init; } = Results;
}

public static class MatchResultCollector
{
    public static IQueryable<Match> MissingScores(IQueryable<Match> matches) =>
        matches.Where(x => x.Result == null ||
            x.Result.HalfTimeHomeGoals == null || x.Result.HalfTimeAwayGoals == null);

    public static IReadOnlyList<IMatchResultProvider> CreatePrimaryProviders(Action<string> log)
    {
        log("[RESULTS] FootballResultsOnline disabled: unlabelled scores cannot verify regulation-time FT.");
        log("[RESULTS] Forebet is primary; SoccerVista and Flashscore are targeted fallbacks for missing FT/HT.");
        return [new ForebetResultProvider()];
    }

    public static Task<CollectedMatchResults> CollectTargetedAsync(
        DateOnly date, ITargetedMatchResultProvider provider, IReadOnlyCollection<Match> matches,
        Action<string> log, CancellationToken cancellationToken = default)
    {
        if (matches.Count == 0)
            return Task.FromResult(new CollectedMatchResults([], 0, 0));

        return CollectAsync(date, [new TargetedProviderAdapter(provider, matches)], log, cancellationToken);
    }

    public static async Task<CollectedMatchResults> CollectAsync(
        DateOnly date, IEnumerable<IMatchResultProvider> providers, Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, ExternalMatchResult>(StringComparer.Ordinal);
        var sourceResults = new List<ExternalMatchResult>();
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        int failed = 0, rejected = 0;
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ExternalMatchResult> rows;
            try
            {
                rows = await provider.GetResultsAsync(date, cancellationToken);
            }
            catch (PartialMatchResultException ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                log($"[RESULTS] {provider.Name} failed: {ex.Message}");
                foreach (string error in ex.Errors)
                    log($"[RESULTS] {provider.Name} fixture failed: {error}");
                failed++;
                rows = ex.Results;
            }
            catch (Exception ex) when (ex is HttpRequestException or
                System.Text.Json.JsonException or Newtonsoft.Json.JsonException or Microsoft.Playwright.PlaywrightException ||
                ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                log($"[RESULTS] {provider.Name} failed: {ex.Message}");
                failed++;
                continue;
            }

            log($"[RESULTS] {provider.Name} returned: {rows.Count}");
            foreach (var row in rows)
            {
                if (!row.IsRegulationTimeVerified)
                {
                    log($"[RESULTS] {provider.Name}: {row.HomeTeam} vs {row.AwayTeam}; " +
                        "regulation-time FT not verified; left unresolved (extra time/penalties must not be used).");
                    rejected++;
                    continue;
                }
                bool hasHalfTime = row.HalfTimeHomeGoals.HasValue && row.HalfTimeAwayGoals.HasValue;
                if (string.IsNullOrWhiteSpace(row.HomeTeam) || string.IsNullOrWhiteSpace(row.AwayTeam) ||
                    string.IsNullOrWhiteSpace(row.Source) || row.HomeGoals < 0 || row.AwayGoals < 0 ||
                    row.HalfTimeHomeGoals.HasValue != row.HalfTimeAwayGoals.HasValue ||
                    hasHalfTime && (row.HalfTimeHomeGoals < 0 || row.HalfTimeAwayGoals < 0 ||
                        row.HalfTimeHomeGoals > row.HomeGoals || row.HalfTimeAwayGoals > row.AwayGoals))
                {
                    log($"[RESULTS] Invalid score/identity from {provider.Name}: {row.HomeTeam} vs {row.AwayTeam}; rejected.");
                    rejected++;
                    continue;
                }

                string id = MatchIdentity.BuildExternalMatchId(
                    date.ToString("yyyy-MM-dd"), row.HomeTeam, row.AwayTeam);
                if (unresolved.Contains(id))
                {
                    rejected++;
                    log($"[RESULTS] {row.HomeTeam} vs {row.AwayTeam}: conflicting regulation-time evidence; left unresolved.");
                    continue;
                }
                if (!results.TryGetValue(id, out var existing))
                {
                    results.Add(id, row);
                    sourceResults.Add(row);
                    continue;
                }
                bool sameFinal = existing.HomeGoals == row.HomeGoals && existing.AwayGoals == row.AwayGoals;
                bool existingHalfTime = existing.HalfTimeHomeGoals.HasValue && existing.HalfTimeAwayGoals.HasValue;
                if (!sameFinal || existingHalfTime && hasHalfTime &&
                    (existing.HalfTimeHomeGoals != row.HalfTimeHomeGoals ||
                     existing.HalfTimeAwayGoals != row.HalfTimeAwayGoals))
                {
                    log($"[RESULTS] Conflicting score for {row.HomeTeam} vs {row.AwayTeam}: " +
                        $"{existing.Source} FT={existing.HomeGoals}-{existing.AwayGoals} " +
                        $"HT={existing.HalfTimeHomeGoals}-{existing.HalfTimeAwayGoals}; " +
                        $"{row.Source} FT={row.HomeGoals}-{row.AwayGoals} " +
                        $"HT={row.HalfTimeHomeGoals}-{row.HalfTimeAwayGoals}; " +
                        "regulation-time score requires verification; all conflicting fixture rows left unresolved.");
                    results.Remove(id);
                    sourceResults.RemoveAll(candidate => MatchIdentity.BuildExternalMatchId(
                        date.ToString("yyyy-MM-dd"), candidate.HomeTeam, candidate.AwayTeam) == id);
                    unresolved.Add(id);
                    rejected++;
                    continue;
                }
                if (!existingHalfTime && hasHalfTime)
                {
                    existing.HalfTimeHomeGoals = row.HalfTimeHomeGoals;
                    existing.HalfTimeAwayGoals = row.HalfTimeAwayGoals;
                    log($"[RESULTS] Half-time supplemented by {row.Source}: {row.HomeTeam} vs {row.AwayTeam}.");
                }
                sourceResults.Add(row);
            }
        }
        return new(results.Values.ToList(), failed, rejected) { SourceResults = sourceResults };
    }

    private sealed class TargetedProviderAdapter(
        ITargetedMatchResultProvider provider, IReadOnlyCollection<Match> matches) : IMatchResultProvider
    {
        public string Name => provider.Name;

        public Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
            DateOnly date, CancellationToken cancellationToken = default) =>
            provider.GetResultsAsync(date, matches, cancellationToken);
    }
}
