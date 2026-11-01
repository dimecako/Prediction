using Prediction.Entities;
using Prediction.Services;
using Prediction.Web;
using Xunit;

namespace Prediction.Tests;

public sealed class DatabaseReportPagesTests
{
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
}
