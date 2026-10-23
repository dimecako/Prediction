using Prediction.Models;

namespace Prediction.Services;

public sealed record CollectedMatchResults(
    IReadOnlyList<ExternalMatchResult> Results, int FailedProviders, int RejectedRows);

public static class MatchResultCollector
{
    public static async Task<CollectedMatchResults> CollectAsync(
        DateOnly date, IEnumerable<IMatchResultProvider> providers, Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, ExternalMatchResult>(StringComparer.Ordinal);
        int failed = 0, rejected = 0;
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ExternalMatchResult> rows;
            try
            {
                rows = await provider.GetResultsAsync(date, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or
                System.Text.Json.JsonException or Newtonsoft.Json.JsonException ||
                ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                log($"[RESULTS] {provider.Name} failed: {ex.Message}");
                failed++;
                continue;
            }

            log($"[RESULTS] {provider.Name} returned: {rows.Count}");
            foreach (var row in rows)
            {
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
                if (!results.TryGetValue(id, out var existing))
                {
                    results.Add(id, row);
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
                        $"HT={row.HalfTimeHomeGoals}-{row.HalfTimeAwayGoals}; keeping first source.");
                    rejected++;
                    continue;
                }
                if (!existingHalfTime && hasHalfTime)
                {
                    existing.HalfTimeHomeGoals = row.HalfTimeHomeGoals;
                    existing.HalfTimeAwayGoals = row.HalfTimeAwayGoals;
                    log($"[RESULTS] Half-time supplemented by {row.Source}: {row.HomeTeam} vs {row.AwayTeam}.");
                }
            }
        }
        return new(results.Values.ToList(), failed, rejected);
    }
}
