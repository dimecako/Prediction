using Prediction.Entities;

namespace Prediction.Services;

public enum SimulatedTicketStatus { Pending, Won, Lost }

public sealed record SettledSelection(SimulatedTicketSelection Selection, bool? Correct);

public sealed record TicketSettlement(
    SimulatedTicket Ticket, SimulatedTicketStatus Status, decimal CombinedOdds,
    decimal? Payout, IReadOnlyList<SettledSelection> Selections)
{
    public decimal? Profit => Payout - Ticket.Stake;
}

public sealed record TicketPerformance(
    int Total, int Won, int Lost, int Pending, decimal SettledStake,
    decimal PendingStake, decimal Payout)
{
    public decimal Profit => Payout - SettledStake;
    public decimal? Roi => SettledStake > 0 ? Profit / SettledStake : null;
}

public static class TicketPerformanceAnalyzer
{
    public const string UnknownLeague = "(unknown league)";

    public static string LeagueName(string? league) =>
        string.IsNullOrWhiteSpace(league) ? UnknownLeague : league.Trim();

    public static string? PendingReason(SettledSelection selection, MatchResult? result) =>
        selection.Correct.HasValue ? null :
        result == null ? "missing final result (unfinished, unavailable or unmatched; not inferred)" :
        "missing required half-time score";

    public static TicketSettlement Settle(SimulatedTicket ticket, IReadOnlyDictionary<long, MatchResult> results)
    {
        if (ticket.Stake <= 0 || ticket.Selections.Count == 0)
            throw new ArgumentException("Cannot settle an empty ticket or a non-positive stake.");
        decimal combinedOdds = 1m;
        var selections = new List<SettledSelection>();
        foreach (var pick in ticket.Selections)
        {
            if (pick.Odds <= 1)
                throw new ArgumentException("A stored selection has invalid odds.");
            combinedOdds = checked(combinedOdds * pick.Odds);
            bool? correct = results.TryGetValue(pick.MatchId, out var result)
                ? TicketMarkets.IsCorrect(new(pick.Market, pick.Pick, pick.CalibrationKey, ""), result, allowFinalOnlyLoss: true)
                : null;
            selections.Add(new(pick, correct));
        }
        var status = selections.Any(x => x.Correct == false) ? SimulatedTicketStatus.Lost :
            selections.All(x => x.Correct == true) ? SimulatedTicketStatus.Won : SimulatedTicketStatus.Pending;
        decimal? payout = status switch
        {
            SimulatedTicketStatus.Won => checked(ticket.Stake * combinedOdds),
            SimulatedTicketStatus.Lost => 0m,
            _ => null
        };
        return new(ticket, status, combinedOdds, payout, selections);
    }

    public static TicketPerformance Summarize(IEnumerable<TicketSettlement> settlements)
    {
        var rows = settlements.ToList();
        return new(rows.Count,
            rows.Count(x => x.Status == SimulatedTicketStatus.Won),
            rows.Count(x => x.Status == SimulatedTicketStatus.Lost),
            rows.Count(x => x.Status == SimulatedTicketStatus.Pending),
            rows.Where(x => x.Payout.HasValue).Sum(x => x.Ticket.Stake),
            rows.Where(x => !x.Payout.HasValue).Sum(x => x.Ticket.Stake),
            rows.Sum(x => x.Payout ?? 0m));
    }
}
