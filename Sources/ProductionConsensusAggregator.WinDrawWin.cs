using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

public partial class ProductionConsensusAggregator
{
     public async Task<List<SiteMatch>> ParseWinDrawWinAsync(IBrowser browser)
    {
        var matches = new List<SiteMatch>();
        var page = await browser.NewPageAsync();
        string url = (targetDate == todayStr) ? "https://www.windrawwin.com/predictions/today/" : 
                     (targetDate == DateTime.Today.AddDays(1).ToString("yyyy-MM-dd")) ? "https://www.windrawwin.com/predictions/tomorrow/" : 
                     $"https://www.windrawwin.com/predictions/future/{targetDate.Replace("-", "")}/";

        var soup = await GetPageFromFlareSolverr(url);
        if (soup == null) { await page.CloseAsync(); return matches; }

        var rows = soup.DocumentNode.SelectNodes("//div[contains(@class,'wttr')]");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                try
                {
                    var matchLink = row.SelectSingleNode(".//a[contains(@class,'wtdesklnk')]");
                    if (matchLink == null) continue;

                    string matchText = HtmlEntity.DeEntitize(matchLink.InnerText).Trim();
                    var parts = Regex.Split(matchText, @"\s*v\s*", RegexOptions.IgnoreCase);
                    if (parts.Length != 2) continue;

                    string home = parts[0].Trim();
                    string away = parts[1].Trim();

                    var predNode = row.SelectSingleNode(".//div[contains(@class,'wtprd')]");
                    if (predNode == null) continue;

                    string predText = HtmlEntity.DeEntitize(predNode.InnerText).Trim().ToLower();
                    string tip = predText.Contains("home") ? "1" : (predText.Contains("away") ? "2" : (predText.Contains("draw") ? "X" : ""));
                    if (string.IsNullOrEmpty(tip)) continue;

                    var scoreNode = row.SelectSingleNode(".//div[contains(@class,'wtsc')]");
                    string score = scoreNode?.InnerText?.Trim() ?? "-";

                    matches.Add(new SiteMatch
                    {
                        SiteName = "WinDrawWin",
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