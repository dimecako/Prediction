using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class DuplicateMatchMergerTests
{
    private static readonly DateOnly Date = new(2026, 11, 3);

    [Fact]
    public void SharedSourceIdMergesIntoRowWithResult()
    {
        var first = Fixture(1, "Monagas SC", "Caracas", ("SoccerVista", "soccervista:1"));
        var second = Fixture(2, "Monagas SC", "Caracas FC", ("SoccerVista", "soccervista:1"), ("Forebet", "forebet:9"));
        second.Result = Score(2, 1);

        var plan = DuplicateMatchMerger.Plan([first, second], new HashSet<long>());

        var merge = Assert.Single(plan.Merges);
        Assert.Same(second, merge.Canonical);
        Assert.Same(first, Assert.Single(merge.Duplicates));
        Assert.Equal("soccervista:1", merge.SourceMatchId);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void TicketedRowIsKeptAndTwoTicketedRowsAreSkipped()
    {
        var first = Fixture(1, "Home", "Away", ("Forebet", "forebet:1"));
        var second = Fixture(2, "Home FC", "Away", ("Forebet", "forebet:1"));
        second.Result = Score(1, 0);

        Assert.Same(first, Assert.Single(
            DuplicateMatchMerger.Plan([first, second], new HashSet<long> { 1 }).Merges).Canonical);
        var plan = DuplicateMatchMerger.Plan([first, second], new HashSet<long> { 1, 2 });
        Assert.Empty(plan.Merges);
        Assert.Contains("tickets", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void ContradictoryRowsAreSkipped()
    {
        var differentIds = DuplicateMatchMerger.Plan([
            Fixture(1, "Home", "Away", ("SoccerVista", "soccervista:1"), ("Forebet", "forebet:1")),
            Fixture(2, "Home", "Away", ("SoccerVista", "soccervista:1"), ("Forebet", "forebet:2"))],
            new HashSet<long>());
        var scoreA = Fixture(1, "Home", "Away", ("Forebet", "forebet:1"));
        var scoreB = Fixture(2, "Home", "Away FC", ("Forebet", "forebet:1"));
        scoreA.Result = Score(1, 0);
        scoreB.Result = Score(0, 1);
        var differentScores = DuplicateMatchMerger.Plan([scoreA, scoreB], new HashSet<long>());
        var womenTeam = DuplicateMatchMerger.Plan([
            Fixture(1, "Arsenal", "Chelsea", ("Forebet", "forebet:1")),
            Fixture(2, "Arsenal W", "Chelsea W", ("Forebet", "forebet:1"))], new HashSet<long>());

        foreach (var plan in new[] { differentIds, differentScores, womenTeam })
        {
            Assert.Empty(plan.Merges);
            Assert.Single(plan.Skipped);
        }
    }

    [Fact]
    public void NameOnlyAndCrossDateRowsAreIgnored()
    {
        var nameOnly = Fixture(1, "Uganda Police", "Kitara", ("SoccerVista", "soccervista:1"));
        var otherId = Fixture(2, "Police FC (UGA)", "Kitara FC", ("Forebet", "forebet:1"));
        var nextDay = Fixture(3, "Uganda Police", "Kitara", ("SoccerVista", "soccervista:1"));
        nextDay.MatchDate = Date.AddDays(1);

        var plan = DuplicateMatchMerger.Plan([nameOnly, otherId, nextDay], new HashSet<long>());

        Assert.Empty(plan.Merges);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void ConflictingKickoffsAndPartialHalfTimeEvidencePreventMerge()
    {
        var first = Fixture(1, "Home", "Away", ("Forebet", "forebet:1"));
        var second = Fixture(2, "Home FC", "Away", ("Forebet", "forebet:1"));
        first.KickoffUtc = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        second.KickoffUtc = first.KickoffUtc.Value.AddHours(2);
        Assert.Empty(DuplicateMatchMerger.Plan([first, second], new HashSet<long>()).Merges);
        second.KickoffUtc = first.KickoffUtc;
        first.Result = Score(2, 1);
        second.Result = Score(2, 1);
        first.Result.HalfTimeHomeGoals = 0;
        second.Result.HalfTimeHomeGoals = 1;
        Assert.Empty(DuplicateMatchMerger.Plan([first, second], new HashSet<long>()).Merges);
    }

    [DatabaseFact]
    public async Task ApplyPreservesComplementaryPartialHalfTimeScores()
    {
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        string token = Guid.NewGuid().ToString("N");
        var kept = Fixture(0, $"Merge {token}", "Away", ($"Test{token}", $"test:{token}"));
        var duplicate = Fixture(0, $"Merge {token} FC", "Away", ($"Test{token}", $"test:{token}"));
        kept.Result = Score(3, 1);
        kept.Result.HalfTimeAwayGoals = 0;
        duplicate.Result = Score(3, 1);
        duplicate.Result.HalfTimeHomeGoals = 1;
        foreach (var match in new[] { kept, duplicate })
            match.ExternalMatchId = MatchIdentity.BuildExternalMatchId("2026-11-03", match.HomeTeam, match.AwayTeam);
        db.Matches.AddRange(kept, duplicate);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var plan = DuplicateMatchMerger.Plan(await db.Matches.AsNoTracking()
            .Include(x => x.PredictionSnapshots).Include(x => x.Result)
            .Where(x => x.HomeTeam.Contains(token)).ToListAsync(), new HashSet<long> { kept.Id });
        await DuplicateMatchMerger.ApplyAsync(db, plan.Merges);
        var stored = await db.MatchResults.SingleAsync(x => x.MatchId == kept.Id);
        Assert.Equal(1, stored.HalfTimeHomeGoals);
        Assert.Equal(0, stored.HalfTimeAwayGoals);
    }

    [DatabaseFact]
    public async Task ApplyRejectsEvidenceChangedSincePlanning()
    {
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        string token = Guid.NewGuid().ToString("N");
        var first = Fixture(0, $"Merge {token}", "Away", ($"Test{token}", $"test:{token}"));
        var second = Fixture(0, $"Merge {token} FC", "Away", ($"Test{token}", $"test:{token}"));
        foreach (var match in new[] { first, second })
            match.ExternalMatchId = MatchIdentity.BuildExternalMatchId("2026-11-03", match.HomeTeam, match.AwayTeam);
        db.Matches.AddRange(first, second);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var plan = DuplicateMatchMerger.Plan(await db.Matches.AsNoTracking()
            .Include(x => x.PredictionSnapshots).Include(x => x.Result)
            .Where(x => x.HomeTeam.Contains(token)).ToListAsync(), new HashSet<long>());
        Assert.Single(plan.Merges);
        await db.PredictionSnapshots.Where(x => x.MatchId == second.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.SourceMatchId, $"changed:{token}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DuplicateMatchMerger.ApplyAsync(db, plan.Merges));
        Assert.Equal(2, await db.Matches.CountAsync(x => x.HomeTeam.Contains(token)));
    }

    [DatabaseFact]
    public async Task ApplyMovesSnapshotsAndResultAndDeletesDuplicate()
    {
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        string token = Guid.NewGuid().ToString("N");
        var kept = Fixture(0, $"Merge {token}", "Away", ($"Test{token}", $"test:{token}"));
        var duplicate = Fixture(0, $"Merge {token} FC", "Away", ($"Test{token}", $"test:{token}"));
        duplicate.League = "Test League";
        duplicate.Result = Score(3, 1);
        duplicate.Result.HalfTimeHomeGoals = 1;
        duplicate.Result.HalfTimeAwayGoals = 0;
        foreach (var match in new[] { kept, duplicate })
            match.ExternalMatchId = MatchIdentity.BuildExternalMatchId("2026-11-03", match.HomeTeam, match.AwayTeam);
        db.Matches.AddRange(kept, duplicate);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var plan = DuplicateMatchMerger.Plan(await db.Matches.AsNoTracking()
            .Include(x => x.PredictionSnapshots).Include(x => x.Result)
            .Where(x => x.HomeTeam.Contains(token)).ToListAsync(), new HashSet<long> { kept.Id });
        await DuplicateMatchMerger.ApplyAsync(db, plan.Merges);

        var stored = Assert.Single(await db.Matches.Include(x => x.PredictionSnapshots).Include(x => x.Result)
            .Where(x => x.HomeTeam.Contains(token)).ToListAsync());
        Assert.Equal(kept.Id, stored.Id);
        Assert.Equal(2, stored.PredictionSnapshots.Count);
        Assert.Equal((3, 1, 1), (stored.Result!.HomeGoals, stored.Result.AwayGoals, stored.Result.HalfTimeHomeGoals));
        Assert.Equal("Test League", stored.League);
    }

    private static Match Fixture(long id, string home, string away,
        params (string Source, string SourceMatchId)[] links)
    {
        var match = new Match
        {
            Id = id, MatchDate = Date, HomeTeam = home, AwayTeam = away,
            ExternalMatchId = MatchIdentity.BuildExternalMatchId(Date.ToString("yyyy-MM-dd"), home, away)
        };
        foreach (var link in links)
            match.PredictionSnapshots.Add(new()
            {
                MatchId = id, Source = link.Source, SourceMatchId = link.SourceMatchId,
                PredictedResult = "1", CapturedAtUtc = DateTime.UtcNow
            });
        return match;
    }

    private static MatchResult Score(int home, int away) => new()
    {
        HomeGoals = home, AwayGoals = away, TotalGoals = home + away, Btts = home > 0 && away > 0,
        Result = home > away ? "1" : home == away ? "X" : "2", Source = "Test"
    };
}
