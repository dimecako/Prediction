using Prediction.Infrastructure;
using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class LeagueNoSelectionTests
{
    [Theory]
    [InlineData("X")]
    [InlineData("1")]
    public void ValidPricedVotesWithoutActionableSignalAreEvaluationsNotTechnicalFailures(string pick)
    {
        var match = new Match
        {
            PredictionSnapshots = [new() { Source = "A", PredictedResult = pick }]
        };
        var decision = TicketSelector.Select(match, new(), _ => 1.8, true);
        Assert.Null(decision.Confirmed);
        Assert.True(TicketSelector.HasRequiredData(match, _ => 1.8));
        Assert.True(LeagueTicketSelector.IsNoSelection(true, 0, [],
            TicketSelector.HasRequiredData(match, _ => 1.8), evaluatedValidFixture: true));
        Assert.False(TicketSelector.HasRequiredData(match, _ => null));
        Assert.False(TicketSelector.HasRequiredData(match, _ => double.NaN));
        match.PredictionSnapshots.First().Confidence = double.PositiveInfinity;
        Assert.False(TicketSelector.HasRequiredData(match, _ => 1.8));
        match.PredictionSnapshots.Clear();
        Assert.False(TicketSelector.HasRequiredData(match, _ => 1.8));
    }

    [Theory]
    [InlineData("UEFA Champions League")]
    [InlineData("Imaginary Cup")]
    public async Task ValidLeagueFilterWithoutPicksSucceedsAndCompletesCheckpoint(string? league)
    {
        var step = new DailyRunStep("ai-rank safe league", ["ai-rank", "2026-11-01", "15", "safe", "league"]);
        var checkpoint = await DailyRunCheckpoint.LoadAsync(null, new(2026, 11, 1), 15, "test", _ => { });
        int status = await CommandExecution.RunAsync(() =>
        {
            if (!LeagueTicketSelector.IsNoSelection(true, 0, [league], hasRequiredData: true))
                CommandExecution.Fail();
            return Task.CompletedTask;
        }, automation: true);
        if (status == 0)
            await checkpoint.MarkCompletedAsync(step);

        Assert.Equal(0, status);
        Assert.True(checkpoint.IsCompleted(step));
    }

    [Theory]
    [InlineData(false, 0, true, "UEFA Champions League")]
    [InlineData(true, 1, true, "UEFA Champions League")]
    [InlineData(true, 0, false, "UEFA Champions League")]
    [InlineData(true, 0, true, "ENGLAND 1")]
    [InlineData(true, 0, true, null)]
    [InlineData(true, 0, true, "")]
    [InlineData(true, 0, true, "  ")]
    public void NoSelectionDoesNotHideOtherOutcomes(
        bool leagueMode, int selected, bool hasRequiredData, string? league)
    {
        Assert.False(LeagueTicketSelector.IsNoSelection(leagueMode, selected, [league], hasRequiredData));
    }

    [Fact]
    public void NoQualifiedCandidateIsNotEvidenceThatTheLeagueFilterCausedZeroPicks()
    {
        Assert.False(LeagueTicketSelector.IsNoSelection(true, 0, [], hasRequiredData: true));
    }

    [Fact]
    public void MissingMetadataMixedWithExcludedLeaguesStillFails()
    {
        Assert.False(LeagueTicketSelector.IsNoSelection(
            true, 0, ["UEFA Youth League", null], hasRequiredData: true));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void EmptyThresholdResultNeedsValidFixtureEvidenceAndCompleteData(
        bool hasRequiredData, bool evaluatedValidFixture, bool expected)
    {
        Assert.Equal(expected, LeagueTicketSelector.IsNoSelection(
            true, 0, [], hasRequiredData, evaluatedValidFixture));
        Assert.False(LeagueTicketSelector.IsNoSelection(
            false, 0, [], hasRequiredData, evaluatedValidFixture));
    }

    [Fact]
    public async Task MissingSourceOrBookmakerDataAndDatabaseExceptionsRemainFailures()
    {
        Assert.Equal(1, await CommandExecution.RunAsync(() =>
        {
            if (!LeagueTicketSelector.IsNoSelection(true, 0, ["UEFA Youth League"], hasRequiredData: false))
                CommandExecution.Fail();
            return Task.CompletedTask;
        }, automation: true));
        Assert.Equal(1, await CommandExecution.RunAsync(
            () => throw new InvalidOperationException("database failure"), automation: true));
    }
}
