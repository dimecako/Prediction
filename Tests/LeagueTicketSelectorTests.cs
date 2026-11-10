using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class LeagueTicketSelectorTests
{
    private sealed record Candidate(
        string League, string Name, double Agreement, int Votes, double Safety, double Ml, double Accuracy);

    [Theory]
    [InlineData("ENGLAND 1")]
    [InlineData("Premier League")]
    [InlineData("Ligue 2")]
    [InlineData("England Championship")]
    [InlineData("Bundesliga 2")]
    [InlineData("Serie B")]
    [InlineData("La Liga 2")]
    [InlineData("Austria 2. Liga")]
    [InlineData("Bulgaria 1")]
    [InlineData("Chile Liga de Primera")]
    [InlineData("Cyprus")]
    [InlineData("Greece Super League")]
    [InlineData("Hungary NB1")]
    [InlineData("Netherlands Eerste Divisie")]
    [InlineData("Norway Eliteserien")]
    [InlineData("Portugal Liga Portugal 2")]
    [InlineData("Scottish Championship")]
    [InlineData("Switzerland Challenge League")]
    [InlineData("USA MLS")]
    [InlineData("Denmark Superliga")]
    [InlineData("UEFA Champions League")]
    [InlineData("UEFA Europa League")]
    [InlineData("UEFA Conference League")]
    public void AllowsConfiguredLeagueNamesWithoutPriorityRanks(string league)
    {
        Assert.True(LeagueTicketSelector.IsAllowedLeague(league));
        Assert.Equal(LeagueEligibility.Eligible, LeagueTicketSelector.Classify(league));
    }

    [Fact]
    public void DoesNotConfuseUnlistedDivisionsWithAllowedLeagues()
    {
        Assert.False(LeagueTicketSelector.IsAllowedLeague("SWEDEN 3"));
        Assert.False(LeagueTicketSelector.IsAllowedLeague("SLOVAKIA 2"));
    }

    [Theory]
    [InlineData(null, LeagueEligibility.Missing)]
    [InlineData("", LeagueEligibility.Missing)]
    [InlineData("  ", LeagueEligibility.Missing)]
    [InlineData("UEFA Champions League", LeagueEligibility.Eligible)]
    [InlineData("UEFA Youth League", LeagueEligibility.IntentionallyExcluded)]
    [InlineData("England FA Cup", LeagueEligibility.IntentionallyExcluded)]
    [InlineData("DFB-Pokal", LeagueEligibility.IntentionallyExcluded)]
    [InlineData("England 1", LeagueEligibility.Eligible)]
    [InlineData("Türkiye Süper Lig", LeagueEligibility.Unrecognized)]
    [InlineData("Turkey Süper Lig", LeagueEligibility.Eligible)]
    [InlineData("SWEDEN 3", LeagueEligibility.Unrecognized)]
    [InlineData("England U19", LeagueEligibility.Unrecognized)]
    [InlineData("Imaginary Cup", LeagueEligibility.Unrecognized)]
    [InlineData("Not UEFA Champions League", LeagueEligibility.Unrecognized)]
    public void DiagnosesOnlyExplicitCompetitionIdentities(string? league, LeagueEligibility expected)
    {
        Assert.Equal(expected, LeagueTicketSelector.Classify(league));
        if (expected != LeagueEligibility.Eligible)
            Assert.False(LeagueTicketSelector.IsAllowedLeague(league));
    }

    [Fact]
    public void DiagnosisRetainsActualNamesAndCountsInsteadOfCallingEveryExcludedLeagueUnknown()
    {
        var diagnosis = LeagueTicketSelector.Diagnose(
            [null, "", "UEFA Champions League", "UEFA Champions League", "Imaginary Cup", "ENGLAND 1"]);
        Assert.Contains(new("(missing)", LeagueEligibility.Missing, 2), diagnosis);
        Assert.Contains(new("UEFA Champions League", LeagueEligibility.Eligible, 2), diagnosis);
        Assert.Contains(new("Imaginary Cup", LeagueEligibility.Unrecognized, 1), diagnosis);
        Assert.Contains(new("ENGLAND 1", LeagueEligibility.Eligible, 1), diagnosis);
    }

    [Theory]
    [InlineData("Bad\r\n[FAKE] success")]
    [InlineData("Bad\tleague\u001b[31m")]
    [InlineData("Bad\u2028league\u2029name")]
    [InlineData("Bad\u202Eleague")]
    public void DiagnosisLabelsCannotInjectControlCharactersOrLogLines(string league)
    {
        var label = Assert.Single(LeagueTicketSelector.Diagnose([league])).League;
        Assert.DoesNotContain(label, c => char.IsControl(c) ||
            char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator or
                System.Globalization.UnicodeCategory.ParagraphSeparator or System.Globalization.UnicodeCategory.Format);
        Assert.Equal(LeagueEligibility.Unrecognized, LeagueTicketSelector.Classify(league));
    }

    [Fact]
    public void SelectsAllAllowedLeaguesByRankingAndRoundRobinWithoutPriority()
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
            ["Sweden", "France", "England best agreement", "Sweden second", "France second"],
            selected.Select(x => x.Name));
    }

    [Fact]
    public void SelectionExcludesUnconfiguredLeaguesAndAllowsConfiguredEuropeanCompetitions()
    {
        var candidates = new[]
        {
            new Candidate("SWEDEN 3", "Fallback first", 1, 10, 1, 1, 1),
            new Candidate("ENGLAND 1", "England first", .8, 8, .8, .8, .8),
            new Candidate("FRANCE 1", "France first", .8, 8, .8, .8, .8),
            new Candidate("UEFA Champions League", "Cup", 1, 10, 1, 1, 1),
            new Candidate("", "Missing league", 1, 10, 1, 1, 1),
            new Candidate("SWEDEN 3", "Fallback second", .7, 7, .7, .7, .7),
            new Candidate("Unknown League", "Other fallback", .7, 7, .7, .7, .7),
            new Candidate("ENGLAND 1", "England second", .7, 7, .7, .7, .7)
        };

        var selected = LeagueTicketSelector.SelectAllowedLeagues(candidates, 6, x => x.League);

        Assert.Equal(
            ["England first", "France first", "Cup", "England second"],
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
