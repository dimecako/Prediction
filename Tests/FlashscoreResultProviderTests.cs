using System.Text.Json;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class FlashscoreResultProviderTests
{
    private static readonly DateOnly Date = new(2026, 11, 8);

    [Fact]
    public void ParsesVerifiedFtAndFirstHalfScore()
    {
        var result = FlashscoreResultProvider.ParseVerifiedMatch(Date, "M109CYgU",
            "Crystal Palace", "Liverpool", "08.11.2026 15:00", "Finished", "1\n-\n3",
            ["1ST HALF", "0 - 1", "2ND HALF", "1 - 2"]);

        Assert.Equal("flashscore:M109CYgU", result.ExternalId);
        Assert.Equal("Flashscore", result.Source);
        Assert.Equal(1, result.HomeGoals);
        Assert.Equal(3, result.AwayGoals);
        Assert.Equal(0, result.HalfTimeHomeGoals);
        Assert.Equal(1, result.HalfTimeAwayGoals);
        Assert.True(result.IsRegulationTimeVerified);
    }

    [Fact]
    public void MissingFirstHalfScoreRemainsUnknown()
    {
        var result = FlashscoreResultProvider.ParseVerifiedMatch(Date, "match-id",
            "Home", "Away", "08.11.2026 15:00", "Finished", "2 - 1", ["2ND HALF", "1 - 1"]);

        Assert.Null(result.HalfTimeHomeGoals);
        Assert.Null(result.HalfTimeAwayGoals);
    }

    [Theory]
    [InlineData("After Extra Time", "")]
    [InlineData("Finished", "EXTRA TIME")]
    [InlineData("Finished", "PENALTIES")]
    public void ExtraTimeAndPenaltyResultsAreRejected(string status, string period)
    {
        Assert.Throws<JsonException>(() => FlashscoreResultProvider.ParseVerifiedMatch(Date,
            "match-id", "Home", "Away", "08.11.2026 15:00", status, "2 - 1",
            period.Length == 0 ? [] : [period]));
    }

    [Theory]
    [InlineData("07.11.2026 15:00", "08.11.2026")]
    [InlineData("08.11.2026 15:00", "2 - 1 aet")]
    [InlineData("08.11.2026 15:00", "0 - 1")]
    public void WrongDateOrInvalidAndImpossibleScoreAreRejected(string kickoff, string score)
    {
        Assert.Throws<JsonException>(() => FlashscoreResultProvider.ParseVerifiedMatch(Date,
            "match-id", "Home", "Away", kickoff, "Finished", score, ["1ST HALF", "1 - 0"]));
    }
}
