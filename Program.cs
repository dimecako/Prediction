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

public partial class ProductionConsensusAggregator
{
    private string targetDate;
    private string todayStr;

    private static readonly SemaphoreSlim flareSemaphore = new SemaphoreSlim(1, 1);

    private readonly string flareSolverrUrl = Environment.GetEnvironmentVariable("FLARESOLVERR_URL") ?? "http://localhost:8191/v1";

    private ConcurrentDictionary<string, UnifiedMatch> unifiedDb = new ConcurrentDictionary<string, UnifiedMatch>();

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