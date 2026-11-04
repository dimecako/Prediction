using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class TicketPerformanceTests
{
    private static readonly DateTime Created = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("ml-league", "ml")]
    [InlineData("ml-league-safe", "ml-safe")]
    [InlineData("daily-league-safe", "ml-league-safe")]
    public void LeagueModesCanBeValidatedAndSettledWithDistinctIdentity(string mode, string standardMode)
    {
        var selections = new[] { Pick(1) };
        selections[0].Position = 1;
        var ticket = TicketLedger.Create(DateOnly.FromDateTime(Created), false, selections, Created, mode);
        var standard = TicketLedger.Create(ticket.MatchDate, false, selections, Created, standardMode);

        TicketLedger.Validate(ticket);
        Assert.Equal(mode, ticket.Mode);
        Assert.NotEqual(standard.Fingerprint, ticket.Fingerprint);
        Assert.Equal(ticket.Fingerprint,
            TicketLedger.Create(ticket.MatchDate, false, selections, Created.AddMinutes(1), mode).Fingerprint);
        Assert.Equal(SimulatedTicketStatus.Won,
            TicketPerformanceAnalyzer.Settle(ticket, Results((1, 2, 0))).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public void UnknownSimulationModesStillFailExplicitly(string mode)
    {
        var selection = Pick(1);
        selection.Position = 1;
        Assert.Throws<ArgumentException>(() =>
            TicketLedger.Create(DateOnly.FromDateTime(Created), false, [selection], Created, mode));
    }

    [Fact]
    public void IdentityIgnoresEnumerationOrderAndCaptureTimeButIncludesModeAndPrices()
    {
        var first = Create(Pick(1, 1.85m), Pick(2, 1.90m));
        var repeat = TicketLedger.Create(first.MatchDate, false,
            first.Selections.Reverse(), Created.AddMinutes(1));
        Assert.Equal(first.Fingerprint, repeat.Fingerprint);
        Assert.NotEqual(first.Fingerprint, Create(Pick(1, 1.86m), Pick(2, 1.90m)).Fingerprint);
        Assert.NotEqual(first.Fingerprint,
            TicketLedger.Create(first.MatchDate, true, first.Selections, Created).Fingerprint);
        Assert.Equal(1m, first.Stake);
    }

    [Fact]
    public void RepeatedSignalsDoNotChangeIdentityOfTheSameSelections()
    {
        var pick = Pick(1, 1.85m);
        var first = Create(pick);
        pick.Accuracy = 0.80;
        pick.Safe = 0.70;
        pick.Sample = 150;
        Assert.Equal(first.Fingerprint, Create(pick).Fingerprint);
    }

    [Fact]
    public void EmptyDuplicateUnpricedAndStartedTicketsFailExplicitly()
    {
        Assert.Throws<ArgumentException>(() => Create());
        Assert.Throws<ArgumentException>(() => Create(Pick(1), Pick(1)));
        var unpriced = Pick(1, 1m);
        Assert.Throws<ArgumentException>(() => Create(unpriced));
        var started = Pick(1);
        started.KickoffUtc = Created;
        Assert.Throws<ArgumentException>(() => Create(started));
        started.KickoffUtc = DateTime.SpecifyKind(Created.AddHours(1), DateTimeKind.Unspecified);
        Assert.Throws<ArgumentException>(() => Create(started));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void InvalidAccuracyCannotBeSaved(double accuracy)
    {
        var pick = Pick(1);
        pick.Accuracy = accuracy;
        Assert.Throws<ArgumentException>(() => Create(pick));
    }

    [Fact]
    public void WinningAccumulatorUsesOneStakeAndExactFrozenDecimalOdds()
    {
        var ticket = Create(Pick(1, 1.85m), Pick(2, 1.90m));
        var row = TicketPerformanceAnalyzer.Settle(ticket, Results((1, 2, 0), (2, 1, 0)));
        Assert.Equal(SimulatedTicketStatus.Won, row.Status);
        Assert.Equal(3.515m, row.CombinedOdds);
        Assert.Equal(3.515m, row.Payout);
        Assert.Equal(2.515m, row.Profit);
        var totals = TicketPerformanceAnalyzer.Summarize(new[] { row });
        Assert.Equal(1m, totals.SettledStake);
        Assert.Equal(2.515m, totals.Roi);
    }

    [Fact]
    public void OneLossSettlesAccumulatorEvenWhenOtherResultsAreMissing()
    {
        var row = TicketPerformanceAnalyzer.Settle(Create(Pick(1), Pick(2)), Results((1, 0, 1)));
        Assert.Equal(SimulatedTicketStatus.Lost, row.Status);
        Assert.Equal(0m, row.Payout);
        Assert.Equal(-1m, row.Profit);
        Assert.Null(row.Selections[1].Correct);
    }

    [Fact]
    public void MissingResultsAndHalfTimeArePendingNotLosses()
    {
        var pick = Pick(1);
        pick.Market = "Combo";
        pick.Pick = "1-1";
        var row = TicketPerformanceAnalyzer.Settle(Create(pick), Results((1, 2, 0)));
        Assert.Equal(SimulatedTicketStatus.Pending, row.Status);
        Assert.Null(row.Payout);
        Assert.Null(row.Profit);
        Assert.Null(row.Selections.Single().Correct);
        var totals = TicketPerformanceAnalyzer.Summarize(new[] { row });
        Assert.Equal(0m, totals.SettledStake);
        Assert.Equal(1m, totals.PendingStake);
        Assert.Null(totals.Roi);
        Assert.Equal(SimulatedTicketStatus.Pending,
            TicketPerformanceAnalyzer.Settle(Create(Pick(1)), Results()).Status);
    }

    [Fact]
    public void FailedThreePlusLegSettlesTicketWithoutHalfTimeOrOtherLegResults()
    {
        var pick = Pick(1);
        pick.Market = "HalfGoals";
        pick.Pick = TicketMarkets.FirstHalf1To3And3PlusPick;
        var row = TicketPerformanceAnalyzer.Settle(Create(pick, Pick(2)), Results((1, 1, 1)));
        Assert.False(row.Selections[0].Correct);
        Assert.Null(row.Selections[1].Correct);
        Assert.Equal(SimulatedTicketStatus.Lost, row.Status);
        Assert.Equal(-1m, row.Profit);
        Assert.Equal(-1m, TicketPerformanceAnalyzer.Summarize(new[] { row }).Roi);
    }

    [Fact]
    public void HalfTimeBackfillAndCorrectedResultsAreReflectedWithoutChangingFrozenTicket()
    {
        var pick = Pick(1, 2.5m);
        pick.Market = "Combo";
        pick.Pick = "1-1";
        var ticket = Create(pick);
        var results = Results((1, 2, 0));
        Assert.Equal(SimulatedTicketStatus.Pending, TicketPerformanceAnalyzer.Settle(ticket, results).Status);
        results[1].HalfTimeHomeGoals = 1;
        results[1].HalfTimeAwayGoals = 0;
        Assert.Equal(2.5m, TicketPerformanceAnalyzer.Settle(ticket, results).Payout);
        results[1].HalfTimeHomeGoals = 0;
        Assert.Equal(SimulatedTicketStatus.Lost, TicketPerformanceAnalyzer.Settle(ticket, results).Status);
        Assert.Equal(2.5m, ticket.Selections.Single().Odds);
    }

    [Fact]
    public void RoiUsesOnlySettledStakeAndCanHandleDifferentStakes()
    {
        var won = Create(Pick(1, 2m));
        won.Stake = 2m;
        var lost = Create(Pick(2));
        var pending = Create(Pick(3));
        var results = Results((1, 2, 0), (2, 0, 1));
        var totals = TicketPerformanceAnalyzer.Summarize(new[] { won, lost, pending }
            .Select(x => TicketPerformanceAnalyzer.Settle(x, results)));
        Assert.Equal(3, totals.Total);
        Assert.Equal(1, totals.Won);
        Assert.Equal(1, totals.Lost);
        Assert.Equal(1, totals.Pending);
        Assert.Equal(3m, totals.SettledStake);
        Assert.Equal(1m, totals.PendingStake);
        Assert.Equal(4m, totals.Payout);
        Assert.Equal(1m, totals.Profit);
        Assert.Equal(1m / 3m, totals.Roi);
    }

    [Fact]
    public void NoSimulationsMeansNoRoiNotZeroPercentSuccess()
    {
        var totals = TicketPerformanceAnalyzer.Summarize([]);
        Assert.Equal(0, totals.Total);
        Assert.Null(totals.Roi);
    }

    [Fact]
    public void UnsupportedStoredMarketsDoNotBecomeSuccessfulSettlements()
    {
        var pick = Pick(1);
        pick.Market = "Unknown";
        Assert.Throws<ArgumentException>(() =>
            TicketPerformanceAnalyzer.Settle(Create(pick), Results((1, 2, 0))));
    }

    [Fact]
    public void PendingDiagnosticsDistinguishMissingFinalAndRequiredHalfTime()
    {
        var pick = Pick(1);
        pick.Market = "HalfGoals";
        pick.Pick = TicketMarkets.FirstHalf1To3And3PlusPick;
        var ticket = Create(pick);
        var missingFinal = TicketPerformanceAnalyzer.Settle(ticket, Results()).Selections.Single();
        Assert.Contains("missing final result", TicketPerformanceAnalyzer.PendingReason(missingFinal, null));
        var results = Results((1, 2, 1));
        var missingHalfTime = TicketPerformanceAnalyzer.Settle(ticket, results).Selections.Single();
        Assert.Equal("missing required half-time score",
            TicketPerformanceAnalyzer.PendingReason(missingHalfTime, results[1]));
        results[1].HalfTimeHomeGoals = 1;
        results[1].HalfTimeAwayGoals = 0;
        var graded = TicketPerformanceAnalyzer.Settle(ticket, results).Selections.Single();
        Assert.Null(TicketPerformanceAnalyzer.PendingReason(graded, results[1]));
    }

    private static SimulatedTicket Create(params SimulatedTicketSelection[] picks) =>
        TicketLedger.Create(DateOnly.FromDateTime(Created), false,
            picks.Select((p, i) => { p.Position = i + 1; return p; }), Created);

    private static SimulatedTicketSelection Pick(long matchId, decimal odds = 1.8m) => new()
    {
        MatchId = matchId, Fixture = $"Home {matchId} vs Away {matchId}",
        League = "Test league", KickoffUtc = Created.AddHours(4),
        Market = "1X2", Pick = "1", CalibrationKey = "1X2|1|70+",
        Odds = odds, BaseOdds = odds, Accuracy = 0.75, Safe = 0.65, Sample = 100
    };

    private static Dictionary<long, MatchResult> Results(params (long Id, int Home, int Away)[] rows) =>
        rows.ToDictionary(x => x.Id, x => new MatchResult
        {
            MatchId = x.Id, HomeGoals = x.Home, AwayGoals = x.Away,
            TotalGoals = x.Home + x.Away, Btts = x.Home > 0 && x.Away > 0,
            Result = x.Home > x.Away ? "1" : x.Home < x.Away ? "2" : "X"
        });
}
