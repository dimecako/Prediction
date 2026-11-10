using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class FtShadowTests
{
    private static readonly DateOnly Date = new(2030, 1, 10);
    private static readonly DateTime Now = new(2030, 1, 10, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PairedArmsFreezeIdenticalPricesAndPrematchSnapshotsWithoutChangingLiveSelector()
    {
        var match = Fixture();
        match.PredictionSnapshots.Add(new()
        {
            Source = "Source0", PredictedResult = "2", Confidence = .99,
            CapturedAtUtc = Now.AddMinutes(1)
        });
        var history = History(won: 60);
        var offer = Offer(match);
        var run = Capture([match], [offer], history);
        var fixture = Assert.Single(run.Fixtures);
        Assert.All(fixture.Snapshots, s => Assert.Equal("1", s.Result));
        Assert.Equal(3, fixture.Snapshots.Count);
        Assert.All(fixture.Offers, o =>
        {
            Assert.Equal(1.8, o.Odds);
            Assert.Equal(o.BaselineScore.Odds, o.StrictScore.Odds);
            Assert.True(o.BaselineQualified);
            Assert.False(o.StrictQualified);
        });
        Assert.Single(fixture.Offers, o => o.BaselinePosition.HasValue);
        Assert.DoesNotContain(fixture.Offers, o => o.StrictPosition.HasValue);
        var before = TicketSelector.Select(Fixture(), TicketQualityPolicy.Calibrate(history),
            _ => 1.8, true, true, true);
        Capture([Fixture()], [offer], history);
        var after = TicketSelector.Select(Fixture(), TicketQualityPolicy.Calibrate(history),
            _ => 1.8, true, true, true);
        Assert.Equal(before, after);
    }

    [Fact]
    public void EachArmSelectsAtMostFifteenDistinctFixturesFromSameOfferUniverse()
    {
        var matches = Enumerable.Range(1, 20).Select(i => Fixture(i)).ToList();
        var run = Capture(matches, matches.Select(Offer).ToList(), History());
        var baseline = run.Fixtures.Where(f => f.Offers.Any(o => o.BaselinePosition.HasValue)).ToList();
        var strict = run.Fixtures.Where(f => f.Offers.Any(o => o.StrictPosition.HasValue)).ToList();
        Assert.Equal(15, baseline.Count);
        Assert.Equal(15, strict.Count);
        Assert.Equal(baseline.Select(f => f.MatchId), strict.Select(f => f.MatchId));
        Assert.All(run.Fixtures, f =>
        {
            Assert.True(f.Offers.Count(o => o.BaselinePosition.HasValue) <= 1);
            Assert.True(f.Offers.Count(o => o.StrictPosition.HasValue) <= 1);
        });
        Assert.Equal(Enumerable.Range(1, 15),
            baseline.SelectMany(f => f.Offers).Where(o => o.BaselinePosition.HasValue)
                .Select(o => o.BaselinePosition!.Value).Order());
    }

    [Fact]
    public void DuplicateBookmakerFixtureIsCountedOnceAndKnownStartedUnmatchedAreExcluded()
    {
        var match = Fixture();
        var duplicate = Fixture(2);
        duplicate.HomeTeam = match.HomeTeam;
        duplicate.AwayTeam = match.AwayTeam;
        var started = Fixture(3);
        var known = Fixture(4);
        known.Result = new() { HomeGoals = 2, AwayGoals = 1 };
        var unmatched = Fixture(5);
        var run = Capture([match, duplicate, started, known, unmatched],
            [Offer(match), Offer(started) with { KickoffUtc = Now }, Offer(known)], History());
        Assert.Single(run.Fixtures);
        Assert.Equal(1, run.Duplicates);
        Assert.Equal(1, run.Started);
        Assert.Equal(1, run.KnownResults);
        Assert.Equal(1, run.Unmatched);
    }

    [Fact]
    public void MissingAlternativePriceIsFrozenAsRejectionAndNeverFilled()
    {
        var match = Fixture();
        var offer = Offer(match) with { Odds = new Dictionary<string, double> { ["1"] = 1.8 } };
        var run = Capture([match], [offer], History());
        Assert.All(run.Fixtures.Single().Offers, o =>
        {
            Assert.Null(o.Odds);
            Assert.False(o.BaselineQualified);
            Assert.False(o.StrictQualified);
            Assert.Null(o.BaselinePosition);
            Assert.Null(o.StrictPosition);
        });
        Assert.Single(run.Fixtures);
    }

    [Fact]
    public void ValidDrawIsAnExplicitFrozenAbstentionNotAnEmptyRunFailure()
    {
        var match = Fixture();
        foreach (var s in match.PredictionSnapshots) s.PredictedResult = "X";
        var run = Capture([match], [Offer(match) with
        {
            Odds = new Dictionary<string, double> { ["2"] = 3.2 }
        }], History());
        Assert.Empty(run.Fixtures.Single().Offers);
        Assert.Contains("No unambiguous", run.Fixtures.Single().Abstention);
    }

    [Fact]
    public void NoBackfillAndNoFutureOrLateHistoricalEvidence()
    {
        Assert.Throws<ArgumentException>(() =>
            FtShadowEvaluation.Capture([], [], Date.AddDays(-1), Now, 15, [], _ => { }));
        var history = History();
        var late = history[0];
        late.Result!.CollectedAtUtc = Now;
        var postmatch = history[1];
        foreach (var s in postmatch.PredictionSnapshots) s.CapturedAtUtc = Now;
        var sameDay = history[2];
        sameDay.MatchDate = Date;
        var run = Capture([Fixture()], [Offer(Fixture())], history);
        Assert.Equal(97, run.HistoricalMatches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void InvalidCountIsRejected(int count) =>
        Assert.Throws<ArgumentException>(() =>
            FtShadowEvaluation.Capture([], [], Date, Now, count, [], _ => { }));

    [Fact]
    public void MissingInvalidOrAmbiguousInputCannotBecomeSuccessfulEmptyEvaluation()
    {
        var match = Fixture();
        Assert.Throws<InvalidOperationException>(() => Capture([match], [], History()));
        Assert.Throws<InvalidOperationException>(() => Capture([match], [Offer(match), Offer(match)], History()));
        match.PredictionSnapshots.Clear();
        Assert.Throws<InvalidOperationException>(() => Capture([match], [Offer(match)], History()));
        match = Fixture();
        var offer = Offer(match) with
        {
            Odds = new Dictionary<string, double> { ["1"] = 1.8, ["50723"] = double.NaN }
        };
        Assert.Throws<InvalidOperationException>(() => Capture([match], [offer], History()));
    }

    [Fact]
    public void FrozenPayloadRoundTripsScoresVersionsAndDecisions()
    {
        var capture = Capture([Fixture()], [Offer(Fixture())], History());
        var run = Stored(capture);
        var loaded = FtShadowEvaluation.Read(run);
        Assert.Equal(JsonSerializer.Serialize(capture), JsonSerializer.Serialize(loaded));
        run.MatchDate = run.MatchDate.AddDays(1);
        Assert.Throws<InvalidDataException>(() => FtShadowEvaluation.Read(run));
        run = Stored(capture with { ChallengerVersion = "unknown" });
        Assert.Throws<InvalidDataException>(() => FtShadowEvaluation.Read(run));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void FrozenPayloadAcceptsPostgresMicrosecondPrecisionWithoutChangingJson(int subMicrosecondTicks)
    {
        var capture = Capture([Fixture()], [Offer(Fixture())], History()) with
        {
            CapturedAtUtc = Now.AddTicks(1234560 + subMicrosecondTicks)
        };
        var run = Stored(capture);
        run.CapturedAtUtc = Now.AddTicks(1234560);

        var loaded = FtShadowEvaluation.Read(run);

        Assert.Equal(capture.CapturedAtUtc, loaded.CapturedAtUtc);
        Assert.Equal(JsonSerializer.Serialize(capture), run.FrozenJson);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(10)]
    public void FrozenPayloadRejectsTimestampDifferencesAtDatabasePrecision(int changedTicks)
    {
        var capture = Capture([Fixture()], [Offer(Fixture())], History()) with
        {
            CapturedAtUtc = Now.AddTicks(1234567)
        };
        var run = Stored(capture);
        run.CapturedAtUtc = Now.AddTicks(1234560 + changedTicks);

        Assert.Throws<InvalidDataException>(() => FtShadowEvaluation.Read(run));
    }

    [Fact]
    public void CorruptFrozenPositionsQualificationPricesAndSnapshotsAreRejected()
    {
        var capture = Capture([Fixture()], [Offer(Fixture())], History());
        var selected = capture.Fixtures.Single().Offers.Single(o => o.BaselinePosition.HasValue);
        selected.BaselinePosition = 2;
        Assert.Throws<InvalidDataException>(() => FtShadowEvaluation.Read(Stored(capture)));
        selected.BaselinePosition = 1;
        var fixture = capture.Fixtures.Single();
        foreach (var corrupt in new[]
        {
            fixture with { Offers = [selected with { BaselineQualified = false }] },
            fixture with { Offers = [selected with { Odds = null }] },
            fixture with { Snapshots = [fixture.Snapshots[0] with { CapturedAtUtc = Now.AddMinutes(1) }] },
            fixture with { Offers = [selected with { Pick = "BTTS & 3+" }] }
        })
            Assert.Throws<InvalidDataException>(() =>
                FtShadowEvaluation.Read(Stored(capture with { Fixtures = [corrupt] })));
        Assert.Throws<InvalidDataException>(() =>
            FtShadowEvaluation.Read(Stored(capture with { Fixtures = null! })));
    }

    [Fact]
    public void FutureDateCaptureCannotUseResultsNotYetKnownAtCaptureTime()
    {
        var match = Fixture();
        match.MatchDate = Date.AddDays(1);
        var history = History();
        history[0].Result!.CollectedAtUtc = Now.AddHours(1);
        var run = FtShadowEvaluation.Capture([match],
            [Offer(match) with { KickoffUtc = Now.AddDays(1).AddHours(5) }],
            Date.AddDays(1), Now, 15, history, _ => { });
        Assert.Equal(99, run.HistoricalMatches);
        match.PredictionSnapshots.Add(new()
        {
            Source = "Invalid", Confidence = double.NaN, CapturedAtUtc = Now.AddMinutes(-1)
        });
        Assert.Throws<InvalidOperationException>(() => FtShadowEvaluation.Capture([match],
            [Offer(match) with { KickoffUtc = Now.AddDays(1).AddHours(5) }],
            Date.AddDays(1), Now, 15, history, _ => { }));
    }

    [Fact]
    public void ReportGradesExactEventsPendingAndSinglesAtFrozenPricesNotComponentsOrRepeats()
    {
        var run = Capture([Fixture(1), Fixture(2), Fixture(3)],
            [Offer(Fixture(1)), Offer(Fixture(2)), Offer(Fixture(3))], History());
        var results = new Dictionary<long, MatchResult>
        {
            [1] = new() { HomeGoals = 2, AwayGoals = 1 },
            [2] = new() { HomeGoals = 0, AwayGoals = 3 }
        };
        var comparison = FtShadowReport.Compare([run], results);
        var total = comparison.Single(c => c.Pick == "ALL");
        Assert.Equal(9, total.Baseline.Offered);
        Assert.Equal(3, total.Baseline.Selected);
        Assert.Equal(1, total.Baseline.Won);
        Assert.Equal(1, total.Baseline.Lost);
        Assert.Equal(1, total.Baseline.Pending);
        Assert.Equal(.5, total.Baseline.Accuracy);
        Assert.Equal(-.2m, total.Baseline.SinglesProfit);
        Assert.Equal(-.1m, total.Baseline.SinglesRoi);
        Assert.Equal(total.Baseline, total.Challenger);
        Assert.Equal(3, total.SharedSelected);
        Assert.Equal(0, total.BaselineOnlySelected);
        Assert.Throws<ArgumentException>(() => FtShadowReport.Compare([run, run], results));
        var logs = new List<string>();
        FtShadowReport.Write([run], results, logs.Add);
        Assert.Contains(logs, l => l.Contains("INSUFFICIENT EVIDENCE"));
        Assert.Contains(logs, l => l.Contains("No auto-promotion"));
        Assert.Contains(logs, l => l.Contains("NOT accumulator"));
    }

    [Fact]
    public void ZeroSelectionReportHasNoSuccessShapedAccuracyOrRoi()
    {
        var run = Capture([Fixture()], [Offer(Fixture())], []);
        var total = FtShadowReport.Compare([run], new Dictionary<long, MatchResult>()).First();
        Assert.Equal(0, total.Baseline.Selected);
        Assert.Null(total.Baseline.Accuracy);
        Assert.Null(total.Baseline.SinglesRoi);
        var logs = new List<string>();
        FtShadowReport.Write([], new Dictionary<long, MatchResult>(), logs.Add);
        Assert.Contains(logs, l => l.Contains("No frozen prospective runs"));
    }

    [Fact]
    public void PendingShadowFixturesTriggerOnlyAllowedRecentResultDates()
    {
        var run = Capture([Fixture()], [Offer(Fixture())], History());
        Assert.Equal([Date], DailyRunPlan.PendingShadowDates([run],
            new Dictionary<long, MatchResult>(), Date.AddDays(2)));
        Assert.Empty(DailyRunPlan.PendingShadowDates([run],
            new Dictionary<long, MatchResult>(), Date.AddDays(3)));
        Assert.Empty(DailyRunPlan.PendingShadowDates([run],
            new Dictionary<long, MatchResult> { [1] = new() }, Date.AddDays(2)));
    }

    [Fact]
    public void AdditiveSchemaHasUniqueExperimentDateAndMatchesGeneratedSnapshot()
    {
        using var db = new FootballDbContext(new DbContextOptionsBuilder<FootballDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var entity = db.Model.FindEntityType(typeof(FtShadowRun))!;
        Assert.Equal("ft_shadow_runs", entity.GetTableName());
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(["Experiment", "MatchDate"]));
    }

    [DatabaseFact]
    public async Task FrozenRunAndZeroSelectionRoundTripWithoutCreatingLiveTickets()
    {
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        int ticketsBefore = await db.SimulatedTickets.CountAsync();
        var capture = Capture([Fixture()], [Offer(Fixture())], []) with
        {
            CapturedAtUtc = Now.AddTicks(1234567)
        };
        var saved = await FtShadowEvaluation.SaveAsync(db, capture);
        var repeated = await FtShadowEvaluation.SaveAsync(db, capture with { RequestedCount = 1 });
        Assert.True(saved.Created);
        Assert.False(repeated.Created);
        Assert.Equal(saved.Run.Id, repeated.Run.Id);
        Assert.Equal(15, FtShadowEvaluation.Read(repeated.Run).RequestedCount);
        Assert.Equal(capture.CapturedAtUtc, FtShadowEvaluation.Read(repeated.Run).CapturedAtUtc);
        Assert.Equal(ticketsBefore, await db.SimulatedTickets.CountAsync());
        await transaction.RollbackAsync();
    }

    private static FtShadowCapture Capture(IReadOnlyList<Match> matches, IReadOnlyList<BookmakerMatch> offers,
        IReadOnlyList<Match> history) => FtShadowEvaluation.Capture(matches, offers, Date, Now, 15, history, _ => { });

    private static FtShadowRun Stored(FtShadowCapture capture) => new()
    {
        Experiment = capture.Experiment, MatchDate = capture.Date, CapturedAtUtc = capture.CapturedAtUtc,
        RequestedCount = capture.RequestedCount, FrozenJson = JsonSerializer.Serialize(capture)
    };

    private static Match Fixture(int id = 1) => new()
    {
        Id = id, MatchDate = Date, League = "ITALY 1",
        HomeTeam = new string((char)('a' + id % 26), 4) + new string((char)('a' + id / 26 % 26), 4),
        AwayTeam = new string((char)('z' - id % 26), 4) + new string((char)('z' - id / 26 % 26), 4),
        PredictionSnapshots = Enumerable.Range(0, 3).Select(i => new PredictionSnapshot
        {
            Id = id * 10 + i, Source = $"Source{i}", PredictedResult = "1", Goals = "O2.5",
            Confidence = .75, CapturedAtUtc = Now.AddHours(-1)
        }).ToList()
    };

    private static BookmakerMatch Offer(Match match) => new(match.HomeTeam, match.AwayTeam,
        Now.AddHours(5), new Dictionary<string, double>
        {
            ["1"] = 1.8, ["50723"] = 1.8, ["51784"] = 1.8, ["419"] = 1.8
        }, "ITALY 1");

    private static List<Match> History(int won = 80) => Enumerable.Range(1, 100).Select(i =>
    {
        var match = Fixture(1000 + i);
        match.MatchDate = Date.AddDays(-1);
        match.KickoffUtc = Now.AddDays(-1).AddHours(5);
        foreach (var s in match.PredictionSnapshots) s.CapturedAtUtc = Now.AddDays(-1);
        match.Result = new()
        {
            HomeGoals = i <= won ? 2 : 0, AwayGoals = i <= won ? 1 : 3,
            CollectedAtUtc = Now.AddDays(-1).AddHours(7)
        };
        return match;
    }).ToList();
}
