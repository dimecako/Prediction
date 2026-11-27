using Newtonsoft.Json.Linq;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class ProductionPipelineLeagueFilterTests
{
    [Fact]
    public void BetMinesExperimentAttachesWithoutBecomingConsensusSource()
    {
        var aggregator = new ProductionConsensusAggregator("2030-01-01");
        var allowedLeagueSource = new List<SiteMatch>
        {
            LeaguePrediction("Allowed Home", "Allowed Away", "UEFA Champions League")
        };

        aggregator.ExecutePipeline(
            [],
            [],
            [],
            [],
            [],
            [],
            allowedLeagueSource,
            [],
            [],
            [],
            []);

        var attached = aggregator.AttachBetMinesExperiment(
            [new SiteMatch
            {
                SiteName = "BetMines",
                SourceMatchId = "betmines:123",
                HomeTeam = "Allowed Home",
                AwayTeam = "Allowed Away",
                Tip = "12",
                Prob = 80,
                BttsMarket = "GG",
                GoalsMarket = "O2.5"
            }]);

        Assert.Equal(1, attached);
        var match = Assert.Single(aggregator.UnifiedMatches);
        Assert.NotNull(match.Sources["BetMines"]);
        Assert.Equal("12", match.Sources["BetMines"]!.Tip);
    }

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

    [Theory]
    [InlineData("Barcelona", "Real Madrid", "Spain La Liga 1", "La Liga", "SPAIN LA LIGA")]
    [InlineData("Paris SG", "Marseille", "France 1", "Ligue 1", "FRANCE LIGUE 1")]
    [InlineData("Arsenal", "Chelsea", "England 1", "Premier League", "ENGLAND PREMIER LEAGUE")]
    [InlineData("Inter", "Milan", "Italy 1", "Serie A", "ITALY SERIE A")]
    [InlineData("Barcelona", "Real Madrid", "Spain La Ligue 1", "La Ligue 1", "SPAIN LA LIGA")]
    public void PipelineUsesOneCanonicalLeagueAcrossSourcesWhenAnAliasIsUnqualified(
        string home, string away, string qualifiedLeague, string unqualifiedLeague, string expectedLeague)
    {
        var aggregator = new ProductionConsensusAggregator("2030-01-01");
        var forebet = new List<SiteMatch> { Prediction(home, away) };
        var qualified = new List<SiteMatch>
        {
            LeaguePrediction(home, away, qualifiedLeague)
        };
        var unqualified = new List<SiteMatch>
        {
            LeaguePrediction(home, away, unqualifiedLeague)
        };

        aggregator.ExecutePipeline(
            forebet, [], [], [], [], [], qualified, unqualified, [], [], []);

        var match = Assert.Single(aggregator.UnifiedMatches);
        Assert.Equal(expectedLeague, match.League);
        Assert.All(forebet.Concat(qualified).Concat(unqualified),
            source => Assert.Equal(expectedLeague, source.League));
    }

    [Fact]
    public void PipelineRejectsFixtureWithConflictingAllowedLeagueIdentities()
    {
        var aggregator = new ProductionConsensusAggregator("2030-01-01");
        var forebet = new List<SiteMatch> { Prediction("Shared Home", "Shared Away") };
        var france = new List<SiteMatch>
        {
            LeaguePrediction("Shared Home", "Shared Away", "France Ligue 1")
        };
        var spain = new List<SiteMatch>
        {
            LeaguePrediction("Shared Home", "Shared Away", "Spain La Liga")
        };

        aggregator.ExecutePipeline(
            forebet, [], [], [], [], [], france, spain, [], [], []);

        Assert.Empty(aggregator.UnifiedMatches);
    }

    [Fact]
    public void PipelineUnifiesBorussiaGladbachSpellingAcrossSources()
    {
        var aggregator = new ProductionConsensusAggregator("2030-01-01");
        var abbreviated = new List<SiteMatch>
        {
            Prediction("Borussia M'gladbach", "Borussia Dortmund")
        };
        var full = new List<SiteMatch>
        {
            LeaguePrediction("Borussia Monchengladbach", "Borussia Dortmund", "Germany Bundesliga")
        };

        aggregator.ExecutePipeline(
            abbreviated, [], [], [], [], [], full, [], [], [], []);

        var match = Assert.Single(aggregator.UnifiedMatches);
        Assert.Equal("borussia monchengladbach", MatchIdentity.NormalizeTeam(match.HomeOrig));
        Assert.Equal(2, match.Sources.Count(source => source.Value != null));
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
