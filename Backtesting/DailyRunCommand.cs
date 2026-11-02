using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Infrastructure;
using Prediction.Services;

public partial class Program
{
    private static async Task RunDailyAutomationAsync(string[] args)
    {
        var date = DailyRunPlan.Today(DateTimeOffset.UtcNow);
        int count = TicketDefaultCount;
        if (args.Length > 3 ||
            (args.Length > 1 && !DateOnly.TryParseExact(args[1], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) ||
            (args.Length > 2 && (!int.TryParse(args[2], out count) || count is < 1 or > 100)))
        {
            Console.Error.WriteLine("[DAILY-RUN] Use: daily-run [yyyy-MM-dd] [count 1-100]. Default date: Europe/Skopje today.");
            CommandExecution.Fail();
            return;
        }

        string? connection = Environment.GetEnvironmentVariable("ConnectionStrings__FootballDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__FootballDb is not configured.");

        await using var runLock = await DailyRunLock.TryAcquireAsync(connection);
        if (runLock == null)
        {
            Console.Error.WriteLine("[DAILY-RUN] Another daily-run holds the database lock; no steps executed. Retry later.");
            CommandExecution.Fail();
            return;
        }

        Console.WriteLine($"[DAILY-RUN] Starting {date:yyyy-MM-dd}; count={count}; no Excel import, build or migrations.");
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        var oldestResultDate = date.AddDays(-2);
        var tickets = await db.SimulatedTickets.AsNoTracking()
            .Where(x => x.MatchDate >= oldestResultDate && x.MatchDate < date)
            .Include(x => x.Selections).ToListAsync();
        var matchIds = tickets.SelectMany(x => x.Selections).Select(x => x.MatchId).Distinct().ToList();
        var results = await db.MatchResults.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId)).ToDictionaryAsync(x => x.MatchId);
        var pendingDates = DailyRunPlan.PendingDates(tickets, results, date);
        Console.WriteLine($"[DAILY-RUN] Collecting yesterday ({date.AddDays(-1):yyyy-MM-dd}); " +
            $"also checking {oldestResultDate:yyyy-MM-dd} only if it has Pending selections (including lost accumulators). " +
            "Older dates are excluded.");
        var steps = DailyRunPlan.Build(date, count, pendingDates);
        var failed = new List<string>();
        foreach (var step in steps)
        {
            // A lost connection also loses the advisory lock: stop before any further writes.
            await runLock.CheckAsync();
            Console.WriteLine($"[DAILY-RUN] START {step.Name}");
            int status = await CommandExecution.RunAsync(() => ExecuteCommandAsync(step.Args), automation: true);
            if (status != 0)
            {
                failed.Add(step.Name);
                Console.Error.WriteLine($"[DAILY-RUN] FAILED {step.Name} (exit {status}); continuing remaining independent steps.");
            }
            else
                Console.WriteLine($"[DAILY-RUN] OK {step.Name}");
        }
        await runLock.CheckAsync();
        if (failed.Count > 0)
        {
            Console.Error.WriteLine($"[DAILY-RUN] Completed with {failed.Count} failed steps: {string.Join(", ", failed)}");
            CommandExecution.Fail();
        }
        else
            Console.WriteLine("[DAILY-RUN] All steps completed successfully.");
    }
}

namespace Prediction.Services
{
    public sealed record DailyRunStep(string Name, string[] Args);

    public static class DailyRunPlan
    {
        public static bool CheckSourceCoverage(IEnumerable<(string Source, int Rows)> sources,
            Action<string> log)
        {
            bool success = true;
            foreach (var (source, rows) in sources)
            {
                log($"[DAILY-RUN] Prediction source {source}: {rows} rows.");
                if (rows == 0)
                {
                    success = false;
                    log($"[DAILY-RUN] FAILED source {source}: empty response; provider may be unavailable or incomplete.");
                }
            }
            return success;
        }

        public static DateOnly Today(DateTimeOffset now) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,
                TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje")).DateTime);

        public static IReadOnlyList<DateOnly> PendingDates(IEnumerable<SimulatedTicket> tickets,
            IReadOnlyDictionary<long, MatchResult> results, DateOnly target) =>
            tickets.Where(t => t.MatchDate >= target.AddDays(-2) && t.MatchDate < target && t.Selections.Any(s =>
                !results.TryGetValue(s.MatchId, out var result) ||
                !TicketMarkets.IsCorrect(new(s.Market, s.Pick, s.CalibrationKey, ""),
                    result, allowFinalOnlyLoss: true).HasValue))
                .Select(t => t.MatchDate).Distinct().Order().ToList();

        public static IReadOnlyList<DailyRunStep> Build(DateOnly date, int count,
            IEnumerable<DateOnly> pendingDates)
        {
            var steps = new List<DailyRunStep>();
            var yesterday = date.AddDays(-1);
            var dayBeforeYesterday = date.AddDays(-2);
            var resultDates = new List<DateOnly> { yesterday };
            if (pendingDates.Contains(dayBeforeYesterday))
                resultDates.Add(dayBeforeYesterday);
            foreach (var resultDate in resultDates)
            {
                string old = resultDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                steps.Add(new($"Collect recent results {old}", ["collect-results", old]));
                steps.Add(new($"Report past tickets {old}", ["ticket-report", old, old]));
            }
            string target = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string size = count.ToString(CultureInfo.InvariantCulture);
            steps.Add(new("Collect target predictions", [target]));
            steps.AddRange(new DailyRunStep[]
            {
                new("ticket", ["ticket", target, size]),
                new("ticket safe", ["ticket", target, size, "safe"]),
                new("ai-rank", ["ai-rank", target, size]),
                new("ai-rank safe", ["ai-rank", target, size, "safe"]),
                new("ai-rank safe league", ["ai-rank", target, size, "safe", "league"]),
                new("daily-ticket", ["daily-ticket", target, size]),
                new("daily-ticket-league", ["daily-ticket-league", target, size]),
                new("ticket-1-3p3+", ["ticket-1-3p3+", target, size]),
                new("ticket-1-3p3+ safe", ["ticket-1-3p3+", target, size, "safe"]),
                new("Target ticket report", ["ticket-report", target, target])
            });
            return steps;
        }
    }
}
