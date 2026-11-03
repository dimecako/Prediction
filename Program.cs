using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Playwright;
using System.Globalization;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using PlaywrightCookie = Microsoft.Playwright.Cookie;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Services;
using Prediction.ML;
using Prediction.Infrastructure;

public partial class ProductionConsensusAggregator
{
    private string targetDate;
    private string todayStr;

    private static readonly SemaphoreSlim flareSemaphore = new SemaphoreSlim(1, 1);

    private readonly string flareSolverrUrl = Environment.GetEnvironmentVariable("FLARESOLVERR_URL") ?? "http://localhost:8191/v1";

    private ConcurrentDictionary<string, UnifiedMatch> unifiedDb = new ConcurrentDictionary<string, UnifiedMatch>();

    public IReadOnlyCollection<UnifiedMatch> UnifiedMatches => unifiedDb.Values.ToList();

    public ProductionConsensusAggregator(string targetDate)
    {
        this.targetDate = targetDate;
        this.todayStr = DateTime.Today.ToString("yyyy-MM-dd");
    }
}


public partial class Program
{
    private static readonly SemaphoreSlim analysisLock =
        new SemaphoreSlim(1, 1);

    private static readonly SemaphoreSlim scraperConcurrency =
        new SemaphoreSlim(3, 3);

    private static async Task<List<SiteMatch>> RunScraperAsync(
        Func<Task<List<SiteMatch>>> scrape)
    {
        await scraperConcurrency.WaitAsync();

        try
        {
            return await scrape();
        }
        finally
        {
            scraperConcurrency.Release();
        }
    }

    public static Task<int> Main(string[] args) =>
        CommandExecution.RunAsync(() => ExecuteCommandAsync(args));

    private static async Task ExecuteCommandAsync(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("daily-run", StringComparison.OrdinalIgnoreCase))
        {
            await RunDailyAutomationAsync(args);
            return;
        }

        if (args.Length > 0 && args[0].Equals("ticket-import", StringComparison.OrdinalIgnoreCase))
        {
            await RunTicketImportAsync(args);
            return;
        }

        if (args.Length > 0 &&
            args[0].Equals("ticket-source-check", StringComparison.OrdinalIgnoreCase))
        {
            await RunTicketSourceCheckAsync(args);
            return;
        }

        if (args.Length > 0 &&
            args[0].Equals("ticket-diagnostics", StringComparison.OrdinalIgnoreCase))
        {
            await RunTicketDiagnosticsAsync(args);
            return;
        }

        if (args.Length > 0 &&
            args[0].Equals("ticket-report", StringComparison.OrdinalIgnoreCase))
        {
            await RunTicketReportAsync(args);
            return;
        }

        if (args.Length > 0 &&
            args[0].Equals("league-stats", StringComparison.OrdinalIgnoreCase))
        {
            await RunLeagueStatsAsync(args);
            return;
        }

        if (args.Length > 0 &&
            (args[0].Equals("ticket", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("ticket-1-3p3+", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("daily-ticket", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("daily-ticket-league", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("ai-rank", StringComparison.OrdinalIgnoreCase)))
        {
            await RunTicketAsync(args);
            return;
        }

        if (args.Length > 0 &&
            args[0].Equals("calibration", StringComparison.OrdinalIgnoreCase))
        {
            await RunCalibrationAsync();
            return;
        }

        if (args.Length > 0 &&
            args[0].Equals("weighted-consensus", StringComparison.OrdinalIgnoreCase))
        {
            await RunWeightedConsensusAsync(args);
            return;
        }

        if (args.Length >= 2 &&
    args[0].Equals(
        "db-status",
        StringComparison.OrdinalIgnoreCase))
{
    if (!DateOnly.TryParseExact(
            args[1],
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var statusDate))
    {
        Console.WriteLine(
            "[DB-STATUS] Invalid date. Use yyyy-MM-dd.");
        return;
    }

    var connection =
        Environment.GetEnvironmentVariable(
            "ConnectionStrings__FootballDb");

    if (string.IsNullOrWhiteSpace(connection))
    {
        throw new InvalidOperationException(
            "ConnectionStrings__FootballDb is not configured.");
    }

    var options =
        new DbContextOptionsBuilder<FootballDbContext>()
            .UseNpgsql(connection)
            .Options;

    await using var db =
        new FootballDbContext(options);

    DateTime from =
        DateTime.SpecifyKind(
            statusDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Utc);

    DateTime to = from.AddDays(1);

    var matches = await db.Matches
        .AsNoTracking()
        .Where(x =>
            x.KickoffUtc >= from &&
            x.KickoffUtc < to)
        .Select(x => new
        {
            x.Id,
            x.HomeTeam,
            x.AwayTeam
        })
        .ToListAsync();

    var matchIds =
        matches.Select(x => x.Id).ToList();

    var duplicateSnapshots =
        await db.PredictionSnapshots
            .AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId))
            .GroupBy(x => new
            {
                x.MatchId,
                x.Source
            })
            .Where(x => x.Count() > 1)
            .Select(x => new
            {
                x.Key.MatchId,
                x.Key.Source,
                Count = x.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToListAsync();

           Console.WriteLine(
            $"[DB-STATUS] Date: {statusDate:yyyy-MM-dd}");

        Console.WriteLine(
            $"[DB-STATUS] Matches: {matches.Count}");

        Console.WriteLine(
            $"[DB-STATUS] Duplicate Match+Source groups: {duplicateSnapshots.Count}");

        foreach (var duplicate in duplicateSnapshots)
        {
            var match =
                matches.Single(x =>
                    x.Id == duplicate.MatchId);

            Console.WriteLine(
                $"[DUPLICATE] #{match.Id} | " +
                $"{match.HomeTeam} vs {match.AwayTeam} | " +
                $"{duplicate.Source} | " +
                $"Count={duplicate.Count}");
        }

        var sampleSnapshots = await db.PredictionSnapshots
            .AsNoTracking()
            .Where(x =>
                x.MatchId == 94 &&
                x.Source == "ZuluBet")
            .OrderBy(x => x.CapturedAtUtc)
            .ToListAsync();

        Console.WriteLine();
        Console.WriteLine("[SNAPSHOT SAMPLE] Match #94 / ZuluBet");

        foreach (var s in sampleSnapshots)
        {
            Console.WriteLine(
                $"#{s.Id} | " +
                $"{s.CapturedAtUtc:O} | " +
                $"Result={s.PredictedResult} | " +
                $"Score={s.PredictedScore} | " +
                $"BTTS={s.Btts} | " +
                $"Goals={s.Goals} | " +
                $"Conf={s.Confidence}");
        }

        int changedGroups = 0;

            foreach (var duplicate in duplicateSnapshots)
            {
                var variants = await db.PredictionSnapshots
                    .AsNoTracking()
                    .Where(x =>
                        x.MatchId == duplicate.MatchId &&
                        x.Source == duplicate.Source)
                    .Select(x => new
                    {
                        x.PredictedResult,
                        x.PredictedScore,
                        x.Btts,
                        x.Goals,
                        x.Confidence
                    })
                    .Distinct()
                    .CountAsync();

                if (variants > 1)
                {
                    changedGroups++;

                    Console.WriteLine(
                        $"[CHANGED] MatchId={duplicate.MatchId} | " +
                        $"{duplicate.Source} | Variants={variants}");
                }
            }

            Console.WriteLine();
            Console.WriteLine(
                $"[DB-STATUS] Duplicate groups with changed predictions: {changedGroups}");

        return;
    }

        if (args.Length > 0 &&
            args[0].Equals(
                "backtest",
                StringComparison.OrdinalIgnoreCase))
        {

            if (args.Length < 2 ||
                !DateOnly.TryParseExact(
                    args[1],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var backtestDate))
            {
                Console.WriteLine(
                    "[BACKTEST] Invalid date. Use: backtest yyyy-MM-dd");

                return;
            }

            var backtestConnectionString =
                Environment.GetEnvironmentVariable(
                    "ConnectionStrings__FootballDb");

            if (string.IsNullOrWhiteSpace(backtestConnectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings__FootballDb environment variable is not configured.");
            }

            var backtestDbOptions =
                new DbContextOptionsBuilder<FootballDbContext>()
                    .UseNpgsql(backtestConnectionString)
                    .Options;

            await using var backtestDb =
                new FootballDbContext(backtestDbOptions);

            var matches = await backtestDb.Matches
                .AsNoTracking()
                .Include(x => x.Result)
                .Include(x => x.PredictionSnapshots)
                .Where(x =>
                    x.Result != null &&
                    x.MatchDate == backtestDate)
                .ToListAsync();

                Console.WriteLine(
                    $"[BACKTEST] Date: {backtestDate:yyyy-MM-dd}");

            Console.WriteLine(
                $"[BACKTEST] Matches with results: {matches.Count}");

            var stats = new Dictionary<
                string,
                (
                    int ResultTotal,
                    int ResultCorrect,
                    int BttsTotal,
                    int BttsCorrect,
                    int GoalsTotal,
                    int GoalsCorrect
                )>();

            foreach (var match in matches)
            {
                if (match.Result == null)
                    continue;

                foreach (var prediction in match.PredictionSnapshots
                    .GroupBy(x => x.Source)
                    .Select(g => g
                        .OrderByDescending(x => x.CapturedAtUtc)
                        .First()))
                {
                    var grade =
                        PredictionGrader.Grade(
                            prediction,
                            match.Result);

                    if (!stats.TryGetValue(
                            prediction.Source,
                            out var stat))
                    {
                        stat = default;
                    }

                    if (grade.CorrectResult.HasValue)
                    {
                        stat.ResultTotal++;

                        if (grade.CorrectResult.Value)
                            stat.ResultCorrect++;
                    }

                    if (grade.CorrectBtts.HasValue)
                    {
                        stat.BttsTotal++;

                        if (grade.CorrectBtts.Value)
                            stat.BttsCorrect++;
                    }

                    if (grade.CorrectGoals.HasValue)
                    {
                        stat.GoalsTotal++;

                        if (grade.CorrectGoals.Value)
                            stat.GoalsCorrect++;
                    }

                    stats[prediction.Source] = stat;
                }
            }

            Console.WriteLine();

            foreach (var entry in stats.OrderBy(x => x.Key))
            {
                var stat = entry.Value;

                static string Format(
                    int correct,
                    int total)
                {
                    if (total == 0)
                        return "-";

                    double percentage =
                        correct * 100.0 / total;

                    return
                        $"{correct}/{total} ({percentage:F1}%)";
                }

                Console.WriteLine(
                    $"[BACKTEST] {entry.Key}");

                Console.WriteLine(
                    $"  Result: {Format(stat.ResultCorrect, stat.ResultTotal)}");

                Console.WriteLine(
                    $"  BTTS:   {Format(stat.BttsCorrect, stat.BttsTotal)}");

                Console.WriteLine(
                    $"  Goals:  {Format(stat.GoalsCorrect, stat.GoalsTotal)}");
            }

            if (stats.Count == 0)
            {
                Console.WriteLine(
                    "[BACKTEST] No graded predictions yet.");
            }

            Console.WriteLine();
            await PrintWeightedConsensusAsync(
                backtestDb,
                backtestDate,
                showFixtures: false);

            Console.WriteLine("[BACKTEST] Done.");

            return;
        }

        
        if (args.Length > 0 &&
            args[0].Equals(
                "train-model",
                StringComparison.OrdinalIgnoreCase))
        {
            var trainingConnectionString =
                Environment.GetEnvironmentVariable(
                    "ConnectionStrings__FootballDb");

            if (string.IsNullOrWhiteSpace(trainingConnectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings__FootballDb environment variable is not configured.");
            }

            var trainingDbOptions =
                new DbContextOptionsBuilder<FootballDbContext>()
                    .UseNpgsql(trainingConnectionString)
                    .Options;

            await using var trainingDb =
                new FootballDbContext(trainingDbOptions);

            var trainer =
                new FootballModelTrainer(trainingDb);

            var trainingRows =
                await trainer.LoadTrainingDataAsync();

            var matchTrainingRows =
                await trainer.LoadMatchTrainingDataAsync();


            Console.WriteLine(
                $"[ML] Match-level training rows: {matchTrainingRows.Count}");

            var resultModelTrainer = new ResultModelTrainer();
            resultModelTrainer.Train(matchTrainingRows);

            var bttsModelTrainer = new BttsModelTrainer();
            bttsModelTrainer.Train(matchTrainingRows);

            var goalsModelTrainer = new GoalsModelTrainer();
            goalsModelTrainer.Train(matchTrainingRows);
            Console.WriteLine();
            Console.WriteLine(
                $"[ML] Training rows loaded: {trainingRows.Count}");

            Console.WriteLine(
                $"[ML] Matches represented: {trainingRows.Select(x => new { x.MatchDate, x.HomeTeam, x.AwayTeam }).Distinct().Count()}");

            Console.WriteLine(
                $"[ML] Sources represented: {trainingRows.Select(x => x.Source).Distinct().Count()}");

            Console.WriteLine();
            Console.WriteLine("[ML] Rows by source:");

            foreach (var source in trainingRows
                .GroupBy(x => x.Source)
                .OrderByDescending(x => x.Count()))
            {
                Console.WriteLine(
                    $"  {source.Key}: {source.Count()}");
            }

            Console.WriteLine();
            Console.WriteLine("[ML] Actual Result distribution:");

            foreach (var result in trainingRows
                .GroupBy(x => x.ActualResult)
                .OrderBy(x => x.Key))
            {
                Console.WriteLine(
                    $"  {result.Key}: {result.Count()}");
            }

            Console.WriteLine();

            return;
        }

        if (args.Length >= 2 &&
            args[0].Equals(
                "collect-results",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!DateOnly.TryParseExact(
                    args[1],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                Console.WriteLine(
                    "[RESULTS] Invalid date. Use yyyy-MM-dd.");
                CommandExecution.Fail();
                return;
            }

            string resultTargetDate =
                date.ToString("yyyy-MM-dd");

            Console.WriteLine(
                $"[RESULTS] Collecting results for {resultTargetDate}...");

            var collectConnectionString =
                Environment.GetEnvironmentVariable(
                    "ConnectionStrings__FootballDb");

            if (string.IsNullOrWhiteSpace(collectConnectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings__FootballDb environment variable is not configured.");
            }

            var collectDbOptions =
                new DbContextOptionsBuilder<FootballDbContext>()
                    .UseNpgsql(collectConnectionString)
                    .Options;

            await using var collectDb =
                new FootballDbContext(collectDbOptions);

            if (!CommandExecution.IsAutomation)
                await collectDb.Database.MigrateAsync();
            using var httpClient = new HttpClient();
            var primaryCollected = await MatchResultCollector.CollectAsync(date,
                MatchResultCollector.CreatePrimaryProviders(Console.WriteLine), Console.WriteLine);
            if (primaryCollected.FailedProviders > 0 || primaryCollected.RejectedRows > 0)
                CommandExecution.Fail();
            var primaryPersistence = await MatchResultPersister.PersistAsync(
                collectDb, date, primaryCollected.SourceResults, Console.WriteLine);

            var pendingMatches = await collectDb.Matches.AsNoTracking()
                .Where(x => x.MatchDate == date && x.Result == null).ToListAsync();
            CollectedMatchResults fallbackCollected = new([], 0, 0);
            MatchResultPersistenceSummary fallbackPersistence = new(0, 0, 0, 0, 0, 0);
            if (pendingMatches.Count > 0)
            {
                Console.WriteLine($"[RESULTS] {pendingMatches.Count} fixtures still have no stored result; " +
                    "checking only those fixtures with SoccerVista.");
                fallbackCollected = await MatchResultCollector.CollectTargetedAsync(date,
                    new SoccerVistaResultProvider(httpClient), pendingMatches, Console.WriteLine);
                if (fallbackCollected.FailedProviders > 0 || fallbackCollected.RejectedRows > 0)
                    CommandExecution.Fail();
                fallbackPersistence = await MatchResultPersister.PersistAsync(
                    collectDb, date, fallbackCollected.SourceResults, Console.WriteLine);
            }
            else
                Console.WriteLine("[RESULTS] No fixtures are missing results; SoccerVista fallback skipped.");

            var collected = new CollectedMatchResults(
                primaryCollected.Results.Concat(fallbackCollected.Results).ToList(),
                primaryCollected.FailedProviders + fallbackCollected.FailedProviders,
                primaryCollected.RejectedRows + fallbackCollected.RejectedRows)
            {
                SourceResults = primaryCollected.SourceResults.Concat(fallbackCollected.SourceResults).ToList()
            };
            int matched = primaryPersistence.Matched + fallbackPersistence.Matched;
            int inserted = primaryPersistence.Inserted + fallbackPersistence.Inserted;
            int existing = primaryPersistence.Existing + fallbackPersistence.Existing;
            int unmatched = primaryPersistence.Unmatched + fallbackPersistence.Unmatched;
            int halfTimeUpdated = primaryPersistence.HalfTimeUpdated + fallbackPersistence.HalfTimeUpdated;
            int changes = primaryPersistence.Changes + fallbackPersistence.Changes;
            var externalResults = collected.Results;

            Console.WriteLine();
            Console.WriteLine($"[RESULTS] Matched: {matched}");
            Console.WriteLine($"[RESULTS] Inserted: {inserted}");
            Console.WriteLine($"[RESULTS] Existing: {existing}");
            Console.WriteLine($"[RESULTS] Half-time backfilled: {halfTimeUpdated}");
            Console.WriteLine($"[RESULTS] Half-time supplied: {externalResults.Count(x => x.HalfTimeHomeGoals.HasValue && x.HalfTimeAwayGoals.HasValue)}");
            Console.WriteLine($"[RESULTS] Unmatched: {unmatched}");
            Console.WriteLine($"[RESULTS] Failed providers: {collected.FailedProviders}; rejected rows/conflicts: {collected.RejectedRows}");
            Console.WriteLine($"[RESULTS] EF changes: {changes}");
            int dateMatches = await collectDb.Matches.CountAsync(x => x.MatchDate == date);
            int finalResults = await collectDb.Matches.CountAsync(x => x.MatchDate == date && x.Result != null);
            Console.WriteLine($"[RESULTS] Date coverage: FT={finalResults}/{dateMatches}; missing FT={dateMatches - finalResults}.");
            var tracked = await collectDb.SimulatedTickets.AsNoTracking().Where(x => x.MatchDate == date)
                .SelectMany(x => x.Selections).Select(x => x.MatchId).Distinct().ToListAsync();
            int trackedResults = await collectDb.MatchResults.CountAsync(x => tracked.Contains(x.MatchId));
            Console.WriteLine($"[RESULTS] Tracked ticket fixtures: FT={trackedResults}/{tracked.Count}; missing FT={tracked.Count - trackedResults}.");
            Console.WriteLine("[RESULTS] Missing scores do not prove a fixture is unfinished; sources may be incomplete or names may not match.");
            var trackedTickets = await collectDb.SimulatedTickets.AsNoTracking().Include(x => x.Selections)
                .Where(x => x.MatchDate == date).ToListAsync();
            await TicketResultDiagnostics.WriteAsync(collectDb, trackedTickets, Console.WriteLine);
            Console.WriteLine(CommandExecution.ExitCode == 0 ? "[RESULTS] Done." : "[RESULTS] Completed with errors; see provider/conflict messages.");

            return;
        }
       
        if (args.Length > 0 &&
    DateTime.TryParseExact(
        args[0],
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out DateTime cliDate))
        {
            string targetDate = cliDate.ToString("yyyy-MM-dd");

            Console.WriteLine($"[CLI] Starting pipeline for {targetDate}");

            var aggregator =
                new ProductionConsensusAggregator(targetDate);

            using (var playwright = await Playwright.CreateAsync())
            {
                var browser =
                    await playwright.Chromium.LaunchAsync(
                        new BrowserTypeLaunchOptions
                        {
                            Headless = true
                        });

                try
                {
                    var forebetTask =
                        RunScraperAsync(() => aggregator.ParseForebetAsync(browser));

                    var statareaTask =
                        RunScraperAsync(() => aggregator.ParseStatareaAsync(browser));

                    var predictzTask =
                        RunScraperAsync(() => aggregator.ParsePredictzAsync(browser));

                    var wdwTask =
                        RunScraperAsync(() => aggregator.ParseWinDrawWinAsync(browser));

                    var zulubetTask =
                        RunScraperAsync(() => aggregator.ParseZuluBetAsync(browser));

                    var vitibetTask =
                        RunScraperAsync(() => aggregator.ParseVitibetAsync(browser));

                    var feedincoTask =
                        RunScraperAsync(() => aggregator.ParseFeedincoAsync(browser));

                    var soccerVistaTask =
                        RunScraperAsync(() => aggregator.ParseSoccerVistaAsync(browser));

                    var footyStatsTask =
                        RunScraperAsync(() => aggregator.ParseFootyStatsAsync(browser));

                    var oddslotTask =
                        RunScraperAsync(() => aggregator.ParseOddslotAsync(browser));

                    var theDatabeticsTask =
                        RunScraperAsync(() => aggregator.ParseTheDatabeticsAsync(browser));

                    await Task.WhenAll(
                        forebetTask,
                        statareaTask,
                        predictzTask,
                        wdwTask,
                        zulubetTask,
                        vitibetTask,
                        feedincoTask,
                        soccerVistaTask,
                        footyStatsTask,
                        oddslotTask,
                        theDatabeticsTask);

                    if (CommandExecution.IsAutomation && !DailyRunPlan.CheckSourceCoverage(
                        [
                            ("Forebet", (await forebetTask).Count),
                            ("Statarea", (await statareaTask).Count),
                            ("PredictZ", (await predictzTask).Count),
                            ("WinDrawWin", (await wdwTask).Count),
                            ("ZuluBet", (await zulubetTask).Count),
                            ("Vitibet", (await vitibetTask).Count),
                            ("Feedinco", (await feedincoTask).Count),
                            ("SoccerVista", (await soccerVistaTask).Count),
                            ("FootyStats", (await footyStatsTask).Count),
                            ("Oddslot", (await oddslotTask).Count),
                            ("TheDatabetics", (await theDatabeticsTask).Count)
                        ], Console.WriteLine))
                        CommandExecution.Fail();

                    aggregator.ExecutePipeline(
                        await forebetTask,
                        await statareaTask,
                        await predictzTask,
                        await wdwTask,
                        await zulubetTask,
                        await vitibetTask,
                        await feedincoTask,
                        await soccerVistaTask,
                        await footyStatsTask,
                        await oddslotTask,
                        await theDatabeticsTask);

                    var cliConnectionString =
                        Environment.GetEnvironmentVariable("ConnectionStrings__FootballDb");

                    if (string.IsNullOrWhiteSpace(cliConnectionString))
                    {
                        throw new InvalidOperationException(
                            "ConnectionStrings__FootballDb environment variable is not configured.");
                    }

                    var dbOptions =
                        new DbContextOptionsBuilder<FootballDbContext>()
                            .UseNpgsql(cliConnectionString)
                            .Options;

                    await using (var db =
                        new FootballDbContext(dbOptions))
                    {
                        if (!CommandExecution.IsAutomation)
                            await db.Database.MigrateAsync();
                        var snapshotWriter =
                            new PredictionSnapshotWriter(db);

                        await snapshotWriter.SaveAsync(
                            targetDate,
                            aggregator.UnifiedMatches);
                    }


                    aggregator.PrintGuaranteedReport();
                }
                finally
                {
                    await browser.CloseAsync();
                }
            }

            Console.WriteLine(
                $"[CLI] Analysis finished: {targetDate}");

            return;
        }

        var builder = WebApplication.CreateBuilder(args);

        var connectionString = builder.Configuration
            .GetConnectionString("FootballDb");

        builder.Services.AddDbContext<FootballDbContext>(options =>
            options.UseNpgsql(connectionString));
        builder.Services.AddScoped<Prediction.Services.IDatabaseReportService, Prediction.Services.DatabaseReportService>();
        builder.Services.AddProblemDetails();

        var app = builder.Build();

        ConfigureWebAccess(app);
        app.UseExceptionHandler();
        Prediction.Web.DatabaseReportEndpoints.MapDatabaseReports(app);

        if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings__FootballDb is not configured.");

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FootballDbContext>();
            await db.Database.MigrateAsync();
        }

        // Render go dava PORT kako environment variable.
        // Lokalno koristime 10000.
        string port =
            Environment.GetEnvironmentVariable("PORT") ?? "10000";

        app.Urls.Clear();
        app.Urls.Add($"http://0.0.0.0:{port}");

        // ------------------------------------------------
        // HOME PAGE
        // ------------------------------------------------

        app.MapGet("/", async context =>
        {
            context.Response.ContentType =
                "text/html; charset=utf-8";

            await context.Response.WriteAsync(GetHomePage());
        });

        // ------------------------------------------------
        // ANALYSE
        // ------------------------------------------------

        app.MapGet("/analyse", async context =>
        {
            string targetDate =
                context.Request.Query["date"].ToString();

            if (!DateTime.TryParseExact(
                    targetDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsedDate))
            {
                context.Response.StatusCode = 400;

                await context.Response.WriteAsync(
                    "Invalid date. Use yyyy-MM-dd.");

                return;
            }

            targetDate =
                parsedDate.ToString("yyyy-MM-dd");

            await analysisLock.WaitAsync();

            TextWriter originalOutput = Console.Out;

            var writer = new StringWriter();

            try
            {
                // Log to Render BEFORE redirecting Console.Out.
                Console.WriteLine(
                    $"[ANALYSE] Starting pipeline for {targetDate}");

                Console.SetOut(writer);

                Console.WriteLine(
                    $"[+] Starting pipeline for {targetDate}");

                var aggregator =
                    new ProductionConsensusAggregator(targetDate);

                using (var playwright =
                    await Playwright.CreateAsync())
                {
                    var browser =
                        await playwright.Chromium.LaunchAsync(
                            new BrowserTypeLaunchOptions
                            {
                                Headless = true
                            });

                    try
                    {
                        var forebetTask =
                            RunScraperAsync(() => aggregator.ParseForebetAsync(browser));

                        var statareaTask =
                            RunScraperAsync(() => aggregator.ParseStatareaAsync(browser));

                        var predictzTask =
                            RunScraperAsync(() => aggregator.ParsePredictzAsync(browser));

                        var wdwTask =
                            RunScraperAsync(() => aggregator.ParseWinDrawWinAsync(browser));

                        var zulubetTask =
                            RunScraperAsync(() => aggregator.ParseZuluBetAsync(browser));
                        
                        var vitibetTask =
                            RunScraperAsync(() => aggregator.ParseVitibetAsync(browser));
                        
                        var feedincoTask =
                            RunScraperAsync(() => aggregator.ParseFeedincoAsync(browser));

                        var soccerVistaTask =
                            RunScraperAsync(() => aggregator.ParseSoccerVistaAsync(browser));

                        var footyStatsTask =
                            RunScraperAsync(() => aggregator.ParseFootyStatsAsync(browser));

                        var oddslotTask =
                            RunScraperAsync(() => aggregator.ParseOddslotAsync(browser));

                        var theDatabeticsTask =
                            RunScraperAsync(() => aggregator.ParseTheDatabeticsAsync(browser));

                        await Task.WhenAll(
                            forebetTask,
                            statareaTask,
                            predictzTask,
                            wdwTask,
                            zulubetTask,
                            vitibetTask,
                            feedincoTask,
                            soccerVistaTask,
                            footyStatsTask,
                            oddslotTask,
                            theDatabeticsTask);

                        aggregator.ExecutePipeline(
                            await forebetTask,
                            await statareaTask,
                            await predictzTask,
                            await wdwTask,
                            await zulubetTask,
                            await vitibetTask,
                            await feedincoTask,
                            await soccerVistaTask,
                            await footyStatsTask,
                            await oddslotTask,
                            await theDatabeticsTask);

                        await using var scope = app.Services.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<FootballDbContext>();
                        var snapshotWriter = new PredictionSnapshotWriter(db);
                        await snapshotWriter.SaveAsync(targetDate, aggregator.UnifiedMatches);

                        aggregator.PrintGuaranteedReport();
                    }
                    finally
                    {
                        await browser.CloseAsync();
                    }
                }

                string result = writer.ToString();

                Console.SetOut(originalOutput);

                Console.WriteLine(
                    $"Analysis finished: {targetDate}");

                context.Response.ContentType =
                    "text/html; charset=utf-8";

                await context.Response.WriteAsync(
                    GetResultPage(
                        targetDate,
                        result));
            }
            catch (Exception ex)
            {
                Console.SetOut(originalOutput);

                Console.WriteLine(ex);

                context.Response.StatusCode = 500;

                context.Response.ContentType =
                    "text/html; charset=utf-8";

                await context.Response.WriteAsync(
                    GetErrorPage(ex.Message));
            }
            finally
            {
                Console.SetOut(originalOutput);

                writer.Dispose();

                analysisLock.Release();
            }
        });

        // ------------------------------------------------
        // HEALTH CHECK
        // ------------------------------------------------

        app.MapGet("/health", () =>
            Results.Ok(new
            {
                status = "OK",
                service = "Football AI"
            }));

        await app.RunAsync();
    }    
}
