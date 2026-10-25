using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class DailyTicketSelectorTests
{
    private static readonly DateOnly Date = new(2030, 1, 1);
    private static readonly DateTime Now = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SelectsDailyFixWithoutHistoryOrResults()
    {
        var match = Fixture(1);
        var decision = DailyTicketSelector.Select(match, Offer(match));
        Assert.Equal("1", decision.Pick!.Candidate.Pick);
        Assert.Equal("1X2", decision.Pick.Candidate.Market);
        Assert.Equal(75, decision.Pick.SourceProbability);
        Assert.Null(match.Result);
    }

    [Theory]
    [InlineData("1", "419", "1 & T1 2+")]
    [InlineData("2", "420", "2 & T2 2+")]
    public void LowOddsAllowConfirmedTeamGoalsWithoutHistory(string side, string code, string pick)
    {
        var match = Fixture(1, side: side, goals: "U2.5");
        var decision = DailyTicketSelector.Select(match, Offer(match, baseOdds: 1.3,
            extraOdds: new Dictionary<string, double> { [code] = 1.75 }));
        Assert.Equal(pick, decision.Pick!.Candidate.Pick);
        Assert.Equal(1.75, decision.Pick.Odds);
    }

    [Fact]
    public void AlternativesPreferLowestConfirmedQualifyingPrice()
    {
        var match = Fixture(1);
        var decision = DailyTicketSelector.Select(match, Offer(match, baseOdds: 1.4,
            extraOdds: new Dictionary<string, double> { ["419"] = 1.9, ["50723"] = 1.7, ["51784"] = 1.6 }));
        Assert.Equal("1 & 2-5 golovi", decision.Pick!.Candidate.Pick);
        Assert.Equal(1.7, decision.Pick.Odds);
    }

    [Fact]
    public void DoesNotInventMissingCompositePrice()
    {
        var match = Fixture(1);
        Assert.Null(DailyTicketSelector.Select(match, Offer(match, baseOdds: 1.4)).Pick);
    }

    [Theory]
    [InlineData(1.69, false)]
    [InlineData(1.7, true)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void AlternativesRequireValidPriceAtMinimum(double odds, bool expected)
    {
        var match = Fixture(1);
        var decision = DailyTicketSelector.Select(match, Offer(match, baseOdds: 1.4,
            extraOdds: new Dictionary<string, double> { ["419"] = odds }));
        Assert.Equal(expected, decision.Pick != null);
    }

    [Fact]
    public void HighOddsUseDailyOverSignalInsteadOfHistoricalThreshold()
    {
        var match = Fixture(1);
        var decision = DailyTicketSelector.Select(match, Offer(match, baseOdds: 2.2,
            extraOdds: new Dictionary<string, double> { ["24"] = 1.8 }));
        Assert.Equal("O2.5", decision.Pick!.Candidate.Pick);
        match.PredictionSnapshots.Single().Goals = "U2.5";
        Assert.Null(DailyTicketSelector.Select(match, Offer(match, baseOdds: 2.2,
            extraOdds: new Dictionary<string, double> { ["24"] = 1.8 })).Pick);
    }

    [Fact]
    public void MissingSourceProbabilityStaysUnknownAndCanStillSupplyCombination()
    {
        var match = Fixture(1);
        match.PredictionSnapshots.Single().Confidence = null;
        var decision = DailyTicketSelector.Select(match, Offer(match, baseOdds: 1.3,
            extraOdds: new Dictionary<string, double> { ["419"] = 1.75 }));
        Assert.NotNull(decision.Pick);
        Assert.Null(decision.Pick.SourceProbability);
        Assert.Contains("Prob.=n/a", decision.Pick.Evidence);
    }

    [Fact]
    public void ConflictingResultVotesDoNotSelectEitherSide()
    {
        var match = Fixture(1);
        match.PredictionSnapshots.Add(new() { Source = "Other", PredictedResult = "2", Confidence = .8 });
        Assert.Null(DailyTicketSelector.Select(match, Offer(match)).Pick);
    }

    [Fact]
    public void RanksInterBeforeMilanEvenWithSameTenOfTenAgreement()
    {
        var milan = Fixture(1, .567);
        var inter = Fixture(2, .707);
        foreach (var match in new[] { milan, inter })
            for (int i = 1; i < 10; i++)
                match.PredictionSnapshots.Add(new()
                {
                    Source = $"Source{i}", PredictedResult = "1",
                    Confidence = match.PredictionSnapshots.First().Confidence
                });
        var selection = Build([milan, inter], [Offer(milan), Offer(inter)], 2, false);
        Assert.Equal([inter.Id, milan.Id], selection.Picks.Select(p => p.MatchId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothModesCanSelectFifteenFromDailyFixturesWithoutHistory(bool leagueOnly)
    {
        var matches = Enumerable.Range(1, 20).Select(i => Fixture(i)).ToList();
        var offers = matches.Select(m => Offer(m, league: leagueOnly ? "ITALY 1" : "UNLISTED LEAGUE")).ToList();
        var result = Build(matches, offers, 15, leagueOnly);
        Assert.Equal(15, result.Picks.Count);
        Assert.Equal(15, result.Picks.Select(p => p.MatchId).Distinct().Count());
    }

    [Fact]
    public void LeagueOnlyFiltersConfiguredLeaguesAndRoundsInPriorityOrder()
    {
        var matches = Enumerable.Range(1, 4).Select(i => Fixture(i, .5 + i * .1)).ToList();
        var offers = new[]
        {
            Offer(matches[0], league: "ENGLAND 1"),
            Offer(matches[1], league: "ENGLAND 1"),
            Offer(matches[2], league: "ITALY 1"),
            Offer(matches[3], league: "UNLISTED LEAGUE")
        };
        var league = Build(matches, offers, 15, true);
        Assert.Equal([2L, 3L, 1L], league.Picks.Select(p => p.MatchId));
        Assert.Equal(1, league.ExcludedLeagues);
        var all = Build(matches, offers, 15, false);
        Assert.Equal(4, all.Picks.Count);
        Assert.Equal(4, all.Picks[0].MatchId);
    }

    [Fact]
    public void ExcludesWrongDatesStartedOffersAndUnmatchedFixtures()
    {
        var valid = Fixture(1);
        var old = Fixture(2);
        old.MatchDate = Date.AddDays(-1);
        var started = Fixture(3);
        var missing = Fixture(4);
        var result = Build([valid, old, started, missing],
            [Offer(valid), Offer(old), Offer(started) with { KickoffUtc = Now }], 15, false);
        Assert.Equal(valid.Id, Assert.Single(result.Picks).MatchId);
        Assert.Contains(result.Rejections, r => r.Contains("zapocnal"));
        Assert.Contains(result.Rejections, r => r.Contains("Nema Maxbet"));
    }

    [Fact]
    public void DeduplicatesSameFixtureAndUsesLatestSnapshotPerSource()
    {
        var first = Fixture(1, .6);
        var duplicate = Fixture(2, .8);
        duplicate.HomeTeam = first.HomeTeam;
        duplicate.AwayTeam = first.AwayTeam;
        duplicate.PredictionSnapshots.Add(new()
        {
            Source = "Second", PredictedResult = "1", Confidence = .8
        });
        duplicate.PredictionSnapshots.Add(new()
        {
            Source = "Source", PredictedResult = "2", Confidence = .99, CapturedAtUtc = Now.AddDays(-1)
        });
        var result = Build([first, duplicate], [Offer(first)], 15, false);
        Assert.Equal(1, result.Duplicates);
        var pick = Assert.Single(result.Picks);
        Assert.Equal(duplicate.Id, pick.MatchId);
        Assert.Equal(2, pick.TotalVotes);
        Assert.Equal(80, pick.SourceProbability);
    }

    private static DailyTicketSelection Build(
        IReadOnlyList<Match> matches, IReadOnlyList<BookmakerMatch> offers, int count, bool leagueOnly) =>
        DailyTicketSelector.Build(matches, offers, Date, Now, count, leagueOnly);

    private static Match Fixture(int id, double confidence = .75, string side = "1", string goals = "O2.5") => new()
    {
        Id = id, MatchDate = Date, HomeTeam = $"Home{id:D2}", AwayTeam = $"Away{id:D2}",
        PredictionSnapshots =
        [
            new() { Source = "Source", PredictedResult = side, Confidence = confidence,
                Goals = goals, Btts = "YES", CapturedAtUtc = Now }
        ]
    };

    private static BookmakerMatch Offer(
        Match match, string league = "ITALY 1", double baseOdds = 1.8,
        IReadOnlyDictionary<string, double>? extraOdds = null)
    {
        var odds = new Dictionary<string, double> { ["1"] = baseOdds, ["3"] = baseOdds };
        if (extraOdds != null)
            foreach (var (key, value) in extraOdds)
                odds[key] = value;
        return new(match.HomeTeam, match.AwayTeam, Now.AddHours(5), odds, league);
    }
}
