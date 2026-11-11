using Newtonsoft.Json.Linq;
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

    [Theory]
    [InlineData("France", "Ligue 1", "FRANCE LIGUE 1")]
    [InlineData("DR Congo", "Ligue 1", null)]
    [InlineData("Italy", "Serie B", "ITALY SERIE B")]
    [InlineData("Brazil", "Serie B", null)]
    [InlineData(null, "Ligue 1", null)]
    public void PipelineRequiresCountryQualifiedCompetitionBeforeMergingAnySource(
        string? country, string name, string? expectedLeague)
    {
        var aggregator = new ProductionConsensusAggregator("2030-01-01");
        var sourceMatches = new List<SiteMatch> { Prediction("Home", "Away") };
        var metadata = new JObject { ["name"] = name, ["countryName"] = country };
        var leagueMatches = new List<SiteMatch>
        {
            LeaguePrediction("Home", "Away", SourceLeagueReader.Read(metadata, "name")!)
        };

        aggregator.ExecutePipeline(sourceMatches, [], [], [], [], [], [], leagueMatches, [], [], []);

        if (expectedLeague == null)
        {
            Assert.Empty(aggregator.UnifiedMatches);
            Assert.Empty(sourceMatches);
            Assert.Empty(leagueMatches);
        }
        else
        {
            Assert.Equal(expectedLeague, Assert.Single(aggregator.UnifiedMatches).League);
            Assert.Equal(expectedLeague, Assert.Single(sourceMatches).League);
            Assert.Equal(expectedLeague, Assert.Single(leagueMatches).League);
        }
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
