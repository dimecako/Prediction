using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;

namespace Prediction.Services;

public static class TicketResultDiagnostics
{
    public static bool IsCandidate(Match target, Match candidate)
    {
        if (target.Id == candidate.Id ||
            Math.Abs(target.MatchDate.DayNumber - candidate.MatchDate.DayNumber) > 1)
            return false;
        return Related(target.HomeTeam, candidate.HomeTeam) &&
               Related(target.AwayTeam, candidate.AwayTeam);

        static bool Related(string left, string right)
        {
            string a = MatchIdentity.NormalizeTeam(left);
            string b = MatchIdentity.NormalizeTeam(right);
            if (a == b && a.Length > 0)
                return true;
            var words = a.Split(' ').Where(x => x.Length >= 4).ToHashSet(StringComparer.Ordinal);
            return b.Split(' ').Any(words.Contains);
        }
    }

    public static async Task WriteAsync(FootballDbContext db,
        IReadOnlyCollection<SimulatedTicket> tickets, Action<string> log)
    {
        var ids = tickets.SelectMany(x => x.Selections).Select(x => x.MatchId).Distinct().ToList();
        if (ids.Count == 0)
        {
            log("[TICKET-DIAGNOSTICS] No saved ticket fixtures in this range.");
            return;
        }
        var fixtures = await db.Matches.AsNoTracking().Include(x => x.Result)
            .Include(x => x.PredictionSnapshots).Where(x => ids.Contains(x.Id)).ToListAsync();
        var results = fixtures.Where(x => x.Result != null).ToDictionary(x => x.Id, x => x.Result!);
        var pendingIds = tickets.SelectMany(x => TicketPerformanceAnalyzer.Settle(x, results).Selections)
            .Where(x => !x.Correct.HasValue).Select(x => x.Selection.MatchId).ToHashSet();
        var pending = fixtures.Where(x => pendingIds.Contains(x.Id)).ToList();
        log($"[TICKET-DIAGNOSTICS] Pending unique fixtures={pending.Count}. " +
            "Stored data only; candidate names/dates are suggestions, NEVER automatic links or proof of a final result.");
        if (pending.Count == 0)
            return;
        var from = pending.Min(x => x.MatchDate);
        var to = pending.Max(x => x.MatchDate);
        from = from == DateOnly.MinValue ? from : from.AddDays(-1);
        to = to == DateOnly.MaxValue ? to : to.AddDays(1);
        var nearby = await db.Matches.AsNoTracking().Include(x => x.Result)
            .Include(x => x.PredictionSnapshots)
            .Where(x => x.MatchDate >= from && x.MatchDate <= to).ToListAsync();
        foreach (var fixture in pending.OrderBy(x => x.MatchDate).ThenBy(x => x.Id))
        {
            string reason = fixture.Result == null
                ? "missing stored final result (NOT evidence the match is still playing)"
                : "missing required half-time result";
            var forebet = fixture.PredictionSnapshots.Where(x => x.Source == "Forebet")
                .OrderBy(x => x.CapturedAtUtc).ToList();
            string forebetInfo = forebet.Count == 0 ? "no stored Forebet prediction" :
                $"Forebet prediction captured UTC={forebet[0].CapturedAtUtc:O}; " +
                $"source IDs={string.Join(",", forebet.Select(x => x.SourceMatchId ?? "legacy/not captured").Distinct())}";
            log($"[TICKET-MISSING] #{fixture.Id} | {fixture.MatchDate:yyyy-MM-dd} | " +
                $"{fixture.HomeTeam} vs {fixture.AwayTeam} | {reason} | {forebetInfo} | " +
                $"prediction sources={string.Join(",", fixture.PredictionSnapshots.Select(x => x.Source).Distinct().Order())}");
            var candidates = nearby.Where(x => IsCandidate(fixture, x))
                .OrderBy(x => Math.Abs(x.MatchDate.DayNumber - fixture.MatchDate.DayNumber))
                .ThenBy(x => x.Id).ToList();
            foreach (var candidate in candidates.Take(5))
            {
                string score = candidate.Result == null ? "missing" :
                    $"{candidate.Result.HomeGoals}-{candidate.Result.AwayGoals}";
                log($"[TICKET-CANDIDATE] For #{fixture.Id}: #{candidate.Id} | {candidate.MatchDate:yyyy-MM-dd} | " +
                    $"{candidate.HomeTeam} vs {candidate.AwayTeam} | FT={score} | " +
                    $"sources={string.Join(",", candidate.PredictionSnapshots.Select(x => x.Source).Distinct().Order())} | " +
                    "UNVERIFIED; check both clubs, competition and kickoff before linking.");
            }
            if (candidates.Count == 0)
                log($"[TICKET-CANDIDATE] For #{fixture.Id}: no stored same-order name candidates within +/-1 day.");
            else if (candidates.Count > 5)
                log($"[TICKET-CANDIDATE] For #{fixture.Id}: showing 5 of {candidates.Count} candidates.");
        }
    }
}
