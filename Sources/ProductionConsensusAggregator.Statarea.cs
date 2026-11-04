using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Playwright;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParseStatareaAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();
            var page = await browser.NewPageAsync();

            try
            {
                string url =
                    $"https://www.statarea.com/predictions/date/{targetDate}/";

                Console.WriteLine($"[Statarea PLAYWRIGHT] GET {url}");

                await page.GotoAsync(
                    url,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 60000
                    });

                string html = null;

                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    try
                    {
                        Console.WriteLine(
                            $"[Statarea] Waiting for matches, attempt {attempt}/5");

                        await page
                            .Locator("div.match")
                            .First
                            .WaitForAsync(
                                new LocatorWaitForOptions
                                {
                                    State = WaitForSelectorState.Attached,
                                    Timeout = 15000
                                });

                        await page.WaitForTimeoutAsync(1000);

                        html = await page.ContentAsync();

                        Console.WriteLine(
                            $"[Statarea] HTML received: {html.Length} chars");

                        break;
                    }
                    catch (PlaywrightException ex)
                    {
                        Console.WriteLine(
                            $"[Statarea] Attempt {attempt}/5 failed: {ex.Message}");

                        if (attempt < 5)
                            await page.WaitForTimeoutAsync(1500);
                    }
                }

                if (string.IsNullOrWhiteSpace(html))
                {
                    Console.WriteLine("[Statarea] Empty HTML.");
                    return matches;
                }

                var soup = new HtmlDocument();
                soup.LoadHtml(html);

                var headerRow =
                    soup.DocumentNode.SelectSingleNode(
                        "//tr[contains(@class, 'had') or contains(@class, 'header')]");

                Dictionary<string, int> columnIndices =
                    new Dictionary<string, int>
                    {
                        { "1", 3 },
                        { "X", 4 },
                        { "2", 5 },
                        { "btts", 11 }
                    };

                if (headerRow != null)
                {
                    var headers = headerRow
                        .SelectNodes(".//td | .//th")
                        ?.Select(n => n.InnerText.Trim().ToLower())
                        .ToList();

                    if (headers != null)
                    {
                        var keys = new[]
                        {
                            ("1", "1"),
                            ("X", "x"),
                            ("2", "2"),
                            ("btts", "btts")
                        };

                        foreach (var k in keys)
                        {
                            if (headers.Contains(k.Item2))
                                columnIndices[k.Item1] =
                                    headers.IndexOf(k.Item2);
                        }
                    }
                }

                var matchRows =
                    soup.DocumentNode.SelectNodes(
                        "//div[@class='match']");

                if (matchRows == null)
                {
                    Console.WriteLine(
                        "[Statarea] No match rows found.");

                    return matches;
                }

                foreach (var row in matchRows)
                {
                    try
                    {
                        var homeNode =
                            row.SelectSingleNode(
                                ".//div[@class='hostteam']//div[@class='name']/a");

                        var awayNode =
                            row.SelectSingleNode(
                                ".//div[@class='guestteam']//div[@class='name']/a");

                        if (homeNode == null || awayNode == null)
                            continue;

                        string home = homeNode.InnerText.Trim();
                        string away = awayNode.InnerText.Trim();

                        var values =
                            row.SelectNodes(
                                ".//*[contains(@class, 'value')] | .//td[contains(@class, 'value')]");

                        if (values == null ||
                            values.Count <= columnIndices.Values.Max())
                            continue;

                        if (!int.TryParse(
                                values[columnIndices["1"]]
                                    .InnerText.Replace("%", "").Trim(),
                                out int p1))
                            continue;

                        if (!int.TryParse(
                                values[columnIndices["X"]]
                                    .InnerText.Replace("%", "").Trim(),
                                out int pX))
                            continue;

                        if (!int.TryParse(
                                values[columnIndices["2"]]
                                    .InnerText.Replace("%", "").Trim(),
                                out int p2))
                            continue;

                        string bttsYes =
                            values.Count > columnIndices["btts"]
                                ? values[columnIndices["btts"]]
                                    .InnerText.Trim()
                                : "-";

                        int maxP =
                            Math.Max(p1, Math.Max(pX, p2));

                        string tip =
                            maxP == p1 ? "1" :
                            maxP == pX ? "X" : "2";

                        matches.Add(
                            new SiteMatch
                            {
                                SiteName = "Statarea",
                                HomeTeam = home,
                                AwayTeam = away,
                                Tip = tip,
                                Prob = maxP,
                                Btts = bttsYes,
                                BttsMarket =
                                    int.TryParse(bttsYes, out int b)
                                        ? (b >= 50 ? "YES" : "NO")
                                        : "-"
                            });
                    }
                    catch
                    {
                        continue;
                    }
                }

                Console.WriteLine(
                    $"[Statarea PLAYWRIGHT] Parsed: {matches.Count}");

                return matches;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Statarea PLAYWRIGHT ERROR] {ex}");

                return matches;
            }
            finally
            {
                await page.CloseAsync();
            }
    }
}