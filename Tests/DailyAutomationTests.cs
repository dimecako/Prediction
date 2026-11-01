using Prediction.Entities;
using Prediction.Infrastructure;
using Prediction.Reporting;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

[CollectionDefinition("Automation process settings", DisableParallelization = true)]
public sealed class AutomationProcessSettingsCollection;

[Collection("Automation process settings")]
public sealed class DailyAutomationTests
{
    private static readonly DateOnly Target = new(2026, 11, 1);

    [Fact]
    public void EmptySourceIsNamedAsFailureInsteadOfSuccessfulProgress()
    {
        var messages = new List<string>();
        Assert.False(DailyRunPlan.CheckSourceCoverage([("Forebet", 3), ("ZuluBet", 0)], messages.Add));
        Assert.Contains(messages, message => message.Contains("FAILED source ZuluBet"));
        Assert.True(DailyRunPlan.CheckSourceCoverage([("Forebet", 3)], messages.Add));
    }

    [Fact]
    public void PlanRechecksAllOldDatesAndContainsExactlyNineVariantsWithoutImportOrTraining()
    {
        var old = Target.AddYears(-2);
        var steps = DailyRunPlan.Build(Target, 15, [Target, Target.AddDays(-1), old, old]);
        Assert.Equal(["collect-results", "ticket-report", "collect-results", "ticket-report"],
            steps.Take(4).Select(s => s.Args[0]));
        Assert.Equal(old.ToString("yyyy-MM-dd"), steps[0].Args[1]);
        Assert.Equal([Target.ToString("yyyy-MM-dd")], steps[4].Args);
        Assert.Equal(new[]
        {
            "ticket 2026-11-01 15", "ticket 2026-11-01 15 safe",
            "ai-rank 2026-11-01 15", "ai-rank 2026-11-01 15 safe",
            "ai-rank 2026-11-01 15 safe league", "daily-ticket 2026-11-01 15",
            "daily-ticket-league 2026-11-01 15", "ticket-1-3p3+ 2026-11-01 15",
            "ticket-1-3p3+ 2026-11-01 15 safe"
        }, steps.Skip(5).Take(9).Select(s => string.Join(" ", s.Args)));
        Assert.Equal("ticket-report", steps[^1].Args[0]);
        Assert.DoesNotContain(steps, s => s.Args[0] is "ticket-import" or "train" or "build");
    }

    [Fact]
    public void PendingLegsInLostAccumulatorsAreStillRecheckedAndConfirmedDatesAreExcluded()
    {
        var old = Target.AddYears(-2);
        var lost = Ticket(old, Pick(1), Pick(2));
        var results = new Dictionary<long, MatchResult>
        {
            [1] = Result(1, 0, 1),
            [3] = Result(3, 2, 0)
        };
        Assert.Equal(SimulatedTicketStatus.Lost, TicketPerformanceAnalyzer.Settle(lost, results).Status);
        Assert.Equal([old], DailyRunPlan.PendingDates(
            [lost, Ticket(old, Pick(2)), Ticket(Target.AddDays(-1), Pick(3)), Ticket(Target, Pick(4))],
            results, Target));
    }

    [Fact]
    public void MissingRequiredHalfTimeStaysPendingWithoutRecheckingFinalOnlyConfirmedLegs()
    {
        var half = Pick(1);
        half.Market = "HalfGoals";
        half.Pick = TicketMarkets.FirstHalf1To3And3PlusPick;
        var result = Result(1, 2, 1);
        var results = new Dictionary<long, MatchResult> { [1] = result };
        Assert.Equal([Target.AddDays(-1)], DailyRunPlan.PendingDates(
            [Ticket(Target.AddDays(-1), half)], results, Target));
        Assert.Empty(DailyRunPlan.PendingDates(
            [Ticket(Target.AddDays(-1), Pick(1))], results, Target));
        results[1] = Result(1, 1, 0);
        Assert.Empty(DailyRunPlan.PendingDates(
            [Ticket(Target.AddDays(-1), half)], results, Target));
    }

    [Theory]
    [InlineData("2026-07-15T22:30:00+00:00", "2026-07-16")]
    [InlineData("2026-11-01T22:30:00+00:00", "2026-11-01")]
    public void DefaultDateUsesSkopjeNotHostOrEveningTomorrowRule(string instant, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), DailyRunPlan.Today(DateTimeOffset.Parse(instant)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void ExportDefaultsRemainLocalAndCloudCanDisable(string? value, bool expected)
    {
        Assert.Equal(expected, ExportSettings.Parse(value));
    }

    [Fact]
    public void InvalidExportConfigurationFailsClosed()
    {
        Assert.Throws<InvalidOperationException>(() => ExportSettings.Parse("disabled"));
    }

    [Fact]
    public void PredictionReportingHonorsDisabledExportWithoutCreatingWorkbook()
    {
        string? previous = Environment.GetEnvironmentVariable("Exports__Excel");
        string path = Path.GetFullPath("predictions-2199-01-02.xlsx");
        Assert.False(File.Exists(path));
        try
        {
            Environment.SetEnvironmentVariable("Exports__Excel", "false");
            new ProductionConsensusAggregator("2199-01-02").PrintGuaranteedReport();
            Assert.False(File.Exists(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Exports__Excel", previous);
        }
    }

    [DatabaseFact]
    public async Task DatabaseLockRejectsOverlapAndReleasesWithoutSchemaOrDataWrites()
    {
        string connection = Environment.GetEnvironmentVariable("ConnectionStrings__FootballDb")!;
        var first = await DailyRunLock.TryAcquireAsync(connection);
        Assert.NotNull(first);
        try
        {
            await first.CheckAsync();
            Assert.Null(await DailyRunLock.TryAcquireAsync(connection));
        }
        finally
        {
            await first.DisposeAsync();
        }
        await using var reacquired = await DailyRunLock.TryAcquireAsync(connection);
        Assert.NotNull(reacquired);
        await reacquired.CheckAsync();
    }

    [Fact]
    public async Task NestedFailureDoesNotContaminateFollowingStepsOrProcess()
    {
        int processExit = Environment.ExitCode;
        int status = await CommandExecution.RunAsync(async () =>
        {
            Assert.Equal(1, await CommandExecution.RunAsync(() =>
            {
                CommandExecution.Fail();
                return Task.CompletedTask;
            }, automation: true));
            Assert.Equal(0, CommandExecution.ExitCode);
            Assert.False(CommandExecution.IsAutomation);
            Assert.Equal(0, await CommandExecution.RunAsync(() => Task.CompletedTask));
        });
        Assert.Equal(0, status);
        Assert.Equal(processExit, Environment.ExitCode);
    }

    [Fact]
    public async Task ExceptionsBecomeFailureAndAutomationScopeIsInheritedAndRestored()
    {
        Assert.Equal(1, await CommandExecution.RunAsync(
            () => throw new InvalidOperationException("secret-not-for-logs")));
        Assert.Equal(0, await CommandExecution.RunAsync(async () =>
        {
            Assert.True(CommandExecution.IsAutomation);
            await CommandExecution.RunAsync(() =>
            {
                Assert.True(CommandExecution.IsAutomation);
                return Task.CompletedTask;
            });
        }, automation: true));
        Assert.False(CommandExecution.IsAutomation);
    }

    [Fact]
    public async Task ParallelInvocationsHaveIndependentExitStatus()
    {
        var results = await Task.WhenAll(
            CommandExecution.RunAsync(async () =>
            {
                await Task.Yield();
                CommandExecution.Fail();
            }),
            CommandExecution.RunAsync(async () =>
            {
                await Task.Yield();
                Assert.Equal(0, CommandExecution.ExitCode);
            }));
        Assert.Equal([1, 0], results);
    }

    [Fact]
    public async Task InvalidDailyRunArgumentsFailWithoutDatabaseAccess()
    {
        Assert.Equal(1, await Program.Main(["daily-run", "bad-date"]));
        Assert.Equal(1, await Program.Main(["daily-run", "2026-11-01", "0"]));
        Assert.Equal(1, await Program.Main(["daily-run", "2026-11-01", "101"]));
        Assert.Equal(1, await Program.Main(["ticket", "bad-date"]));
        Assert.Equal(1, await Program.Main(["daily-ticket", "bad-date"]));
        Assert.Equal(1, await Program.Main(["ticket-report", "bad-date"]));
        Assert.Equal(0, CommandExecution.ExitCode);
    }

    private static SimulatedTicket Ticket(DateOnly date, params SimulatedTicketSelection[] picks) =>
        new() { MatchDate = date, Stake = 1, Selections = picks };

    private static SimulatedTicketSelection Pick(long id) =>
        new() { MatchId = id, Market = "1X2", Pick = "1", Odds = 1.85m };

    private static MatchResult Result(long id, int home, int away) =>
        new() { MatchId = id, HomeGoals = home, AwayGoals = away,
            TotalGoals = home + away, Result = home > away ? "1" : home < away ? "2" : "X" };
}
