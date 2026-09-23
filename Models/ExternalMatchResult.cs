namespace Prediction.Models;

public sealed class ExternalMatchResult
{
    public string ExternalId { get; set; } = string.Empty;

    public string HomeTeam { get; set; } = string.Empty;

    public string AwayTeam { get; set; } = string.Empty;

    public DateTime? KickoffUtc { get; set; }

    public int HomeGoals { get; set; }

    public int AwayGoals { get; set; }

    public string Source { get; set; } = string.Empty;
}