using Prediction.Entities;
using Prediction.Models;

namespace Prediction.Services;

public sealed record ResultMatchResolution(Match? Match, string Method, bool Conflict)
{
    public IReadOnlyList<Match> Matches { get; init; } = Match == null ? [] : [Match];
    public IReadOnlyList<Match> Candidates { get; init; } = [];
}

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
        var candidates = exact.Concat(bySource).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToList();
        ResultMatchResolution Conflict(string method) =>
            new(null, method, true) { Candidates = candidates };
        if (!result.IsRegulationTimeVerified)
            return Conflict("regulation-time FT not verified; left unresolved");
        if (exact.Count == 1 && bySource.Count == 1 && exact[0].Id != bySource[0].Id)
        {
            if (VerifiedLiveFixture(exact[0], bySource[0], result, candidates, snapshots))
                return new(exact[0], "verified live source ID and exact fixture names", false) { Matches = candidates };
            var shared = snapshots.Where(x => candidates.Any(m => m.Id == x.MatchId) &&
                    !string.IsNullOrWhiteSpace(x.SourceMatchId))
                .GroupBy(x => (x.Source, x.SourceMatchId))
                .Where(g => g.Select(x => x.MatchId).Distinct().Count() == candidates.Count);
            if (shared.Any(g => AreVerifiedDuplicates(candidates, snapshots, g.Key.Source,
                    g.Key.SourceMatchId!)) &&
                candidates.All(x => RelatedTeam(x.HomeTeam, result.HomeTeam) &&
                    RelatedTeam(x.AwayTeam, result.AwayTeam) && ScoresAgree(x, result) &&
                    KickoffsAgree(x, result)))
                return new(candidates[0], "verified shared provider evidence", false) { Matches = candidates };
        }
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
                bySource.Any(x => !ScoresAgree(x, result) || !KickoffsAgree(x, result)))
                return Conflict("shared source ID has conflicting identity or stored scores; not linked");
            return new(bySource[0], "verified shared source ID", false) { Matches = bySource };
        }
        if (exact.Count > 1 ||
            exact.Count == 1 && bySource.Count == 1 && exact[0].Id != bySource[0].Id ||
            knownDifferentId)
            return Conflict("ambiguous/conflicting stored source ID and fixture identity; not linked");
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
        if (matches.Where(x => x.KickoffUtc.HasValue).Select(x => x.KickoffUtc).Distinct().Count() > 1)
            return false;
        var scores = matches.Where(x => x.Result != null).Select(x => x.Result!).ToList();
        if (scores.Select(x => (x.HomeGoals, x.AwayGoals)).Distinct().Count() > 1 ||
            scores.Where(x => x.HalfTimeHomeGoals.HasValue)
                .Select(x => x.HalfTimeHomeGoals).Distinct().Count() > 1 ||
            scores.Where(x => x.HalfTimeAwayGoals.HasValue)
                .Select(x => x.HalfTimeAwayGoals).Distinct().Count() > 1)
            return false;
        return matches.All(x => RelatedTeam(first.HomeTeam, x.HomeTeam) &&
            RelatedTeam(first.AwayTeam, x.AwayTeam));
    }

    private static bool ScoresAgree(Match match, ExternalMatchResult result) =>
        match.Result == null ||
        match.Result.HomeGoals == result.HomeGoals && match.Result.AwayGoals == result.AwayGoals &&
        (!result.HalfTimeHomeGoals.HasValue || !match.Result.HalfTimeHomeGoals.HasValue ||
         match.Result.HalfTimeHomeGoals == result.HalfTimeHomeGoals) &&
        (!result.HalfTimeAwayGoals.HasValue || !match.Result.HalfTimeAwayGoals.HasValue ||
         match.Result.HalfTimeAwayGoals == result.HalfTimeAwayGoals);

    private static bool KickoffsAgree(Match match, ExternalMatchResult result) =>
        !result.KickoffUtc.HasValue || !match.KickoffUtc.HasValue || match.KickoffUtc == result.KickoffUtc;

    private static bool VerifiedLiveFixture(Match exact, Match linked, ExternalMatchResult incoming,
        IReadOnlyCollection<Match> candidates, IReadOnlyCollection<PredictionSnapshot> snapshots)
    {
        // A fresh dated provider fixture can corroborate an otherwise unlinked exact-name row.
        // Midnight is the legacy date-only placeholder, not a verified kickoff.
        if (incoming.KickoffUtc is not DateTime kickoff || kickoff.Kind != DateTimeKind.Utc ||
            kickoff.TimeOfDay == TimeSpan.Zero ||
            MatchIdentity.NormalizeTeam(exact.HomeTeam) != MatchIdentity.NormalizeTeam(incoming.HomeTeam) ||
            MatchIdentity.NormalizeTeam(exact.AwayTeam) != MatchIdentity.NormalizeTeam(incoming.AwayTeam) ||
            !LiveTeamAgrees(linked.HomeTeam, incoming.HomeTeam) ||
            !LiveTeamAgrees(linked.AwayTeam, incoming.AwayTeam))
            return false;
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(kickoff,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje")));
        if (localDate != exact.MatchDate)
            return false;
        var ids = candidates.Select(x => x.Id).ToHashSet();
        if (snapshots.Where(x => ids.Contains(x.MatchId) && !string.IsNullOrWhiteSpace(x.SourceMatchId))
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Select(x => x.SourceMatchId).Distinct(StringComparer.Ordinal).Count() > 1))
            return false;
        return candidates.All(x => ScoresAgree(x, incoming) &&
            (!x.KickoffUtc.HasValue || x.KickoffUtc.Value.TimeOfDay == TimeSpan.Zero || x.KickoffUtc == kickoff)) &&
            candidates.Where(x => x.Result?.HalfTimeHomeGoals != null)
                .Select(x => x.Result!.HalfTimeHomeGoals).Distinct().Count() <= 1 &&
            candidates.Where(x => x.Result?.HalfTimeAwayGoals != null)
                .Select(x => x.Result!.HalfTimeAwayGoals).Distinct().Count() <= 1;
    }

    private static bool LiveTeamAgrees(string stored, string incoming)
    {
        static string ClubName(string name) => string.Join(" ", MatchIdentity.NormalizeTeam(name)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x is not ("fc" or "cf" or "sc" or "afc" or "bk")));
        return RelatedTeam(ClubName(stored), ClubName(incoming));
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
