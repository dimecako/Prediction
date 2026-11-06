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

    [Fact]
    public void FixedAiChecksAllCandidatesBeforeTakingFifteen()
    {
        var matches = Enumerable.Range(1, 20).Select(i => Fixture(i, .9 - i * .01)).ToList();
        var evaluated = new List<long>();
        var result = DailyTicketSelector.BuildFixed(matches, matches.Select(m => Offer(m)).ToList(),
            Date, Now, 15, (match, _) =>
            {
                evaluated.Add(match.Id);
                return match.Id <= 3 ? .54 : .65;
            });

        Assert.Equal(20, evaluated.Count);
        Assert.Equal(17, result.Eligible);
        Assert.Equal(Enumerable.Range(4, 15).Select(i => (long)i), result.Picks.Select(p => p.MatchId));
        Assert.All(result.Picks, p =>
        {
            Assert.Equal("1X2", p.Candidate.Market);
            Assert.Equal("1", p.Candidate.Pick);
            Assert.Equal(.65, p.MlProbability);
            Assert.Contains("AI 1X2=1", p.Evidence);
        });
        Assert.Equal(3, result.Rejections.Count);
        var ticket = TicketLedger.CreateFixedAuto(Date, result.Picks, Now);
        Assert.Equal("fixed-auto", ticket.Mode);
        Assert.Equal(15, ticket.Selections.Count);
    }

    [Fact]
    public void FixedAiRanksCombinedConfidenceBeforeTruncation()
    {
        var matches = new[] { Fixture(1, .9), Fixture(2, .8), Fixture(3, .7) };
        var result = DailyTicketSelector.BuildFixed(matches, matches.Select(m => Offer(m)).ToList(),
            Date, Now, 1, (match, _) => match.Id == 3 ? .99 : .55);
        Assert.Equal(3, Assert.Single(result.Picks).MatchId);
    }

    [Fact]
    public void FixedSelectionDoesNotRelaxThresholdsToReachFifteen()
    {
        var matches = new[] { Fixture(1, .55), Fixture(2, .549), Fixture(3, .8), Fixture(4, .8) };
        var offers = new[] { Offer(matches[0], baseOdds: 1.6), Offer(matches[1]),
            Offer(matches[2], baseOdds: 1.59), Offer(matches[3]) };
        var result = DailyTicketSelector.BuildFixed(matches, offers, Date, Now, 15,
            (match, _) => match.Id == 4 ? .549 : .55);
        Assert.Equal(1, Assert.Single(result.Picks).MatchId);
        Assert.Equal(3, result.Rejections.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1.01)]
    public void FixedAiRejectsUnknownOrInvalidProbabilities(double? probability)
    {
        var match = Fixture(1);
        var result = DailyTicketSelector.BuildFixed([match], [Offer(match)], Date, Now, 15,
            (_, _) => probability);
        Assert.Empty(result.Picks);
        Assert.Contains("AI confidence", Assert.Single(result.Rejections));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(100)]
    public void SourceOnlyFixedSelectionPreservesRequestedCount(int count)
    {
        var matches = Enumerable.Range(1, 105).Select(i =>
        {
            var match = Fixture(i);
            string name = new string((char)('a' + i / 26), 12) +
                new string((char)('a' + i % 26), 12);
            match.HomeTeam = name + " Home";
            match.AwayTeam = name + " Away";
            return match;
        }).ToList();
        var result = DailyTicketSelector.BuildFixed(matches, matches.Select(m => Offer(m)).ToList(),
            Date, Now, count);
        Assert.Equal(count, result.Picks.Count);
        Assert.All(result.Picks, p => Assert.Null(p.MlProbability));
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
