using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class PredictionSnapshotWriterTests
{
    [DatabaseFact]
    public async Task ProductionCaptureStoresOnlyUniquelyMatchedAllowedLeaguePredictions()
    {
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        const string date = "2026-11-01";
        var allowed = Capture("Allowed Home", "Allowed Away");
        allowed.League = "UEFA Champions League";
        var outside = Capture("Outside Home", "Outside Away");
        outside.League = "SWEDEN 3";

        await new PredictionSnapshotWriter(db).SaveAllowedAsync(date, [allowed, outside]);

        var stored = await db.Matches.Include(x => x.PredictionSnapshots).ToListAsync();
        var match = Assert.Single(stored);
        Assert.Equal("UEFA CHAMPIONS LEAGUE", match.League);
        Assert.Equal("Allowed Home", match.HomeTeam);
        Assert.Single(match.PredictionSnapshots);
        await transaction.RollbackAsync();
    }

    [DatabaseFact]
    public async Task CaptureReusesVerifiedAliasesButAnyConflictPreventsAllCaptureWrites()
    {
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        string token = Guid.NewGuid().ToString("N");
        var first = Fixture(0, $"Test {token}");
        var second = Fixture(0, $"Test {token} City");
        string sourceId = $"forebet:{token}";
        foreach (var match in new[] { first, second })
        {
            match.ExternalMatchId = MatchIdentity.BuildExternalMatchId(
                "2026-11-01", match.HomeTeam, match.AwayTeam);
            match.PredictionSnapshots.Add(new()
            {
                Source = "Forebet", SourceMatchId = sourceId, PredictedResult = "X",
                CapturedAtUtc = DateTime.UtcNow
            });
        }
        db.Matches.AddRange(first, second);
        await db.SaveChangesAsync();
        var capture = new UnifiedMatch { HomeOrig = $"FC Test {token} City", AwayOrig = "Ipswich" };
        capture.Sources["Forebet"] = new() { SourceMatchId = sourceId, Tip = "1" };
        db.ChangeTracker.Clear();
        await new PredictionSnapshotWriter(db).SaveAsync("2026-11-01", [capture]);
        Assert.Equal(2, await db.Matches.CountAsync(x => x.HomeTeam.Contains(token)));

        var stored = await db.Matches.Include(x => x.Result)
            .Where(x => x.HomeTeam.Contains(token)).OrderBy(x => x.Id).ToListAsync();
        stored[0].Result = new() { HomeGoals = 3, AwayGoals = 1, Source = "Test" };
        stored[1].Result = new() { HomeGoals = 5, AwayGoals = 2, Source = "Test" };
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var independent = new UnifiedMatch { HomeOrig = $"Independent{token}", AwayOrig = "Other" };
        independent.Sources["Forebet"] = new() { SourceMatchId = $"forebet:other{token}", Tip = "X" };
        await Assert.ThrowsAsync<PredictionIdentityConflictException>(() =>
            new PredictionSnapshotWriter(db).SaveAsync("2026-11-01", [capture, independent]));
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Matches.CountAsync(x => x.HomeTeam.Contains(token)));
        Assert.Equal(new[] { 3, 5 }, await db.MatchResults
            .Where(x => x.Match.HomeTeam.Contains(token)).OrderBy(x => x.MatchId)
            .Select(x => x.HomeGoals).ToArrayAsync());
        await transaction.RollbackAsync();
    }

    [DatabaseFact]
    public async Task LaterSourceIdCollisionCannotSaveAnEarlierDisputedSnapshot()
    {
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        string token = Guid.NewGuid().ToString("N");
        var first = Fixture(0, $"First {token}");
        var second = Fixture(0, $"Second {token}");
        foreach (var match in new[] { first, second })
            match.ExternalMatchId = MatchIdentity.BuildExternalMatchId(
                "2026-11-01", match.HomeTeam, match.AwayTeam);
        db.Matches.AddRange(first, second);
        await db.SaveChangesAsync();
        var captures = new[] { first, second }.Select(match =>
        {
            var capture = new UnifiedMatch { HomeOrig = match.HomeTeam, AwayOrig = match.AwayTeam };
            capture.Sources["Forebet"] = new() { SourceMatchId = $"forebet:{token}", Tip = "1" };
            return capture;
        }).ToArray();
        foreach (var order in new[] { captures, captures.Reverse().ToArray() })
        {
            db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<PredictionIdentityConflictException>(() =>
                new PredictionSnapshotWriter(db).SaveAsync("2026-11-01", order));
            db.ChangeTracker.Clear();
            Assert.False(await db.PredictionSnapshots.AnyAsync(x => x.SourceMatchId == $"forebet:{token}"));
        }
        await transaction.RollbackAsync();
    }

    [Fact]
    public void VerifiedStoredAliasesCanBeReusedWithoutAnExactNameMatch()
    {
        var first = Fixture(10, "Hull");
        var second = Fixture(20, "Hull City");
        var selected = PredictionSnapshotWriter.ResolveStoredMatch(null, [second, first],
            [Link(10), Link(20)], [("Forebet", "forebet:123")]);
        Assert.Same(first, selected);
        Assert.Equal("Hull City", second.HomeTeam);
    }

    [Fact]
    public void VerifiedAliasesPreserveTheExactCanonicalFixture()
    {
        var first = Fixture(10, "Hull");
        var exact = Fixture(20, "Hull City");
        Assert.Same(exact, PredictionSnapshotWriter.ResolveStoredMatch(exact, [first],
            [Link(10), Link(20)], [("Forebet", "forebet:123")]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConflictingHistoricalScoresCannotBeBypassedBySharedSourceIds(bool hasExact)
    {
        var first = Fixture(10, "Hull");
        var second = Fixture(20, "Hull City");
        first.Result = new() { HomeGoals = 3, AwayGoals = 1 };
        second.Result = new() { HomeGoals = 5, AwayGoals = 2 };
        var error = Assert.Throws<PredictionIdentityConflictException>(() =>
            PredictionSnapshotWriter.ResolveStoredMatch(hasExact ? first : null, [first, second],
                [Link(10), Link(20)], [("Forebet", "forebet:123")]));
        Assert.Contains("10,20", error.Diagnostic);
        Assert.Equal(3, first.Result.HomeGoals);
        Assert.Equal(5, second.Result.HomeGoals);
    }

    [Fact]
    public void UnrelatedFixturesOrDifferentSourceIdsAreNotVerifiedAliases()
    {
        var first = Fixture(10, "Hull");
        var unrelated = Fixture(20, "Other");
        Assert.Throws<PredictionIdentityConflictException>(() =>
            PredictionSnapshotWriter.ResolveStoredMatch(null, [first, unrelated],
                [Link(10), Link(20)], [("Forebet", "forebet:123")]));
        var alias = Fixture(20, "Hull City");
        var different = Link(20);
        different.SourceMatchId = "forebet:456";
        Assert.Throws<PredictionIdentityConflictException>(() =>
            PredictionSnapshotWriter.ResolveStoredMatch(null, [first, alias],
                [Link(10), different], [("Forebet", "forebet:123")]));
    }

    private static Match Fixture(long id, string home) => new()
    {
        Id = id, HomeTeam = home, AwayTeam = "Ipswich",
        MatchDate = new(2026, 11, 1)
    };

    private static PredictionSnapshot Link(long id) => new()
    {
        MatchId = id, Source = "Forebet", SourceMatchId = "forebet:123"
    };

    private static UnifiedMatch Capture(string home, string away)
    {
        var match = new UnifiedMatch { HomeOrig = home, AwayOrig = away };
        match.Sources["Forebet"] = new() { SourceMatchId = $"forebet:{home}", Tip = "1" };
        return match;
    }
}
