using Newtonsoft.Json.Linq;
using Xunit;

namespace Prediction.Tests;

public sealed class ForebetResultTests
{
    private static readonly DateOnly Date = new(2026, 10, 18);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void ReadsExplicitHalfTimeScores(int home, int away)
    {
        var row = Row();
        row["Host_SC_HT"] = home;
        row["Guest_SC_HT"] = away;
        var result = Assert.Single(Parse(row));
        Assert.Equal(2, result.HomeGoals);
        Assert.Equal(1, result.AwayGoals);
        Assert.Equal(home, result.HalfTimeHomeGoals);
        Assert.Equal(away, result.HalfTimeAwayGoals);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("1", null)]
    [InlineData("-1", "0")]
    [InlineData("3", "0")]
    [InlineData("0", "2")]
    public void MissingOrInvalidHalfTimeNeverBecomesZeroZero(string? home, string? away)
    {
        var row = Row();
        row["Host_SC_HT"] = home;
        row["Guest_SC_HT"] = away;
        var result = Assert.Single(Parse(row));
        Assert.Null(result.HalfTimeHomeGoals);
        Assert.Null(result.HalfTimeAwayGoals);
    }

    [Theory]
    [InlineData("2026-10-17 15:00:00", "FT")]
    [InlineData("2026-10-18 15:00:00", "HT")]
    [InlineData("2026-10-18 15:00:00", "Postp.")]
    public void OnlyFinalResultsOnRequestedDateAreCollected(string date, string status)
    {
        var row = Row();
        row["DATE_BAH"] = date;
        row["comment"] = status;
        Assert.Empty(Parse(row));
    }

    private static List<Prediction.Models.ExternalMatchResult> Parse(JObject row) =>
        ProductionConsensusAggregator.ParseForebetResults(new JArray { new JArray { row } }.ToString(), Date);

    private static JObject Row() => new()
    {
        ["id"] = "123", ["DATE_BAH"] = "2026-10-18 15:00:00", ["comment"] = "FT",
        ["HOST_NAME"] = "Home", ["GUEST_NAME"] = "Away", ["Host_SC"] = "2", ["Guest_SC"] = "1"
    };
}
