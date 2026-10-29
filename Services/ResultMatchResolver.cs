using Prediction.Entities;
using Prediction.Models;

namespace Prediction.Services;

public sealed record ResultMatchResolution(Match? Match, string Method, bool Conflict);

public static class ResultMatchResolver
{
    public static ResultMatchResolution Resolve(DateOnly date, ExternalMatchResult result,
        IReadOnlyCollection<Match> matches, IReadOnlyCollection<PredictionSnapshot> snapshots)
    {
        string identity = MatchIdentity.BuildExternalMatchId(
            date.ToString("yyyy-MM-dd"), result.HomeTeam, result.AwayTeam);
        var exact = matches.Where(x => x.MatchDate == date && x.ExternalMatchId == identity).ToList();
        var sourceIds = string.IsNullOrWhiteSpace(result.ExternalId)
            ? new HashSet<long>()
            : snapshots.Where(x => x.Source == result.Source && x.SourceMatchId == result.ExternalId)
                .Select(x => x.MatchId).ToHashSet();
        var bySource = matches.Where(x => x.MatchDate == date && sourceIds.Contains(x.Id)).ToList();
        bool knownDifferentId = exact.Count == 1 && bySource.Count == 0 &&
            !string.IsNullOrWhiteSpace(result.ExternalId) &&
            snapshots.Any(x => x.MatchId == exact[0].Id && x.Source == result.Source &&
                !string.IsNullOrWhiteSpace(x.SourceMatchId) && x.SourceMatchId != result.ExternalId);
        if (exact.Count > 1 || bySource.Count > 1 ||
            exact.Count == 1 && bySource.Count == 1 && exact[0].Id != bySource[0].Id ||
            knownDifferentId)
            return new(null, "ambiguous/conflicting stored source ID and fixture identity; not linked", true);
        if (bySource.Count == 1)
            return new(bySource[0], "stored source ID", false);
        return exact.Count == 1
            ? new(exact[0], "exact normalized names and date", false)
            : new(null, "no stored source ID or exact names/date match", false);
    }
}
