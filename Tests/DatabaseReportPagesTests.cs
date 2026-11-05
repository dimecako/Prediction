using HtmlAgilityPack;
using Prediction.Entities;
using Prediction.Services;
using Prediction.Web;
using Xunit;

namespace Prediction.Tests;

public sealed class DatabaseReportPagesTests
{
    [Fact]
    public void TicketDropdownIncludesFixedAndAiFixedModes()
    {
        Assert.Contains("fixed-1", DatabaseReportService.TicketModes);
        Assert.Contains("fixed-2", DatabaseReportService.TicketModes);
        Assert.Contains("ai-fixed-1", DatabaseReportService.TicketModes);
        Assert.Contains("ai-fixed-2", DatabaseReportService.TicketModes);
    }

    [Theory]
    [InlineData(UiLanguage.English, "Consensus", "Source details")]
    [InlineData(UiLanguage.Macedonian, "Консензус", "Детали по извор")]
    public void OneStoredMatchHasOneTopLevelRowWithVisibleConsensusAndSourceDetails(
        UiLanguage language, string consensusLabel, string detailsLabel)
    {
        var match = new Match
        {
            HomeTeam = "Home & Team", AwayTeam = "<Away>", League = "<League>",
            PredictionSnapshots =
            [
                new() { Id = 1, Source = "Source A", PredictedResult = "2", PredictedScore = "stale",
                    CapturedAtUtc = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc) },
                new() { Id = 2, Source = "Source A", PredictedResult = "1", Btts = "YES", Goals = "O2.5",
                    CapturedAtUtc = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc) },
                new() { Id = 3, Source = "<Source B>", PredictedResult = "1", Btts = "YES", Goals = "O2.5" }
            ]
        };
        string html = DatabaseReportPages.Predictions(new DateOnly(2026, 11, 1), [match], 1, 1, language);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var table = document.DocumentNode.SelectSingleNode("//div[@class='scroll']/table");
        Assert.Equal(2, table.SelectNodes("./tr").Count);
        var row = table.SelectSingleNode("./tr[2]");
        Assert.Contains("1 (2/2)", row.SelectSingleNode("./td[2]").InnerText);
        Assert.Contains("YES (2/2)", row.SelectSingleNode("./td[4]").InnerText);
        Assert.Contains("O2.5 (2/2)", row.SelectSingleNode("./td[6]").InnerText);
        Assert.Equal(9, row.SelectNodes("./td").Count);
        Assert.Contains(consensusLabel, table.SelectSingleNode("./tr[1]").InnerText);
        Assert.Contains(detailsLabel, row.SelectSingleNode(".//details/summary").InnerText);
        Assert.Equal(2, row.SelectNodes(".//details/table/tr[td]").Count);
        Assert.Equal(1, html.Split("Home &amp; Team - &lt;Away&gt;").Length - 1);
        Assert.Contains("&lt;League&gt;", html);
        Assert.Contains("&lt;Source B&gt;", html);
        Assert.DoesNotContain("stale", html);
        Assert.DoesNotContain("<Source B>", html);
    }

    [Theory]
    [InlineData(UiLanguage.English, false, "n/a")]
    [InlineData(UiLanguage.Macedonian, false, "н/д")]
    [InlineData(UiLanguage.English, true, "n/a")]
    [InlineData(UiLanguage.Macedonian, true, "н/д")]
    public void TiedOrMissingVotesHaveNoConsensus(UiLanguage language, bool missing, string expected)
    {
        var match = new Match
        {
            HomeTeam = "Home", AwayTeam = "Away",
            PredictionSnapshots = missing ? [] :
            [
                new() { Source = "A", PredictedResult = "1", Btts = "YES", Goals = "O2.5" },
                new() { Source = "B", PredictedResult = "2", Btts = "NO", Goals = "U2.5" }
            ]
        };
        var document = new HtmlDocument();
        document.LoadHtml(DatabaseReportPages.Predictions(new DateOnly(2026, 11, 1), [match], 1, 1, language));
        var row = document.DocumentNode.SelectSingleNode("//div[@class='scroll']/table/tr[2]");
        foreach (int column in new[] { 2, 3, 4, 5, 6, 7 })
            Assert.Equal(expected, row.SelectSingleNode($"./td[{column}]").InnerText);
    }

    [Fact]
    public void PredictionsEncodeSourceAndFixtureWithoutRunningCollection()
    {
        var match = new Match
        {
            HomeTeam = "<script>alert(1)</script>",
            AwayTeam = "Away & Team",
            PredictionSnapshots =
            [
                new PredictionSnapshot { Source = "<img src=x>", PredictedResult = "1", Confidence = .7 }
            ]
        };
        string html = DatabaseReportPages.Predictions(new DateOnly(2026, 11, 1), [match], 1, 1);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img src=x&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("70.0", html);
        Assert.Contains("Pending", html);
    }

    [Fact]
    public void TicketShowsPendingReasonAndFrozenEvidence()
    {
        var ticket = new SimulatedTicket
        {
            Id = 75, Mode = "daily", Stake = 1,
            Selections =
            [
                new SimulatedTicketSelection
                {
                    MatchId = 42, Position = 1, Fixture = "Home - Away", Market = "RESULT",
                    Pick = "1", Odds = 2, Evidence = "<script>not trusted</script>"
                }
            ]
        };
        var results = new Dictionary<long, MatchResult>();
        string html = DatabaseReportPages.Ticket(TicketPerformanceAnalyzer.Settle(ticket, results), results);
        Assert.Contains("Ticket #75", html);
        Assert.Contains("missing final result", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("Historical accuracy:", html);
    }

    [Fact]
    public void PaginationPreservesModeAndDoesNotOfferInvalidNextPage()
    {
        string html = DatabaseReportPages.Pagination("/tickets?mode=1-3p3%2B&from=2026-11-01", 2, 51, 50);
        Assert.Contains("&amp;from=", html);
        Assert.Contains("page=1", html);
        Assert.DoesNotContain("Next", html);
    }

    [Fact]
    public void MacedonianLanguageIsAppliedAndRetainedInReportNavigation()
    {
        var language = DatabaseReportPages.ParseLanguage("MK");
        string html = DatabaseReportPages.Predictions(new DateOnly(2026, 11, 1), [], 1, 0, language);

        Assert.Contains("<html lang=\"mk\">", html);
        Assert.Contains("Почетна", html);
        Assert.Contains("Нема зачувани прогнози", html);
        Assert.Contains("name=\"lang\" value=\"mk\"", html);
        Assert.Contains("href=\"?lang=en\">EN</a><a lang=\"mk\" href=\"?lang=mk\">MK</a>", html);
    }
}
