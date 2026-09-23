namespace Prediction.Entities;

public sealed class Match
{
    public long Id { get; set; }

    public string ExternalMatchId { get; set; } = string.Empty;

    public string HomeTeam { get; set; } = string.Empty;

    public string AwayTeam { get; set; } = string.Empty;

    public DateTime? KickoffUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PredictionSnapshot> PredictionSnapshots { get; set; }
    = new List<PredictionSnapshot>();

    public MatchResult? Result { get; set; }
}