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

    private async Task<HtmlDocument> GetPageFromFlareSolverrCore(string url)
    {
        await flareSemaphore.WaitAsync();

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(210)
            };

            var payload = new
            {
                cmd = "request.get",
                url = url,
                maxTimeout = 180000
            };

            string json = JsonConvert.SerializeObject(payload);

            Console.WriteLine($"[FlareSolverr] GET {url}");

            var response = await client.PostAsync(
                flareSolverrUrl,
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"));

            string result =
                await response.Content.ReadAsStringAsync();

            Console.WriteLine(
                $"[FlareSolverr] HTTP {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"[FlareSolverr] RESPONSE: {result}");

                return null;
            }

            dynamic obj =
                JsonConvert.DeserializeObject(result);

            if (obj?.solution?.response == null)
                return null;

            string html =
                obj.solution.response.ToString();

            Console.WriteLine(
                $"[FlareSolverr] HTML length: {html.Length}");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return doc;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[FlareSolverr ERROR] {ex.Message}");

            return null;
        }
        finally
        {
            flareSemaphore.Release();
        }
    }

    
    private async Task<HtmlDocument> GetPageFromFlareSolverr(string url)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var doc = await GetPageFromFlareSolverrCore(url);

                if (doc != null)
                    return doc;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FlareSolverr] Attempt {attempt}/{maxAttempts} failed: {ex.Message}");
            }

            if (attempt < maxAttempts)
            {
                int delaySeconds = attempt * 5;

                Console.WriteLine(
                    $"[FlareSolverr] Retry in {delaySeconds}s...");

                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds));
            }
        }

        Console.WriteLine(
            $"[FlareSolverr] FAILED after {maxAttempts} attempts: {url}");

        return null;
    }
    
    private async Task<string> GetRawFromFlareSolverr(string url)
    {
        await flareSemaphore.WaitAsync();

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(210)
            };

            var payload = new
            {
                cmd = "request.get",
                url = url,
                maxTimeout = 180000
            };

            string json =
                JsonConvert.SerializeObject(payload);

            Console.WriteLine(
                $"[FlareSolverr RAW] GET {url}");

            var response = await client.PostAsync(
                flareSolverrUrl,
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"));

            string result =
                await response.Content.ReadAsStringAsync();

            Console.WriteLine(
                $"[FlareSolverr RAW] HTTP {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"[FlareSolverr RAW] RESPONSE: {result}");

                return "";
            }

            dynamic obj =
                JsonConvert.DeserializeObject(result);

            if (obj?.solution?.response == null)
                return "";

            return obj.solution.response.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[FlareSolverr RAW ERROR] {ex.Message}");

            return "";
        }
        finally
        {
            flareSemaphore.Release();
        }
    }
    
    private async Task<HtmlDocument> ScrapeHtmlAsync(IPage page, string url)
    {
        try
        {
            await page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });

            string html = await page.ContentAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return doc;
        }
        catch
        {
            return null;
        }
    }   
 

    
    
    private async Task<HtmlDocument> GetPageWithPlaywrightAsync(
    IBrowser browser,
    string url,
    string selector,
    string sourceName)
    {
        var page = await browser.NewPageAsync();

        try
        {
            Console.WriteLine(
                $"[{sourceName} PLAYWRIGHT] GET {url}");

            await page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] Final URL: {page.Url}");

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] Title: {await page.TitleAsync()}");

                string diagnosticHtml = await page.ContentAsync();

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] HTML before wait: {diagnosticHtml.Length} chars");

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] pttr count: " +
                    $"{await page.Locator("div.pttr").CountAsync()}");

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] ptcnt count: " +
                    $"{await page.Locator("div.ptcnt").CountAsync()}");

                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] combined count: " +
                    $"{await page.Locator("div.pttr.ptcnt").CountAsync()}");

            await page
                .Locator(selector)
                .First
                .WaitForAsync(
                    new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = 30000
                    });

            string html = await page.ContentAsync();

            if (string.IsNullOrWhiteSpace(html))
            {
                Console.WriteLine(
                    $"[{sourceName} PLAYWRIGHT] Empty HTML");

                return null;
            }

            var soup = new HtmlDocument();
            soup.LoadHtml(html);

            Console.WriteLine(
                $"[{sourceName} PLAYWRIGHT] HTML: {html.Length} chars");

            return soup;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[{sourceName} PLAYWRIGHT ERROR] {ex.Message}");

            return null;
        }
        finally
        {
            await page.CloseAsync();
        }
    }   
}


public class Program
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


    private static string GetHomePage()
    {
        string today =
            DateTime.Today.ToString("yyyy-MM-dd");

        return $@"
<!DOCTYPE html>
<html>
<head>

<meta charset='utf-8'>
<meta name='viewport'
      content='width=device-width, initial-scale=1'>

<title>Football AI</title>

<style>

body {{
    margin: 0;
    font-family: Arial, sans-serif;
    background: #0f172a;
    color: white;
}}

.container {{
    max-width: 700px;
    margin: 70px auto;
    padding: 20px;
}}

.card {{
    background: #1e293b;
    padding: 35px;
    border-radius: 16px;
}}

h1 {{
    margin-top: 0;
    color: #38bdf8;
}}

input {{
    width: 100%;
    box-sizing: border-box;
    padding: 14px;
    margin-top: 10px;
    margin-bottom: 20px;
    border-radius: 8px;
    border: 1px solid #475569;
    background: #0f172a;
    color: white;
    font-size: 18px;
}}

button {{
    width: 100%;
    padding: 15px;
    border: 0;
    border-radius: 8px;
    background: #0284c7;
    color: white;
    font-size: 18px;
    font-weight: bold;
    cursor: pointer;
}}

button:hover {{
    background: #0369a1;
}}

.small {{
    color: #94a3b8;
    margin-top: 20px;
}}

</style>

</head>

<body>

<div class='container'>

<div class='card'>

<h1>⚽ Football AI</h1>

<p>
Select prediction date:
</p>

<input
    id='analysisDate'
    type='date'
    value='{today}'
    required>

<button
    type='button'
    onclick=""window.location.href='/analyse?date=' + document.getElementById('analysisDate').value"">
    ANALYSE
</button>

<div class='small'>
Forebet · Statarea · PredictZ · WinDrawWin
</div>

</div>

</div>

</body>
</html>";
    }


    private static string GetResultPage(
        string date,
        string result)
    {
        string encoded =
            WebUtility.HtmlEncode(result);

        return $@"
<!DOCTYPE html>

<html>

<head>

<meta charset='utf-8'>

<meta name='viewport'
      content='width=device-width, initial-scale=1'>

<title>Football AI - {date}</title>

<style>

body {{
    margin: 0;
    background: #0f172a;
    color: #e2e8f0;
    font-family: Arial, sans-serif;
}}

.container {{
    max-width: 1400px;
    margin: auto;
    padding: 25px;
}}

h1 {{
    color: #38bdf8;
}}

a {{
    color: #38bdf8;
    text-decoration: none;
}}

pre {{
    background: #020617;
    padding: 20px;
    border-radius: 12px;
    overflow-x: auto;
    font-size: 14px;
    line-height: 1.5;
}}

</style>

</head>

<body>

<div class='container'>

<h1>⚽ Football AI</h1>

<h2>
Prediction date: {date}
</h2>

<p>
/
← New analysis
</a>
</p>

<pre>{encoded}</pre>

</div>

</body>

</html>";
    }


    private static string GetErrorPage(
        string error)
    {
        return $@"
<!DOCTYPE html>

<html>

<head>

<meta charset='utf-8'>

<title>Football AI Error</title>

</head>

<body>

<h1>Football AI</h1>

<h2>Analysis failed</h2>

<pre>
{WebUtility.HtmlEncode(error)}
</pre>

/
Back
</a>

</body>

</html>";
    }
}