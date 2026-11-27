using Newtonsoft.Json.Linq;
using Xunit;

namespace Prediction.Tests;

public sealed class BetMinesParserTests
{
    [Fact]
    public void MapsFixtureMarketsAndKeepsLeagueCountryQualified()
    {
        var row = new JObject
        {
            ["id"] = 123,
            ["dateTime"] = "2026-11-27T20:00:00Z",
            ["localTeam"] = new JObject { ["name"] = "Home FC" },
            ["visitorTeam"] = new JObject { ["name"] = "Away FC" },
            ["league"] = new JObject
            {
                ["name"] = "Ligue 1",
                ["country"] = new JObject { ["name"] = "France" }
            },
            ["prediction"] = "12",
            ["probability"] = new JObject
            {
                ["home"] = 40,
                ["draw"] = 20,
                ["away"] = 40,
                ["home_away"] = 80,
                ["btts"] = 65,
                ["btts_no"] = 35,
                ["over_2_5"] = 58,
                ["under_2_5"] = 42
            }
        };

        var matches = ProductionConsensusAggregator.ParseBetMinesApiResponse(
            "[" + row.ToString() + "]",
            "2026-11-27");

        var match = Assert.Single(matches);
        Assert.Equal("BetMines", match.SiteName);
        Assert.Equal("FRANCE LIGUE 1", match.League);
        Assert.Equal("12", match.Tip);
        Assert.Equal(80, match.Prob);
        Assert.Equal("GG", match.BttsMarket);
        Assert.Equal("O2.5", match.GoalsMarket);
        Assert.Equal("betmines:123", match.SourceMatchId);
    }

}
