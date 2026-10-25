using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prediction.Data;
using Prediction.Entities;

namespace Prediction.Services;

public static class TicketLedger
{
    public static SimulatedTicket Create(
        DateOnly date, bool safe, IEnumerable<SimulatedTicketSelection> selections, DateTime createdAtUtc,
        string? mode = null)
    {
        var ticket = new SimulatedTicket
        {
            MatchDate = date,
            CreatedAtUtc = createdAtUtc,
            Mode = mode ?? (safe ? "safe" : "value"),
            Stake = 1m,
            Selections = selections.OrderBy(x => x.Position).ToList()
        };
        Validate(ticket);
        var identity = new
        {
            date,
            ticket.Mode,
            ticket.Stake,
            Selections = ticket.Selections.OrderBy(x => x.MatchId).Select(x => new
            {
                x.MatchId, x.Market, x.Pick, x.Odds, x.KickoffUtc
            })
        };
        ticket.Fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identity))));
        return ticket;
    }

    public static void Validate(SimulatedTicket ticket)
    {
        if (ticket.Stake <= 0 || ticket.Selections.Count == 0 ||
            ticket.Mode is not ("safe" or "value" or "1-3p3+" or "1-3p3+-safe" or "ml" or "ml-safe" or
                "ml-league" or "ml-league-safe" or "daily-league-safe") ||
            ticket.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A simulation needs a positive stake, selections, a valid mode and a UTC timestamp.");
        if (ticket.Selections.Select(x => x.MatchId).Distinct().Count() != ticket.Selections.Count)
            throw new ArgumentException("A simulated ticket cannot contain the same match twice.");
        foreach (var pick in ticket.Selections)
        {
            if (pick.MatchId <= 0 || pick.Odds <= 1 || pick.BaseOdds <= 1 ||
                pick.KickoffUtc.Kind != DateTimeKind.Utc || pick.KickoffUtc <= ticket.CreatedAtUtc ||
                string.IsNullOrWhiteSpace(pick.Fixture) || string.IsNullOrWhiteSpace(pick.Market) ||
                string.IsNullOrWhiteSpace(pick.Pick) || string.IsNullOrWhiteSpace(pick.CalibrationKey) ||
                !double.IsFinite(pick.Accuracy) || pick.Accuracy is < 0 or > 1 ||
                !double.IsFinite(pick.Safe) || pick.Safe < 0 || pick.Safe > pick.Accuracy || pick.Sample <= 0 ||
                pick.Position < 1)
                throw new ArgumentException("Invalid simulated selection or kickoff is no longer in the future.");
        }
        if (!ticket.Selections.Select(x => x.Position).Order().SequenceEqual(
                Enumerable.Range(1, ticket.Selections.Count)))
            throw new ArgumentException("Simulated selection positions must be consecutive.");
    }

    public static async Task<(SimulatedTicket Ticket, bool Created)> SaveAsync(
        FootballDbContext db, SimulatedTicket ticket)
    {
        Validate(ticket);
        var existing = await db.SimulatedTickets.AsNoTracking().Include(x => x.Selections)
            .SingleOrDefaultAsync(x => x.Fingerprint == ticket.Fingerprint);
        if (existing != null)
            return (existing, false);

        db.SimulatedTickets.Add(ticket);
        try
        {
            await db.SaveChangesAsync();
            return (ticket, true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_simulated_tickets_Fingerprint"
        })
        {
            // Concurrent identical runs must reference the one frozen simulation.
            db.ChangeTracker.Clear();
            return (await db.SimulatedTickets.AsNoTracking().Include(x => x.Selections)
                .SingleAsync(x => x.Fingerprint == ticket.Fingerprint), false);
        }
    }
}
