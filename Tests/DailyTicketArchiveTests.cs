using ClosedXML.Excel;
using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class DailyTicketArchiveTests
{
    private static readonly DateOnly Date = new(2030, 1, 2);
    private static readonly DateTime Created = new(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc);
    private static readonly Match Fixture = new()
    {
        Id = 42, MatchDate = Date, HomeTeam = "Home", AwayTeam = "Away"
    };

    private static XLWorkbook Workbook(string market = "1X2", string display = "fix 1")
    {
        var workbook = new XLWorkbook();
        var info = workbook.AddWorksheet("Info");
        info.Cell(1, 1).Value = "daily-ticket";
        info.Cell(2, 2).Value = "2030-01-02";
        info.Cell(3, 2).Value = Created.ToString("O");
        info.Cell(4, 2).Value = "1/15";
        var sheet = workbook.AddWorksheet("Tiket");
        sheet.Cell(1, 1).Value = "#";
        sheet.Cell(2, 1).Value = 1;
        sheet.Cell(2, 2).Value = "Home vs Away";
        sheet.Cell(2, 3).Value = "Test League";
        sheet.Cell(2, 4).Value = market;
        sheet.Cell(2, 5).Value = display;
        sheet.Cell(2, 6).Value = 1.85;
        sheet.Cell(2, 9).Value = 1.75;
        sheet.Cell(2, 10).Value = "Source Prob.=70% (not selection probability)";
        sheet.Cell(2, 11).Value = Created.AddDays(1).ToString("O");
        return workbook;
    }

    [Theory]
    [InlineData("1X2", "fix 1", "1", true)]
    [InlineData("1X2", "fix 2", "2", false)]
    [InlineData("BTTS", "BTTS", "YES", true)]
    [InlineData("Goals", "3+", "O2.5", true)]
    [InlineData("Combo", "1 & T1 2+", "1 & T1 2+", true)]
    [InlineData("Combo", "1 & 1+ prvo & 1+ vtoro", "1 & 1+ prvo & 1+ vtoro", true)]
    [InlineData("Combo", "1-1", "1-1", true)]
    [InlineData("HalfGoals", "1-3 prvo & 3+", "1-3 prvo & 3+", true)]
    public void LegacyDailyWorkbookFreezesAndGradesWithoutInventedCalibration(
        string market, string display, string pick, bool correct)
    {
        using var workbook = Workbook(market, display);
        var ticket = DailyTicketArchive.Read(workbook, Date, [Fixture]);
        var selection = Assert.Single(ticket.Selections);
        Assert.Equal("daily", ticket.Mode);
        Assert.Equal(Created, ticket.CreatedAtUtc);
        Assert.Equal(pick, selection.Pick);
        Assert.Equal(1.85m, selection.Odds);
        Assert.Equal(0, selection.Sample);
        Assert.Equal(0, selection.Accuracy);
        Assert.Equal(0, selection.Safe);
        var result = new MatchResult
        {
            HomeGoals = 2, AwayGoals = 1, HalfTimeHomeGoals = 1, HalfTimeAwayGoals = 0
        };
        var settlement = TicketPerformanceAnalyzer.Settle(ticket,
            new Dictionary<long, MatchResult> { [Fixture.Id] = result });
        Assert.Equal(correct, Assert.Single(settlement.Selections).Correct);
        Assert.Equal(correct ? SimulatedTicketStatus.Won : SimulatedTicketStatus.Lost, settlement.Status);
        Assert.Equal(SimulatedTicketStatus.Pending,
            TicketPerformanceAnalyzer.Settle(ticket, new Dictionary<long, MatchResult>()).Status);
        Assert.Equal(ticket.Fingerprint, DailyTicketArchive.Read(workbook, Date, [Fixture]).Fingerprint);
        workbook.Worksheet("Info").Cell(1, 1).Value = "daily-ticket-league";
        var league = DailyTicketArchive.Read(workbook, Date, [Fixture]);
        Assert.Equal("daily-league", league.Mode);
        Assert.NotEqual(ticket.Fingerprint, league.Fingerprint);
    }

    [Fact]
    public void InvalidMissingAmbiguousAndStartedArchivesFailExplicitly()
    {
        using var workbook = Workbook();
        Assert.Throws<InvalidDataException>(() => DailyTicketArchive.Read(workbook, Date.AddDays(1), [Fixture]));
        Assert.Throws<InvalidDataException>(() => DailyTicketArchive.Read(workbook, Date, []));
        var duplicate = new Match { Id = 43, MatchDate = Date, HomeTeam = "Home", AwayTeam = "Away" };
        Assert.Throws<InvalidDataException>(() => DailyTicketArchive.Read(workbook, Date, [Fixture, duplicate]));
        workbook.Worksheet("Tiket").Cell(2, 12).Value = 42;
        Assert.Equal(42, DailyTicketArchive.Read(workbook, Date, [Fixture, duplicate]).Selections.Single().MatchId);
        workbook.Worksheet("Tiket").Cell(2, 11).Value = Created.ToString("O");
        Assert.Throws<ArgumentException>(() => DailyTicketArchive.Read(workbook, Date, [Fixture]));
    }

    [Fact]
    public void UnknownMarketsAndInconsistentCountsAreRejected()
    {
        using var workbook = Workbook("Unknown", "pick");
        Assert.Throws<InvalidDataException>(() => DailyTicketArchive.Read(workbook, Date, [Fixture]));
        workbook.Worksheet("Tiket").Cell(2, 4).Value = "1X2";
        workbook.Worksheet("Tiket").Cell(2, 5).Value = "fix 1";
        workbook.Worksheet("Info").Cell(4, 2).Value = "2/15";
        Assert.Throws<InvalidDataException>(() => DailyTicketArchive.Read(workbook, Date, [Fixture]));
    }

    [Fact]
    public void DailyModeRejectsCalibrationNumbersAndInvalidPrices()
    {
        using var workbook = Workbook();
        var ticket = DailyTicketArchive.Read(workbook, Date, [Fixture]);
        var pick = ticket.Selections.Single();
        pick.Sample = 100;
        Assert.Throws<ArgumentException>(() => TicketLedger.Validate(ticket));
        pick.Sample = 0;
        pick.Accuracy = 0.7;
        Assert.Throws<ArgumentException>(() => TicketLedger.Validate(ticket));
        pick.Accuracy = 0;
        pick.Odds = 1;
        Assert.Throws<ArgumentException>(() => TicketLedger.Validate(ticket));
    }
}
