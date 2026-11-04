namespace Prediction.Models;

public sealed record SourceFixtureStatus(
    string SourceMatchId, string HomeTeam, string AwayTeam,
    string DateText, string Status, string HomeScore, string AwayScore,
    string HalfTimeHomeScore, string HalfTimeAwayScore, string ExtraTimeScore, string PenaltyScore);
