using Prediction.Entities;
using Prediction.Models;

namespace Prediction.Services;

public sealed record ResultMatchResolution(Match? Match, string Method, bool Conflict)
{
    public IReadOnlyList<Match> Matches { get; init; } = Match == null ? [] : [Match];
}

public static class ResultMatchResolver
{
    public static ResultMatchResolution Resolve(DateOnly date, ExternalMatchResult result,
        IReadOnlyCollection<Match> matches, IReadOnlyCollection<PredictionSnapshot> snapshots)
    {
        if (!result.IsRegulationTimeVerified)
            return new(null, "regulation-time FT not verified; left unresolved", true);
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
        if (bySource.Count > 1)
        {
            if (exact.Any(x => !sourceIds.Contains(x.Id)) ||
                !AreVerifiedDuplicates(bySource, snapshots, result.Source, result.ExternalId) ||
                bySource.Any(x => !RelatedTeam(x.HomeTeam, result.HomeTeam) ||
                    !RelatedTeam(x.AwayTeam, result.AwayTeam)) ||
                bySource.Any(x => x.Result != null &&
                    (x.Result.HomeGoals != result.HomeGoals || x.Result.AwayGoals != result.AwayGoals ||
                     result.HalfTimeHomeGoals.HasValue && x.Result.HalfTimeHomeGoals.HasValue &&
                     (x.Result.HalfTimeHomeGoals != result.HalfTimeHomeGoals ||
                      x.Result.HalfTimeAwayGoals != result.HalfTimeAwayGoals))))
                return new(null, "shared source ID has conflicting identity or stored scores; not linked", true);
            return new(bySource[0], "verified shared source ID", false) { Matches = bySource };
        }
        if (exact.Count > 1 ||
            exact.Count == 1 && bySource.Count == 1 && exact[0].Id != bySource[0].Id ||
            knownDifferentId)
            return new(null, "ambiguous/conflicting stored source ID and fixture identity; not linked", true);
        if (bySource.Count == 1)
            return new(bySource[0], "stored source ID", false);
        return exact.Count == 1
            ? new(exact[0], "exact normalized names and date", false)
            : new(null, "no stored source ID or exact names/date match", false);
    }

    public static bool AreVerifiedDuplicates(IReadOnlyCollection<Match> matches,
        IReadOnlyCollection<PredictionSnapshot> snapshots, string source, string sourceId)
    {
        if (matches.Count == 0 || string.IsNullOrWhiteSpace(sourceId) ||
            matches.Select(x => x.MatchDate).Distinct().Count() != 1)
            return false;
        var ids = matches.Select(x => x.Id).ToHashSet();
        var relevant = snapshots.Where(x => ids.Contains(x.MatchId) &&
            !string.IsNullOrWhiteSpace(x.SourceMatchId)).ToList();
        if (matches.Any(x => !relevant.Any(s => s.MatchId == x.Id &&
                s.Source == source && s.SourceMatchId == sourceId)) ||
            relevant.GroupBy(x => x.Source).Any(g => g.Select(x => x.SourceMatchId).Distinct().Count() > 1))
            return false;
        var first = matches.First();
        var scores = matches.Where(x => x.Result != null).Select(x => x.Result!).ToList();
        if (scores.Select(x => (x.HomeGoals, x.AwayGoals)).Distinct().Count() > 1 ||
            scores.Where(x => x.HalfTimeHomeGoals.HasValue && x.HalfTimeAwayGoals.HasValue)
                .Select(x => (x.HalfTimeHomeGoals, x.HalfTimeAwayGoals)).Distinct().Count() > 1)
            return false;
        return matches.All(x => RelatedTeam(first.HomeTeam, x.HomeTeam) &&
            RelatedTeam(first.AwayTeam, x.AwayTeam));
    }

    private static bool RelatedTeam(string left, string right)
    {
        var a = MatchIdentity.NormalizeTeam(left).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = MatchIdentity.NormalizeTeam(right).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0 || b.Length == 0)
            return false;
        // Age groups, numbered teams and women's teams must not be conflated.
        static bool Qualifier(string word) => word.Any(char.IsDigit) ||
            word is "w" or "women" or "ladies" or "ii" or "iii" or "reserves";
        if (!a.Where(Qualifier).ToHashSet().SetEquals(b.Where(Qualifier)))
            return false;
        var leftWords = a.ToHashSet();
        var rightWords = b.ToHashSet();
        return a.SequenceEqual(b) ||
            (leftWords.IsSubsetOf(rightWords) || rightWords.IsSubsetOf(leftWords)) &&
            a.Where(x => x.Length >= 4 && !Qualifier(x)).Intersect(b).Any();
    }
}
