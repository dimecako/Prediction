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

    public static async Task Main(string[] args)
    {
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
            Console.WriteLine("[BACKTEST] Done.");

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
                return;
            }

            string resultTargetDate =
                date.ToString("yyyy-MM-dd");

            Console.WriteLine(
                $"[RESULTS] Collecting results for {resultTargetDate}...");

            using var httpClient = new HttpClient();

            // 1. Forebet FT results
            var forebetAggregator =
                new ProductionConsensusAggregator(resultTargetDate);

            var externalResults =
                await forebetAggregator.GetForebetResultsAsync(date);

            Console.WriteLine(
                $"[RESULTS] Forebet returned: {externalResults.Count}");

            // 2. Fallback: FootballResultsOnline
            if (externalResults.Count == 0)
            {
                Console.WriteLine(
                    "[RESULTS] Forebet returned no results. Trying FootballResultsOnline...");

                IMatchResultProvider provider =
                    new FootballResultsOnlineProvider(httpClient);

                externalResults =
                    (await provider.GetResultsAsync(date)).ToList();

                Console.WriteLine(
                    $"[RESULTS] {provider.Name} returned: {externalResults.Count}");

                // 3. Fallback: OpenFootball
                if (externalResults.Count == 0)
                {
                    Console.WriteLine(
                        "[RESULTS] FootballResultsOnline returned no results. Trying OpenFootball...");

                    provider =
                        new OpenFootballResultProvider(httpClient);

                    externalResults =
                        (await provider.GetResultsAsync(date)).ToList();

                    Console.WriteLine(
                        $"[RESULTS] {provider.Name} returned: {externalResults.Count}");
                }
            }

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

            int matched = 0;
            int inserted = 0;
            int existing = 0;
            int unmatched = 0;

            foreach (var external in externalResults)
            {
                string externalMatchId =
                    MatchIdentity.BuildExternalMatchId(
                        resultTargetDate,
                        external.HomeTeam,
                        external.AwayTeam);

                var match = await collectDb.Matches
                    .Include(x => x.Result)
                    .SingleOrDefaultAsync(
                        x => x.ExternalMatchId == externalMatchId);

                if (match == null)
                {
                    Console.WriteLine(
                        $"[UNMATCHED] {external.HomeTeam} | " +
                        $"{external.AwayTeam} | " +
                        $"{external.HomeGoals}-{external.AwayGoals}");

                    unmatched++;
                    continue;
                }

                matched++;

                if (match.Result != null)
                {
                    Console.WriteLine(
                        $"[EXISTS] #{match.Id} | " +
                        $"{match.HomeTeam} | {match.AwayTeam} | " +
                        $"{match.Result.HomeGoals}-{match.Result.AwayGoals}");

                    existing++;
                    continue;
                }

                string result =
                    external.HomeGoals > external.AwayGoals
                        ? "1"
                        : external.HomeGoals < external.AwayGoals
                            ? "2"
                            : "X";

                match.Result =
                    new Prediction.Entities.MatchResult
                    {
                        MatchId = match.Id,

                        HomeGoals = external.HomeGoals,
                        AwayGoals = external.AwayGoals,

                        Result = result,

                        Btts =
                            external.HomeGoals > 0 &&
                            external.AwayGoals > 0,

                        TotalGoals =
                            external.HomeGoals +
                            external.AwayGoals,

                        Source = external.Source,

                        CollectedAtUtc = DateTime.UtcNow
                    };

                inserted++;

                Console.WriteLine(
                    $"[SAVED] #{match.Id} | " +
                    $"{match.HomeTeam} | {match.AwayTeam} | " +
                    $"{external.HomeGoals}-{external.AwayGoals} | " +
                    $"Result={result} | " +
                    $"BTTS={(external.HomeGoals > 0 && external.AwayGoals > 0 ? "YES" : "NO")} | " +
                    $"Goals={external.HomeGoals + external.AwayGoals}");
            }

            int changes =
                await collectDb.SaveChangesAsync();

            Console.WriteLine();
            Console.WriteLine($"[RESULTS] Matched: {matched}");
            Console.WriteLine($"[RESULTS] Inserted: {inserted}");
            Console.WriteLine($"[RESULTS] Existing: {existing}");
            Console.WriteLine($"[RESULTS] Unmatched: {unmatched}");
            Console.WriteLine($"[RESULTS] EF changes: {changes}");
            Console.WriteLine("[RESULTS] Done.");

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
                        aggregator.ParseForebetAsync(browser);

                    var statareaTask =
                        aggregator.ParseStatareaAsync(browser);

                    var predictzTask =
                        aggregator.ParsePredictzAsync(browser);

                    var wdwTask =
                        aggregator.ParseWinDrawWinAsync(browser);

                    var zulubetTask =
                        aggregator.ParseZuluBetAsync(browser);

                    await Task.WhenAll(
                        forebetTask,
                        statareaTask,
                        predictzTask,
                        wdwTask,
                        zulubetTask);

                    aggregator.ExecutePipeline(
                        await forebetTask,
                        await statareaTask,
                        await predictzTask,
                        await wdwTask,
                        await zulubetTask);

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

        var app = builder.Build();

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
                            aggregator.ParseForebetAsync(browser);

                        var statareaTask =
                            aggregator.ParseStatareaAsync(browser);

                        var predictzTask =
                            aggregator.ParsePredictzAsync(browser);

                        var wdwTask =
                            aggregator.ParseWinDrawWinAsync(browser);

                        var zulubetTask =
                            aggregator.ParseZuluBetAsync(browser);

                        await Task.WhenAll(
                            forebetTask,
                            statareaTask,
                            predictzTask,
                            wdwTask,
                            zulubetTask);

                        aggregator.ExecutePipeline(
                            await forebetTask,
                            await statareaTask,
                            await predictzTask,
                            await wdwTask,
                            await zulubetTask);

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