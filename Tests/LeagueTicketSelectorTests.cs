using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class LeagueTicketSelectorTests
{
    private sealed record Candidate(
        string League, string Name, double Agreement, int Votes, double Safety, double Ml, double Accuracy);

    [Theory]
    [InlineData("ENGLAND 1", 0)]
    [InlineData("Premier League", 0)]
    [InlineData("Ligue 2", 20)]
    [InlineData("England Championship", 21)]
    [InlineData("Bundesliga 2", 22)]
    [InlineData("Serie B", 23)]
    [InlineData("La Liga 2", 24)]
    public void MapsSupportedLeagueNamesToConfiguredOrder(string league, int expectedPriority)
    {
        Assert.Equal(expectedPriority, LeagueTicketSelector.GetPriority(league));
    }

    [Fact]
    public void DoesNotConfuseUnlistedDivisionsWithPriorityLeagues()
    {
        Assert.Equal(-1, LeagueTicketSelector.GetPriority("SWEDEN 3"));
        Assert.Equal(-1, LeagueTicketSelector.GetPriority("SLOVAKIA 2"));
    }

    [Fact]
    public void SelectsHighestAgreementInPriorityOrderThenAllowsSecondPickPerLeague()
    {
        var candidates = new[]
        {
            new Candidate("SWEDEN 1", "Sweden", .95, 5, .7, .8, .8),
            new Candidate("ENGLAND 1", "England lower agreement", .6, 6, .8, .8, .8),
            new Candidate("FRANCE 1", "France", .9, 5, .7, .8, .8),
            new Candidate("ENGLAND 1", "England best agreement", .8, 4, .7, .8, .8),
            new Candidate("FRANCE 1", "France second", .7, 3, .6, .7, .7),
            new Candidate("SWEDEN 1", "Sweden second", .7, 4, .6, .7, .7),
            new Candidate("SWEDEN 3", "Unlisted", 1, 10, 1, 1, 1)
        };

        var selected = LeagueTicketSelector.Select(candidates, 5,
            x => x.League, x => x.Agreement, x => x.Votes, x => x.Safety,
            x => x.Ml, x => x.Accuracy, _ => 0);

        Assert.Equal(
            ["England best agreement", "France", "Sweden", "England lower agreement", "France second"],
            selected.Select(x => x.Name));
    }

    [Fact]
    public void PrefersSourceAgreementBeforeAiSafetyWithinLeague()
    {
        var candidates = new[]
        {
            new Candidate("ENGLAND 1", "High AI only", .7, 4, .95, .99, .9),
            new Candidate("ENGLAND 1", "Better agreement", .8, 4, .6, .7, .7)
        };

        var selected = LeagueTicketSelector.Select(candidates, 1,
            x => x.League, x => x.Agreement, x => x.Votes, x => x.Safety,
            x => x.Ml, x => x.Accuracy, _ => 0);

        Assert.Equal("Better agreement", Assert.Single(selected).Name);
    }

    [Fact]
    public void DailyRankingChoosesSaferCandidateWhenBothHaveTenOfTenVotes()
    {
        var candidates = new[]
        {
            new Candidate("ITALY 1", "Milan", 1, 10, .55, .60, .60),
            new Candidate("ITALY 1", "Inter", 1, 10, .72, .78, .80)
        };
        var selected = LeagueTicketSelector.Select(candidates, 2,
            x => x.League, x => x.Agreement, x => x.Votes, x => x.Safety,
            x => x.Ml, x => x.Accuracy, _ => 0, safetyFirst: true);
        Assert.Equal(["Inter", "Milan"], selected.Select(x => x.Name));
    }

    [Fact]
    public void DailyRankingPrefersSafetyOverAgreementAndStillFillsFifteenWithTwoPerLeague()
    {
        string[] leagues = ["ENGLAND 1", "FRANCE 1", "GERMANY 1", "ITALY 1",
            "SPAIN 1", "BELGIUM 1", "NETHERLANDS 1", "PORTUGAL 1"];
        var candidates = leagues.SelectMany(league => new[]
        {
            new Candidate(league, "Safer", .8, 8, .8, .85, .85),
            new Candidate(league, "Unanimous", 1, 10, .6, .65, .65),
            new Candidate(league, "Third", .9, 9, .5, .55, .55)
        });
        var selected = LeagueTicketSelector.Select(candidates, 15,
            x => x.League, x => x.Agreement, x => x.Votes, x => x.Safety,
            x => x.Ml, x => x.Accuracy, _ => 0, safetyFirst: true);
        Assert.Equal(15, selected.Count);
        Assert.All(selected.Take(8), x => Assert.Equal("Safer", x.Name));
        Assert.All(selected.GroupBy(x => x.League), g => Assert.InRange(g.Count(), 1, 2));
        Assert.DoesNotContain(selected, x => x.Name == "Third");
    }
}
