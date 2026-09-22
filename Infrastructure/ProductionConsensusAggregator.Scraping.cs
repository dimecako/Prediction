using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Playwright;
using Newtonsoft.Json;

public partial class ProductionConsensusAggregator
{
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