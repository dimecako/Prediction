using System.Globalization;
using System.Text;
using Prediction.Entities;

namespace Prediction.Services;

public static class PredictionReportFixtures
{
    // Presentation copies only: stored identities, snapshots and results are never changed.
    public static IReadOnlyList<Match> Consolidate(IEnumerable<Match> matches)
    {
        var groups = new List<List<Match>>();
        foreach (var bucket in matches.GroupBy(x => (x.MatchDate, Home: ClubName(x.HomeTeam), Away: ClubName(x.AwayTeam))))
        {
            var candidates = new List<List<Match>>();
            foreach (var match in bucket.OrderBy(x => x.Id))
            {
                var group = candidates.FirstOrDefault(g => g.All(other => SameFixture(other, match)));
                if (group == null)
                    candidates.Add([match]);
                else
                    group.Add(match);
            }
            groups.AddRange(candidates);
        }

        return groups.Select(group =>
        {
            var first = group[0];
            var results = group.Where(x => x.Result != null).Select(x => x.Result!).ToList();
            var result = results.FirstOrDefault();
            var display = new Match
            {
                Id = first.Id,
                HomeTeam = first.HomeTeam,
                AwayTeam = first.AwayTeam,
                MatchDate = first.MatchDate,
                League = group.Select(x => x.League).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                KickoffUtc = group.Select(x => x.KickoffUtc).FirstOrDefault(x => x.HasValue),
                PredictionSnapshots = group.SelectMany(x => x.PredictionSnapshots).ToList(),
                Result = result == null ? null : new MatchResult
                {
                    HomeGoals = result.HomeGoals,
                    AwayGoals = result.AwayGoals,
                    HalfTimeHomeGoals = results.Select(x => x.HalfTimeHomeGoals).FirstOrDefault(x => x.HasValue),
                    HalfTimeAwayGoals = results.Select(x => x.HalfTimeAwayGoals).FirstOrDefault(x => x.HasValue)
                }
            };
            display.PredictionSnapshots = CalibrationAnalyzer.LatestPerSource(display).ToList();
            return display;
        }).OrderBy(x => x.HomeTeam, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AwayTeam, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).ToList();
    }

    private static bool SameFixture(Match left, Match right)
    {
        if (left.MatchDate != right.MatchDate ||
            left.KickoffUtc.HasValue && right.KickoffUtc.HasValue && left.KickoffUtc != right.KickoffUtc)
            return false;
        var snapshots = left.PredictionSnapshots.Concat(right.PredictionSnapshots).ToList();
        if (snapshots.Where(x => !string.IsNullOrWhiteSpace(x.SourceMatchId))
            .GroupBy(x => x.Source.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Select(x => x.SourceMatchId!.Trim()).Distinct(StringComparer.Ordinal).Count() > 1))
            return false;
        if (left.Result is { } a && right.Result is { } b &&
            (a.HomeGoals != b.HomeGoals || a.AwayGoals != b.AwayGoals ||
             a.HalfTimeHomeGoals.HasValue && b.HalfTimeHomeGoals.HasValue && a.HalfTimeHomeGoals != b.HalfTimeHomeGoals ||
             a.HalfTimeAwayGoals.HasValue && b.HalfTimeAwayGoals.HasValue && a.HalfTimeAwayGoals != b.HalfTimeAwayGoals))
            return false;

        string home = Normalize(left.HomeTeam), away = Normalize(left.AwayTeam);
        if (home.Length == 0 || away.Length == 0)
            return false;
        if (home == Normalize(right.HomeTeam) && away == Normalize(right.AwayTeam))
            return true;

        // A provider link permits club designator aliases, not partial-name or squad matching.
        if (ClubName(left.HomeTeam).Length == 0 || ClubName(left.AwayTeam).Length == 0 ||
            ClubName(left.HomeTeam) != ClubName(right.HomeTeam) ||
            ClubName(left.AwayTeam) != ClubName(right.AwayTeam))
            return false;
        return left.PredictionSnapshots.Where(x => !string.IsNullOrWhiteSpace(x.Source) &&
                !string.IsNullOrWhiteSpace(x.SourceMatchId))
            .Any(x => ResultMatchResolver.AreVerifiedDuplicates([left, right], snapshots, x.Source, x.SourceMatchId!));
    }

    private static string ClubName(string name)
    {
        var normalized = Normalize(name);
        return normalized switch
        {
            "sl benfica" or "slb benfica" or "sport lisboa e benfica" or
            "sport lisbon benfica" or "benfica lisbon" or "benfica" => "benfica",
            _ => string.Join(" ", normalized.Split(' ')
                .Where(word => word is not ("fc" or "sc" or "cf" or "afc")))
        };
    }

    private static string Normalize(string value)
    {
        var normalized = new StringBuilder();
        bool separator = false;
        foreach (char c in value.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
            {
                if (separator && normalized.Length > 0)
                    normalized.Append(' ');
                normalized.Append(c);
                separator = false;
            }
            else
                separator = true;
        }
        return normalized.ToString();
    }
}
