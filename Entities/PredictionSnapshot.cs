namespace Prediction.Entities;

public sealed class PredictionSnapshot
{
    public long Id { get; set; }

    public long MatchId { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? PredictedResult { get; set; }

    public string? PredictedScore { get; set; }

    public string? Btts { get; set; }

    public string? Goals { get; set; }

    public double? Confidence { get; set; }

    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;

    public Match Match { get; set; } = null!;
}