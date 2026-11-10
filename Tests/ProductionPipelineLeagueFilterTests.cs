using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class ProductionPipelineLeagueFilterTests
{
    [Fact]
    public void PipelineMergesOnlyFixturesUniquelyMatchedToAnAllowedLeagueOffer()
    {
        var aggregator = new ProductionConsensusAggregator("2030-01-01");
        var sourceMatches = new List<SiteMatch>
        {
            Prediction("Allowed Home", "Allowed Away"),
            Prediction("Outside Home", "Outside Away"),
            Prediction("Ambiguous Home", "Ambiguous Away"),
            Prediction("Unmatched Home", "Unmatched Away")
        };
        var leagueSourceMatches = new List<SiteMatch>
        {
            LeaguePrediction("Allowed Home", "Allowed Away", "UEFA Champions League"),
            LeaguePrediction("Outside Home", "Outside Away", "SWEDEN 3"),
            LeaguePrediction("Ambiguous Home", "Ambiguous Away", "UEFA Europa League"),
            LeaguePrediction("Ambiguous Home", "Ambiguous Away", "SWEDEN 3")
        };

        aggregator.ExecutePipeline(sourceMatches, [], [], [], [], [], leagueSourceMatches, [], [], [], []);

        Assert.Equal(new[] { ("Allowed Home", "Allowed Away") },
            aggregator.UnifiedMatches.Select(match => (match.HomeOrig, match.AwayOrig)));
        Assert.Equal(new[] { "Allowed Home" }, sourceMatches.Select(match => match.HomeTeam));
        Assert.Equal(new[] { "Allowed Home" }, leagueSourceMatches.Select(match => match.HomeTeam));
    }

    private static SiteMatch Prediction(string home, string away) => new()
    {
        SiteName = "Forebet",
        HomeTeam = home,
        AwayTeam = away,
        Tip = "1"
    };

    private static SiteMatch LeaguePrediction(string home, string away, string league) =>
        new()
        {
            SiteName = "Feedinco",
            HomeTeam = home,
            AwayTeam = away,
            Tip = "1",
            League = league
        };
}
