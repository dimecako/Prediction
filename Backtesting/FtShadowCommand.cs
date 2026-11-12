using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Infrastructure;
using Prediction.Services;

public partial class Program
{
    private static async Task RunFtShadowAsync(string[] args, DailyRunHistory? runHistory)
    {
        int count = TicketDefaultCount;
        if (args.Length is < 2 or > 3 ||
            !DateOnly.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ||
            (args.Length == 3 && (!int.TryParse(args[2], out count) || count is < 1 or > 15)))
        {
            Console.Error.WriteLine("[FT-SHADOW] Use: ticket-ft-shadow yyyy-MM-dd [count 1-15].");
            CommandExecution.Fail();
            return;
        }
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        if (!CommandExecution.IsAutomation)
            await db.Database.MigrateAsync();
        var existing = await FtShadowEvaluation.FindAsync(db, date);
        if (existing != null)
        {
            var frozen = FtShadowEvaluation.Read(existing);
            Console.WriteLine($"[FT-SHADOW] Reusing frozen #{existing.Id}, {frozen.Date:yyyy-MM-dd}, count={frozen.RequestedCount}. No prices fetched or decisions changed.");
            return;
        }
        if (date < DailyRunPlan.Today(DateTimeOffset.UtcNow))
        {
            Console.Error.WriteLine("[FT-SHADOW] No backfill: new captures require a current/future Skopje date.");
            CommandExecution.Fail();
            return;
        }
        var matches = await db.Matches.AsNoTracking().Where(m => m.MatchDate == date)
            .Include(m => m.Result).Include(m => m.PredictionSnapshots).ToListAsync();
        var history = await LoadTicketHistoryAsync(db, date, runHistory, splitQuery: true);
        IReadOnlyList<BookmakerMatch> offers;
        try
        {
            offers = await MaxbetOddsProvider.FetchAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            Console.Error.WriteLine($"[FT-SHADOW] Bookmaker offer unavailable: {ex.Message}. No run saved.");
            CommandExecution.Fail();
            return;
        }
        var capture = FtShadowEvaluation.Capture(matches, offers, date, DateTime.UtcNow, count, history, Console.WriteLine);
        var saved = await FtShadowEvaluation.SaveAsync(db, capture);
        Console.WriteLine($"[FT-SHADOW] #{saved.Run.Id} {(saved.Created ? "saved" : "already frozen")}; no active ticket created/modified.");
        FtShadowReport.Write([FtShadowEvaluation.Read(saved.Run)], new Dictionary<long, Prediction.Entities.MatchResult>(), Console.WriteLine);
    }

    private static async Task RunFtShadowReportAsync(string[] args)
    {
        if (!TryReportDates(args, out var from, out var to)) return;
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        if (!CommandExecution.IsAutomation)
            await db.Database.MigrateAsync();
        var runs = await db.FtShadowRuns.AsNoTracking()
            .Where(r => r.Experiment == FtShadowEvaluation.Experiment && r.MatchDate >= from && r.MatchDate <= to)
            .OrderBy(r => r.MatchDate).ToListAsync();
        var captures = runs.Select(FtShadowEvaluation.Read).ToList();
        var ids = captures.SelectMany(r => r.Fixtures).Select(f => f.MatchId).Distinct().ToList();
        var results = await db.MatchResults.AsNoTracking().Where(r => ids.Contains(r.MatchId))
            .ToDictionaryAsync(r => r.MatchId);
        FtShadowReport.Write(captures, results, Console.WriteLine);
    }
}
