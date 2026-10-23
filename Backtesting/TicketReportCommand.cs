using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Services;

public partial class Program
{
    private static bool TryReportDates(string[] args, out DateOnly from, out DateOnly to)
    {
        from = DateOnly.MinValue;
        to = DateOnly.MaxValue;
        if (args.Length > 3 ||
            (args.Length > 1 && !DateOnly.TryParseExact(args[1], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out from)) ||
            (args.Length > 2 && !DateOnly.TryParseExact(args[2], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out to)) || from > to)
        {
            Console.Error.WriteLine($"[{args[0].ToUpperInvariant()}] Use: {args[0]} [from yyyy-MM-dd] [to yyyy-MM-dd]; from must not exceed to.");
            Environment.ExitCode = 1;
            return false;
        }
        return true;
    }

    private static async Task RunTicketReportAsync(string[] args)
    {
        if (!TryReportDates(args, out var from, out var to))
            return;
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await db.Database.MigrateAsync();
        var tickets = await db.SimulatedTickets.AsNoTracking()
            .Where(x => x.MatchDate >= from && x.MatchDate <= to)
            .Include(x => x.Selections)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync();
        var matchIds = tickets.SelectMany(x => x.Selections).Select(x => x.MatchId).Distinct().ToList();
        var results = await db.MatchResults.AsNoTracking().Where(x => matchIds.Contains(x.MatchId))
            .ToDictionaryAsync(x => x.MatchId);
        var settlements = tickets.Select(x => TicketPerformanceAnalyzer.Settle(x, results)).ToList();

        Console.WriteLine("[TICKET-REPORT] SIMULATION ONLY. Stakes and returns are units, not actual payments.");
        Console.WriteLine("[TICKET-REPORT] Frozen selections/prices; current stored results. Inclusive match-date range.");
        Console.WriteLine($"[TICKET-REPORT] Unique fixtures={matchIds.Count}; with final results={results.Count}; " +
            $"with half-time={results.Values.Count(x => x.HalfTimeHomeGoals.HasValue && x.HalfTimeAwayGoals.HasValue)}.");
        if (tickets.Count == 0)
            Console.WriteLine("[TICKET-REPORT] No saved simulations in this range. Existing Excel files are not imported or backfilled.");
        foreach (var row in settlements)
        {
            Console.WriteLine($"[SIMULATION] #{row.Ticket.Id} | {row.Ticket.MatchDate:yyyy-MM-dd} | " +
                $"{row.Ticket.Mode} | {row.Selections.Count} picks | {row.Status} | Odds={row.CombinedOdds:F4} | " +
                $"Stake={row.Ticket.Stake:F2} | Payout={Money(row.Payout)} | Profit={Money(row.Profit)} | " +
                $"Won={row.Selections.Count(x => x.Correct == true)} Lost={row.Selections.Count(x => x.Correct == false)} " +
                $"Pending={row.Selections.Count(x => !x.Correct.HasValue)} | Created UTC={row.Ticket.CreatedAtUtc:O}");
            foreach (var leg in row.Selections.OrderBy(x => x.Selection.Position))
            {
                results.TryGetValue(leg.Selection.MatchId, out var result);
                string score = result == null ? "FT=n/a HT=n/a" :
                    $"FT={result.HomeGoals}-{result.AwayGoals} HT=" +
                    (result.HalfTimeHomeGoals.HasValue && result.HalfTimeAwayGoals.HasValue
                        ? $"{result.HalfTimeHomeGoals}-{result.HalfTimeAwayGoals}" : "n/a");
                string status = leg.Correct == true ? "Won" : leg.Correct == false ? "Lost" : "Pending";
                string? reason = TicketPerformanceAnalyzer.PendingReason(leg, result);
                Console.WriteLine($"[TICKET-LEG] #{row.Ticket.Id}/{leg.Selection.Position} | {leg.Selection.Fixture} | " +
                    $"{leg.Selection.Market}/{leg.Selection.Pick} | Odds={leg.Selection.Odds:F2} | {score} | {status}" +
                    (reason == null ? "" : $" | Reason={reason}"));
            }
        }
        PrintPerformance("ALL", settlements);
        foreach (var mode in settlements.GroupBy(x => x.Ticket.Mode))
            PrintPerformance(mode.Key, mode);

        Console.WriteLine("[TICKET-REPORT] League/market: UNIQUE match+market+pick, earliest frozen price in this range.");
        Console.WriteLine("[TICKET-REPORT] Singles ROI below is hypothetical flat 1-unit singles, NOT accumulator ROI or allocated ticket profit.");
        var uniquePicks = settlements.SelectMany(x => x.Selections)
            .GroupBy(x => (x.Selection.MatchId, x.Selection.Market, x.Selection.Pick))
            .Select(g => g.First());
        foreach (var league in uniquePicks.GroupBy(x =>
                     (League: TicketPerformanceAnalyzer.LeagueName(x.Selection.League), x.Selection.Market))
                     .OrderBy(x => x.Key.League).ThenBy(x => x.Key.Market))
        {
            var graded = league.Where(x => x.Correct.HasValue).ToList();
            int correct = graded.Count(x => x.Correct == true);
            decimal payout = graded.Where(x => x.Correct == true).Sum(x => x.Selection.Odds);
            decimal profit = payout - graded.Count;
            string accuracy = graded.Count == 0 ? "n/a" : $"{(double)correct / graded.Count:P1}";
            string roi = graded.Count == 0 ? "n/a" : $"{profit / graded.Count:P1}";
            Console.WriteLine($"[LEAGUE-PICKS] {league.Key.League} | {league.Key.Market} | " +
                $"Correct={correct}/{graded.Count} ({accuracy}) | Pending={league.Count() - graded.Count} | " +
                $"Singles profit={profit:F2} | Singles ROI={roi}" +
                (graded.Count < MinReliableSample ? " (small sample)" : ""));
        }
        Console.WriteLine("[TICKET-REPORT] Pending stakes are excluded from settled ROI. Any losing leg settles an accumulator as lost.");
        Console.WriteLine("[TICKET-REPORT] Missing results/half-time remain ungraded unless the final total rules out a half-goal pick; cancellations are not inferred losses or voids.");
        Console.WriteLine("[TICKET-REPORT] Overlapping simulations and safe/value variants are not independent evidence of profitability.");

        static string Money(decimal? value) => value.HasValue ? value.Value.ToString("F2") : "n/a";
        static void PrintPerformance(string label, IEnumerable<TicketSettlement> rows)
        {
            var total = TicketPerformanceAnalyzer.Summarize(rows);
            string roi = total.Roi.HasValue ? total.Roi.Value.ToString("P1") : "n/a";
            Console.WriteLine($"[ROI {label}] Tickets={total.Total} | Won={total.Won} Lost={total.Lost} Pending={total.Pending} | " +
                $"Settled stake={total.SettledStake:F2} | Pending stake={total.PendingStake:F2} | " +
                $"Payout={total.Payout:F2} | Profit={total.Profit:F2} | ROI={roi}");
        }
    }

    private static async Task RunLeagueStatsAsync(string[] args)
    {
        if (!TryReportDates(args, out var from, out var to))
            return;
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await db.Database.MigrateAsync();
        var matches = await db.Matches.AsNoTracking()
            .Where(x => x.MatchDate >= from && x.MatchDate <= to && x.Result != null)
            .Include(x => x.Result).Include(x => x.PredictionSnapshots).ToListAsync();
        var rows = LeaguePerformanceAnalyzer.Analyze(matches);
        Console.WriteLine("[LEAGUE-STATS] Equal-vote consensus; strict latest snapshot per source BEFORE 00:00 UTC on match date.");
        Console.WriteLine($"[LEAGUE-STATS] Finished matches={matches.Count}; league labels={matches.Count(x => !string.IsNullOrWhiteSpace(x.League))}.");
        Console.WriteLine("[LEAGUE-STATS] Leagues are saved from uniquely paired Maxbet offers by ticket; missing historical labels are unknown.");
        if (rows.Count == 0)
            Console.WriteLine("[LEAGUE-STATS] No graded consensus picks with eligible pre-date snapshots.");
        foreach (var row in rows)
            Console.WriteLine($"[LEAGUE] {row.League} | {row.Market} | Correct={row.Correct}/{row.Total} | " +
                $"Accuracy={row.Accuracy:P1} | Safe={row.Safe:P1}" +
                (row.Total < MinReliableSample ? " (small sample)" : ""));
        Console.WriteLine("[LEAGUE-STATS] Sparse coverage is expected for same-day captures. Accuracy is not ROI or proof of an edge.");
        Console.WriteLine("[LEAGUE-STATS] League statistics are observational; existing selection thresholds/ranking are unchanged.");
    }
}
