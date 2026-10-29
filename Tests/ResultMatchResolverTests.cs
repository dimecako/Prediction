using Prediction.Entities;
using Prediction.Models;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class ResultMatchResolverTests
{
    private static readonly DateOnly Date = new(2026, 10, 28);

    [Fact]
    public void StoredSourceIdResolvesChangedNames()
    {
        var match = Fixture(1, "Original Home", "Original Away");
        var result = Result("New Home", "New Away");
        var resolution = ResultMatchResolver.Resolve(Date, result, [match], [Snapshot(1)]);
        Assert.Same(match, resolution.Match);
        Assert.Equal("stored source ID", resolution.Method);
        Assert.False(resolution.Conflict);
    }

    [Fact]
    public void LegacySnapshotsRetainExactNameMatching()
    {
        var match = Fixture(1, "Hôme", "Away");
        var snapshot = Snapshot(1);
        snapshot.SourceMatchId = null;
        var resolution = ResultMatchResolver.Resolve(Date, Result("Home", "Away"), [match], [snapshot]);
        Assert.Same(match, resolution.Match);
        Assert.False(resolution.Conflict);
    }

    [Fact]
    public void ConflictingOrDuplicateSourceLinksAreNeverApplied()
    {
        var exact = Fixture(1, "Home", "Away");
        var other = Fixture(2, "Other", "Team");
        var conflict = ResultMatchResolver.Resolve(Date, Result("Home", "Away"),
            [exact, other], [Snapshot(2)]);
        Assert.Null(conflict.Match);
        Assert.True(conflict.Conflict);
        var duplicate = ResultMatchResolver.Resolve(Date, Result("Unknown", "Unknown"),
            [exact, other], [Snapshot(1), Snapshot(2)]);
        Assert.Null(duplicate.Match);
        Assert.True(duplicate.Conflict);
    }

    [Fact]
    public void IdsAreScopedToProviderAndMatchDate()
    {
        var match = Fixture(1, "Old", "Names");
        var wrongSource = Snapshot(1);
        wrongSource.Source = "Other";
        Assert.Null(ResultMatchResolver.Resolve(Date, Result("New", "Names"), [match], [wrongSource]).Match);
        match.MatchDate = Date.AddDays(1);
        Assert.Null(ResultMatchResolver.Resolve(Date, Result("Old", "Names"), [match], [Snapshot(1)]).Match);
    }

    [Fact]
    public void SameNamesDoNotOverrideKnownDifferentSourceId()
    {
        var snapshot = Snapshot(1);
        snapshot.SourceMatchId = "forebet:different-fixture";
        var resolution = ResultMatchResolver.Resolve(Date, Result("Home", "Away"),
            [Fixture(1, "Home", "Away")], [snapshot]);
        Assert.True(resolution.Conflict);
        Assert.Null(resolution.Match);
    }

    [Fact]
    public void SimilarNamesAndReversedTeamsCannotSettleResults()
    {
        var match = Fixture(1, "Home", "Away");
        Assert.Null(ResultMatchResolver.Resolve(Date, Result("Home FC", "Away"), [match], []).Match);
        Assert.Null(ResultMatchResolver.Resolve(Date, Result("Away", "Home"), [match], []).Match);
    }

    [Fact]
    public void DiagnosticCandidatesRequireBothSidesAndAdjacentDatesButNeverMerge()
    {
        var target = Fixture(1, "Gimnastica Torrelavega", "UD Ourense");
        var similar = Fixture(2, "Gimnastica T.", "CD Ourense");
        Assert.True(TicketResultDiagnostics.IsCandidate(target, similar));
        Assert.Null(ResultMatchResolver.Resolve(Date, Result(similar.HomeTeam, similar.AwayTeam),
            [target], []).Match);
        similar.MatchDate = Date.AddDays(1);
        Assert.True(TicketResultDiagnostics.IsCandidate(target, similar));
        similar.MatchDate = Date.AddDays(2);
        Assert.False(TicketResultDiagnostics.IsCandidate(target, similar));
        Assert.False(TicketResultDiagnostics.IsCandidate(target, Fixture(3, "Gimnastica T.", "Other Club")));
        Assert.False(TicketResultDiagnostics.IsCandidate(target, Fixture(4, "UD Ourense", "Gimnastica T.")));
    }

    private static Match Fixture(long id, string home, string away) => new()
    {
        Id = id, MatchDate = Date, HomeTeam = home, AwayTeam = away,
        ExternalMatchId = MatchIdentity.BuildExternalMatchId(Date.ToString("yyyy-MM-dd"), home, away)
    };

    private static ExternalMatchResult Result(string home, string away) => new()
    {
        HomeTeam = home, AwayTeam = away, Source = "Forebet", ExternalId = "forebet:123"
    };

    private static PredictionSnapshot Snapshot(long matchId) => new()
    {
        MatchId = matchId, Source = "Forebet", SourceMatchId = "forebet:123"
    };
}
