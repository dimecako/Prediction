namespace Prediction.Entities;

public sealed class MatchResult
{
    public long Id { get; set; }

    public long MatchId { get; set; }

    public int HomeGoals { get; set; }

    public int AwayGoals { get; set; }

    // 1 / X / 2
    public string Result { get; set; } = string.Empty;

    public bool Btts { get; set; }

    public int TotalGoals { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTime CollectedAtUtc { get; set; } = DateTime.UtcNow;

    public Match Match { get; set; } = null!;
}