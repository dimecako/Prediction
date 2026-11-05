using Prediction.Entities;
using Prediction.Services;
using Prediction.Web;
using Xunit;

namespace Prediction.Tests;

public sealed class PredictionReportFixturesTests
{
    [Fact]
    public void ConsolidatesBenficaNameAliasesWithVerifiedProviderId()
    {
        var first = Fixture(1, "SL Benfica", "Lech Poznan");
        var second = Fixture(2, "Benfica", "Lech Poznan");
        first.PredictionSnapshots = [Snapshot(1, 1, "Forebet", "1", "fixture-1")];
        second.PredictionSnapshots = [Snapshot(2, 2, "Forebet", "1", "fixture-1")];

        var display = Assert.Single(PredictionReportFixtures.Consolidate([first, second]));

        Assert.Single(display.PredictionSnapshots);
    }

    [Fact]
    public void ConsolidatesExactNamesAndLatestSourcesWithoutChangingStoredRows()
    {
        var first = Fixture(1, "Atlético-Team", "Away");
        var second = Fixture(2, " atletico team ", "AWAY");
        var old = Snapshot(1, 1, "Forebet", "2");
        var latest = Snapshot(2, 2, " forebet ", "1");
        first.PredictionSnapshots = [old, Snapshot(3, 1, "Other", "1")];
        second.PredictionSnapshots = [latest];
        second.Result = new() { HomeGoals = 2, AwayGoals = 1 };

        var display = Assert.Single(PredictionReportFixtures.Consolidate([first, second]));

        Assert.Equal(2, display.PredictionSnapshots.Count);
        Assert.Contains(latest, display.PredictionSnapshots);
        Assert.DoesNotContain(old, display.PredictionSnapshots);
        Assert.Equal(2, display.Result!.HomeGoals);
        Assert.Null(first.Result);
        Assert.Equal(2, first.PredictionSnapshots.Count);
        Assert.Equal(2, latest.MatchId);
        string html = DatabaseReportPages.Predictions(first.MatchDate, [display], 1, 1);
        Assert.Equal(1, html.Split("Atl&#233;tico-Team - Away").Length - 1);
        Assert.Contains("1 (2/2)", html);
        Assert.Contains("Source details (2)", html);
        Assert.Contains("2-1", html);
    }

    [Fact]
    public void SharedProviderIdAllowsVerifiedClubDesignatorAliasAndCombinesPartialResult()
    {
        var first = Fixture(1, "Monagas SC", "Caracas");
        var second = Fixture(2, "Monagas SC", "Caracas FC");
        first.PredictionSnapshots = [Snapshot(1, 1, "Forebet", "1", "fixture-1")];
        second.PredictionSnapshots = [Snapshot(2, 2, "Forebet", "2", "fixture-1")];
        first.Result = new() { HomeGoals = 2, AwayGoals = 1, HalfTimeHomeGoals = 1 };
        second.Result = new() { HomeGoals = 2, AwayGoals = 1, HalfTimeAwayGoals = 0 };

        var display = Assert.Single(PredictionReportFixtures.Consolidate([first, second]));

        Assert.Equal(1, display.Result!.HalfTimeHomeGoals);
        Assert.Equal(0, display.Result.HalfTimeAwayGoals);
        Assert.Null(first.Result.HalfTimeAwayGoals);
        Assert.Equal("2", Assert.Single(display.PredictionSnapshots).PredictedResult);
    }

    [Theory]
    [InlineData("women", "Home Women", "Away Women")]
    [InlineData("youth", "Home U21", "Away U21")]
    [InlineData("reserve", "Home B", "Away B")]
    [InlineData("reserves", "Home Reserves", "Away Reserves")]
    [InlineData("reversed", "Away", "Home")]
    [InlineData("partial", "Home City", "Away")]
    [InlineData("club-alias-without-id", "Home FC", "Away")]
    [InlineData("non-latin", "Ж", "Away")]
    [InlineData("date", "Home", "Away")]
    [InlineData("id", "Home", "Away")]
    [InlineData("score", "Home", "Away")]
    [InlineData("half-time", "Home", "Away")]
    [InlineData("kickoff", "Home", "Away")]
    public void KeepsUnsafeFixturesSeparate(string conflict, string home, string away)
    {
        var first = Fixture(1, conflict == "non-latin" ? "Д" : "Home", "Away");
        var second = Fixture(2, home, away);
        first.PredictionSnapshots = [Snapshot(1, 1, "Forebet", "1", "fixture-1")];
        second.PredictionSnapshots = [Snapshot(2, 2, "Forebet", "1",
            conflict == "id" ? "fixture-2" : conflict == "club-alias-without-id" ? null : "fixture-1")];
        if (conflict == "date")
            second.MatchDate = first.MatchDate.AddDays(1);
        if (conflict is "score" or "half-time")
        {
            first.Result = new() { HomeGoals = 2, AwayGoals = 1, HalfTimeHomeGoals = 0 };
            second.Result = new()
            {
                HomeGoals = conflict == "score" ? 3 : 2, AwayGoals = 1, HalfTimeHomeGoals = 1
            };
        }
        if (conflict == "kickoff")
        {
            first.KickoffUtc = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            second.KickoffUtc = first.KickoffUtc.Value.AddHours(2);
        }

        Assert.Equal(2, PredictionReportFixtures.Consolidate([first, second]).Count);
    }

    [Fact]
    public void MissingEvidenceCannotBridgeConflictingProviderIds()
    {
        var rows = Enumerable.Range(1, 3).Select(i => Fixture(i, "Home", "Away")).ToList();
        rows[1].PredictionSnapshots = [Snapshot(1, 2, "Forebet", "1", "fixture-1")];
        rows[2].PredictionSnapshots = [Snapshot(2, 3, "FOREBET", "1", "fixture-2")];

        Assert.Equal(2, PredictionReportFixtures.Consolidate(rows).Count);
    }

    [Fact]
    public void ConflictingSourceVotesShowNoConsensusAndRetainBothSourceDetails()
    {
        var fixture = Fixture(1, "Home", "Away");
        fixture.PredictionSnapshots = [Snapshot(1, 1, "Forebet", "1"), Snapshot(2, 1, "Other", "2")];
        string html = DatabaseReportPages.Predictions(fixture.MatchDate, [fixture], 1, 1);

        Assert.Contains("<td>n/a</td>", html);
        Assert.DoesNotContain("1 (1/2)", html);
        Assert.Contains("<td>Forebet</td>", html);
        Assert.Contains("<td>Other</td>", html);
    }

    [Fact]
    public void LatestCaptureIdBreaksTimestampTiesAndAllMarketsUseOneVotePerSource()
    {
        var first = Fixture(1, "Home", "Away");
        var second = Fixture(2, "HOME", "AWAY");
        var old = Snapshot(1, 1, "Forebet", "2");
        var latest = Snapshot(2, 2, "FOREBET", "1");
        latest.CapturedAtUtc = old.CapturedAtUtc;
        latest.Btts = " yes ";
        latest.Goals = "o2.5";
        var other = Snapshot(3, 1, "Other", "1");
        other.Btts = "YES";
        other.Goals = "O2.5";
        first.PredictionSnapshots = [old, other];
        second.PredictionSnapshots = [latest];

        var display = Assert.Single(PredictionReportFixtures.Consolidate([first, second]));
        string html = DatabaseReportPages.Predictions(first.MatchDate, [display], 1, 1);

        Assert.Contains("1 (2/2)", html);
        Assert.Contains("YES (2/2)", html);
        Assert.Contains("O2.5 (2/2)", html);
        Assert.DoesNotContain(old, display.PredictionSnapshots);
    }

    [Fact]
    public void BlankNamesNeverIdentifyAFixture()
    {
        Assert.Equal(2, PredictionReportFixtures.Consolidate(
            [Fixture(1, "", "Away"), Fixture(2, "", "Away")]).Count);
    }

    [Fact]
    public void ConsolidatesBeforePaginationSoAliasesNeverSplitPagesOrInflateTotals()
    {
        var rows = Enumerable.Range(1, 101).SelectMany(i => new[]
        {
            Fixture(i * 2, $"Home {i:D3}", "Away"),
            Fixture(i * 2 + 1, $"HOME {i:D3}", "AWAY")
        }).ToList();
        var fixtures = PredictionReportFixtures.Consolidate(rows);
        var firstPage = fixtures.Take(100).ToList();
        var secondPage = fixtures.Skip(100).Take(100).ToList();

        Assert.Equal(101, fixtures.Count);
        Assert.Equal(100, firstPage.Count);
        Assert.Single(secondPage);
        string first = DatabaseReportPages.Predictions(rows[0].MatchDate, firstPage, 1, fixtures.Count);
        string second = DatabaseReportPages.Predictions(rows[0].MatchDate, secondPage, 2, fixtures.Count);
        Assert.Contains("101 stored fixtures", first);
        Assert.Contains("page=2", first);
        Assert.Contains("Home 101 - Away", second);
        Assert.DoesNotContain("Next", second);
    }

    private static Match Fixture(long id, string home, string away) => new()
    {
        Id = id, MatchDate = new DateOnly(2026, 11, 1), HomeTeam = home, AwayTeam = away
    };

    private static PredictionSnapshot Snapshot(long id, long matchId, string source, string pick,
        string? sourceId = null) => new()
    {
        Id = id, MatchId = matchId, Source = source, SourceMatchId = sourceId, PredictedResult = pick,
        CapturedAtUtc = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc).AddMinutes(id)
    };
}
