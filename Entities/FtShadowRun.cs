namespace Prediction.Entities;

public sealed class FtShadowRun
{
    public long Id { get; set; }
    public string Experiment { get; set; } = string.Empty;
    public DateOnly MatchDate { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public int RequestedCount { get; set; }
    public string FrozenJson { get; set; } = string.Empty;
}
