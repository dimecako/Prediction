namespace Prediction.Entities;

public sealed class SimulatedTicket
{
    public long Id { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public DateOnly MatchDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Mode { get; set; } = string.Empty;
    public decimal Stake { get; set; } = 1m;
    public ICollection<SimulatedTicketSelection> Selections { get; set; } = new List<SimulatedTicketSelection>();
}

public sealed class SimulatedTicketSelection
{
    public long Id { get; set; }
    public long TicketId { get; set; }
    public SimulatedTicket Ticket { get; set; } = null!;
    public long MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public string Fixture { get; set; } = string.Empty;
    public int Position { get; set; }
    public string? League { get; set; }
    public DateTime KickoffUtc { get; set; }
    public string Market { get; set; } = string.Empty;
    public string Pick { get; set; } = string.Empty;
    public string CalibrationKey { get; set; } = string.Empty;
    public decimal Odds { get; set; }
    public double Accuracy { get; set; }
    public double Safe { get; set; }
    public int Sample { get; set; }
    public decimal BaseOdds { get; set; }
    public string Evidence { get; set; } = string.Empty;
}
