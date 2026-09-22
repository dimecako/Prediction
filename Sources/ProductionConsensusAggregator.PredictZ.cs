using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

public partial class ProductionConsensusAggregator
{
    public async Task<List<SiteMatch>> ParsePredictzAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();
        var page = await browser.NewPageAsync();
        string url = (targetDate == todayStr) ? "https://www.predictz.com/predictions/" : $"https://www.predictz.com/predictions/{targetDate.Replace("-", "")}/";
        
       //var soup = await GetPageWithPlaywrightAsync(browser, url, "div.pttr.ptcnt", "PredictZ");
       // STARO
        var soup = await GetPageFromFlareSolverr(url);

        if (soup == null)
        {
            await page.CloseAsync();
            return matches;
        }

        var rows = soup.DocumentNode.SelectNodes("//div[contains(@class,'pttr') and contains(@class,'ptcnt')]");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                try
                {
                    var matchNode = row.SelectSingleNode(".//div[contains(@class,'ptgame')]//a");
                    var predNode = row.SelectSingleNode(".//div[contains(@class,'ptpredboxsml')]");
                    if (matchNode == null || predNode == null) continue;

                    string matchText = HtmlEntity.DeEntitize(matchNode.InnerText.Trim());
                    string predText = HtmlEntity.DeEntitize(predNode.InnerText.Trim());
                    var parts = Regex.Split(matchText, @"\s+v\s+", RegexOptions.IgnoreCase);
                    if (parts.Length != 2) continue;

                    string home = parts[0].Trim();
                    string away = parts[1].Trim();
                    string tip = predText.StartsWith("Home") ? "1" : (predText.StartsWith("Away") ? "2" : (predText.StartsWith("Draw") ? "X" : null));
                    if (tip == null) continue;

                    string score = "-";
                    var scoreMatch = Regex.Match(predText, @"(\d+\-\d+)");
                    if (scoreMatch.Success) score = scoreMatch.Groups[1].Value;

                    matches.Add(new SiteMatch
                    {
                        SiteName = "PredictZ",
                        HomeTeam = home,
                        AwayTeam = away,
                        Tip = tip,
                        Score = score,
                        BttsMarket = GetBttsMarket(score),
                        GoalsMarket = GetGoalMarket(score)
                    });
                }
                catch { continue; }
            }
        }
        await page.CloseAsync();
        return matches;
    } 
}