using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PREDICTION_RUN_DB_TESTS") != "1")
            Skip = "Opt in with PREDICTION_RUN_DB_TESTS=1 and a migrated ConnectionStrings__FootballDb.";
    }
}

public sealed class TicketLedgerDatabaseTests
{
    [DatabaseFact]
    public async Task FrozenSimulationRoundTripsDeduplicatesAndGradesFromStoredResults()
    {
        await using var db = new FootballDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        var match = new Match
        {
            ExternalMatchId = $"phase-nine-test-{Guid.NewGuid():N}",
            HomeTeam = "Phase Nine Test Home", AwayTeam = "Phase Nine Test Away",
            League = "Test League", MatchDate = DateOnly.FromDateTime(now.AddDays(1))
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();
        SimulatedTicket Snapshot(double accuracy) => TicketLedger.Create(match.MatchDate, false,
            new[]
            {
                new SimulatedTicketSelection
                {
                    Position = 1, MatchId = match.Id, Fixture = $"{match.HomeTeam} vs {match.AwayTeam}",
                    League = match.League, KickoffUtc = now.AddDays(1), Market = "1X2", Pick = "1",
                    CalibrationKey = "1X2|1|70+", Odds = 1.85m, BaseOdds = 1.85m,
                    Accuracy = accuracy, Safe = 0.6, Sample = 100
                }
            }, now);
        var saved = await TicketLedger.SaveAsync(db, Snapshot(0.75));
        var duplicate = await TicketLedger.SaveAsync(db, Snapshot(0.80));
        Assert.True(saved.Created);
        Assert.False(duplicate.Created);
        Assert.Equal(saved.Ticket.Id, duplicate.Ticket.Id);
        Assert.Equal(0.75, duplicate.Ticket.Selections.Single().Accuracy);
        Assert.Equal(1, await db.SimulatedTickets.CountAsync(x => x.Fingerprint == saved.Ticket.Fingerprint));
        db.MatchResults.Add(new()
        {
            MatchId = match.Id, HomeGoals = 2, AwayGoals = 0,
            Result = "1", TotalGoals = 2, Source = "PhaseNineTest"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await db.SimulatedTickets.AsNoTracking().Include(x => x.Selections)
            .SingleAsync(x => x.Id == saved.Ticket.Id);
        var results = await db.MatchResults.AsNoTracking()
            .Where(x => x.MatchId == match.Id).ToDictionaryAsync(x => x.MatchId);
        var settlement = TicketPerformanceAnalyzer.Settle(loaded, results);
        Assert.Equal(SimulatedTicketStatus.Won, settlement.Status);
        Assert.Equal(0.85m, settlement.Profit);
        Assert.Equal("Test League", loaded.Selections.Single().League);
        Assert.Equal(now.AddDays(1), loaded.Selections.Single().KickoffUtc);
        await transaction.RollbackAsync();
    }
}
