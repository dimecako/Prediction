using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class TeamNameMatcherTests
{
    [Theory]
    [InlineData("Bayern M.", "Bayern Munich")]
    [InlineData("Shakhtar D.", "Shakhtar Donetsk")]
    [InlineData("Guadalajara C.", "Guadalajara Chivas")]
    [InlineData("Kashiwa R.", "Kashiwa Reysol")]
    [InlineData("Kawasaki F.", "Kawasaki Frontale")]
    [InlineData("Leyton O.", "Leyton Orient")]
    [InlineData("Blaublitz A.", "Blaublitz Akita")]
    [InlineData("A.Fukuoka", "Avispa Fukuoka")]
    [InlineData("V Sendai", "Vegalta Sendai")]
    [InlineData("U.A.N.L. Tigres", "Tigres UANL")]
    [InlineData("Kusatsu", "Thespakusatsu")]
    [InlineData("Yokohama F-M.", "Yokohama F. Marinos")]
    [InlineData("Yokohama F-M.", "Yokohama Marinos")]
    public void AbbreviatedMaxbetNamesMatchFullNames(string maxbet, string ours)
    {
        Assert.True(TeamNameMatcher.Similar(maxbet, ours));
        Assert.True(TeamNameMatcher.Similar(ours, maxbet));
    }

    [Theory]
    [InlineData("Real Madrid C", "Real Madrid")]
    [InlineData("Real S.", "Real Madrid")]
    [InlineData("Bayern M.", "Bayern U19")]
    [InlineData("Bayern M.", "Bayern Munich W")]
    [InlineData("Slavia", "Slavia U19")]
    [InlineData("Inter M.", "Inter Bogota")]
    [InlineData("Guadalajara C.", "Guadalajara W")]
    public void InitialsNeverBridgeDifferentTeams(string a, string b)
    {
        Assert.False(TeamNameMatcher.Similar(a, b));
        Assert.False(TeamNameMatcher.Similar(b, a));
    }

    [Fact]
    public void FixtureNeedsBothTeamsToMatch()
    {
        Assert.True(TeamNameMatcher.SameFixture("Bayern M.", "Arsenal", "Bayern Munich", "Arsenal"));
        Assert.False(TeamNameMatcher.SameFixture("Bayern M.", "Arsenal", "Bayern Munich", "Chelsea"));
    }
}
