using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Services;

public partial class Program
{
    private static async Task RunTicketDiagnosticsAsync(string[] args)
    {
        if (!TryReportDates(args, out var from, out var to))
            return;
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await db.Database.MigrateAsync();
        var tickets = await db.SimulatedTickets.AsNoTracking().Include(x => x.Selections)
            .Where(x => x.MatchDate >= from && x.MatchDate <= to).ToListAsync();
        await TicketResultDiagnostics.WriteAsync(db, tickets, Console.WriteLine);
    }

    private static async Task RunTicketSourceCheckAsync(string[] args)
    {
        if (!TryReportDates(args, out var from, out var to))
            return;
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await db.Database.MigrateAsync();
        var tickets = await db.SimulatedTickets.AsNoTracking().Include(x => x.Selections)
            .Where(x => x.MatchDate >= from && x.MatchDate <= to).ToListAsync();
        var ids = tickets.SelectMany(x => x.Selections).Select(x => x.MatchId).Distinct().ToList();
        var fixtures = await db.Matches.AsNoTracking().Include(x => x.Result).Include(x => x.PredictionSnapshots)
            .Where(x => ids.Contains(x.Id)).ToListAsync();
        var results = fixtures.Where(x => x.Result != null).ToDictionary(x => x.Id, x => x.Result!);
        var pendingIds = tickets.SelectMany(x => TicketPerformanceAnalyzer.Settle(x, results).Selections)
            .Where(x => !x.Correct.HasValue).Select(x => x.Selection.MatchId).ToHashSet();
        Console.WriteLine("[SOURCE-CHECK] Live Forebet feed for pending fixtures only; no scores, identities or frozen tickets are changed.");
        if (pendingIds.Count == 0)
            Console.WriteLine("[SOURCE-CHECK] No pending fixtures.");
        foreach (var day in fixtures.Where(x => pendingIds.Contains(x.Id)).GroupBy(x => x.MatchDate).OrderBy(x => x.Key))
        {
            List<Prediction.Models.SourceFixtureStatus> rows;
            try
            {
                rows = await new ProductionConsensusAggregator(day.Key.ToString("yyyy-MM-dd"))
                    .GetForebetFixtureStatusesAsync(day.Key);
            }
            catch (Exception ex) when (ex is HttpRequestException or Newtonsoft.Json.JsonException or OperationCanceledException)
            {
                Console.Error.WriteLine($"[SOURCE-CHECK] Forebet {day.Key:yyyy-MM-dd} failed: {ex.Message}");
                Environment.ExitCode = 1;
                continue;
            }
            Console.WriteLine($"[SOURCE-CHECK] Requested {day.Key:yyyy-MM-dd}; feed rows={rows.Count}.");
            foreach (var fixture in day.OrderBy(x => x.Id))
            {
                var sourceIds = fixture.PredictionSnapshots.Where(x => x.Source == "Forebet" && x.SourceMatchId != null)
                    .Select(x => x.SourceMatchId).ToHashSet();
                var found = rows.Where(x =>
                    sourceIds.Contains(x.SourceMatchId) ||
                    MatchIdentity.NormalizeTeam(x.HomeTeam) == MatchIdentity.NormalizeTeam(fixture.HomeTeam) &&
                    MatchIdentity.NormalizeTeam(x.AwayTeam) == MatchIdentity.NormalizeTeam(fixture.AwayTeam)).ToList();
                Console.WriteLine($"[SOURCE-CHECK] #{fixture.Id} | {day.Key:yyyy-MM-dd} | {fixture.HomeTeam} vs {fixture.AwayTeam}");
                if (found.Count == 0)
                    Console.WriteLine("[SOURCE-CHECK] No exact stored-ID/name match in this Forebet response; " +
                        "could be missing coverage or different names. This does NOT establish match status.");
                foreach (var row in found)
                    Console.WriteLine($"[SOURCE-ROW] {row.SourceMatchId} | {row.HomeTeam} vs {row.AwayTeam} | " +
                        $"date={row.DateText} | status={row.Status} | raw score={row.HomeScore}-{row.AwayScore} | " +
                        $"HT={row.HalfTimeHomeScore}-{row.HalfTimeAwayScore} | " +
                        $"ET={row.ExtraTimeScore} | penalties={row.PenaltyScore} | " +
                        "diagnostic only; non-FT scores are NOT final results.");
                if (found.Count > 1)
                    Console.WriteLine("[SOURCE-CHECK] Multiple matching rows; manual verification required.");
            }
        }
    }
}
