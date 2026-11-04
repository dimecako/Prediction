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
    public void NeitherExactNamesNorSourceIdCanAuthorizeAnUnverifiedScorePeriod()
    {
        var result = Result("Home", "Away");
        result.IsRegulationTimeVerified = false;
        var resolution = ResultMatchResolver.Resolve(Date, result,
            [Fixture(1, "Home", "Away")], [Snapshot(1)]);
        Assert.True(resolution.Conflict);
        Assert.Empty(resolution.Matches);
        Assert.Contains("regulation-time FT not verified", resolution.Method);
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
    public void SharedSourceIdResolvesBothVerifiedAliases()
    {
        var first = Fixture(1, "JK Tabasalu", "Levadia U19");
        var second = Fixture(2, "Tabasalu", "Levadia U19");
        var resolution = ResultMatchResolver.Resolve(Date, Result("Tabasalu", "Levadia U19"),
            [first, second], [Snapshot(1), Snapshot(2)]);
        Assert.False(resolution.Conflict);
        Assert.Equal(new[] { first, second }, resolution.Matches);
    }

    [Fact]
    public void SharedSourceIdWithDifferentOtherProviderIdsIsRejected()
    {
        var other1 = Snapshot(1);
        other1.Source = "SoccerVista";
        other1.SourceMatchId = "soccervista:a";
        var other2 = Snapshot(2);
        other2.Source = "SoccerVista";
        other2.SourceMatchId = "soccervista:b";
        var resolution = ResultMatchResolver.Resolve(Date, Result("Tabasalu", "Levadia"),
            [Fixture(1, "JK Tabasalu", "Levadia"), Fixture(2, "Tabasalu", "Levadia")],
            [Snapshot(1), Snapshot(2), other1, other2]);
        Assert.True(resolution.Conflict);
        Assert.Empty(resolution.Matches);
    }

    [Fact]
    public void SharedSourceIdCannotOverwriteConflictingStoredScore()
    {
        var first = Fixture(1, "JK Tabasalu", "Levadia");
        first.Result = new() { HomeGoals = 3, AwayGoals = 1 };
        var resolution = ResultMatchResolver.Resolve(Date, Result("Tabasalu", "Levadia"),
            [first, Fixture(2, "Tabasalu", "Levadia")], [Snapshot(1), Snapshot(2)]);
        Assert.True(resolution.Conflict);
        Assert.Empty(resolution.Matches);
    }

    [Fact]
    public void SharedSourceIdRejectsStoredHalfTimeDisagreementEvenForFtOnlyProvider()
    {
        var first = Fixture(1, "JK Tabasalu", "Levadia");
        var second = Fixture(2, "Tabasalu", "Levadia");
        first.Result = new() { HalfTimeHomeGoals = 0, HalfTimeAwayGoals = 0 };
        second.Result = new() { HalfTimeHomeGoals = 1, HalfTimeAwayGoals = 0 };
        var resolution = ResultMatchResolver.Resolve(Date, Result("Tabasalu", "Levadia"),
            [first, second], [Snapshot(1), Snapshot(2)]);
        Assert.True(resolution.Conflict);
        Assert.Empty(resolution.Matches);
    }

    [Fact]
    public void SharedSourceIdCannotIgnoreUnlinkedExactNameRecord()
    {
        var resolution = ResultMatchResolver.Resolve(Date, Result("Tabasalu", "Levadia"),
            [Fixture(1, "JK Tabasalu", "Levadia"), Fixture(2, "FC Tabasalu", "Levadia"),
             Fixture(3, "Tabasalu", "Levadia")], [Snapshot(1), Snapshot(2)]);
        Assert.True(resolution.Conflict);
        Assert.Empty(resolution.Matches);
    }

    [Theory]
    [InlineData("Tabasalu U19", "Tabasalu U21")]
    [InlineData("Tabasalu W", "Tabasalu")]
    [InlineData("Levadia", "Tabasalu")]
    [InlineData("Manchester United", "Manchester City")]
    public void SharedSourceIdDoesNotOverrideDifferentTeams(string firstHome, string secondHome)
    {
        var resolution = ResultMatchResolver.Resolve(Date, Result(secondHome, "Away"),
            [Fixture(1, firstHome, "Away"), Fixture(2, secondHome, "Away")],
            [Snapshot(1), Snapshot(2)]);
        Assert.True(resolution.Conflict);
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

    [Fact]
    public void ExactAndSourceRowsNeedSharedProviderProofNotJustSimilarNames()
    {
        var exact = Fixture(1, "Tabasalu", "Levadia");
        var alias = Fixture(2, "JK Tabasalu", "Levadia");
        var incoming = Result("Tabasalu", "Levadia");
        var links = new[] { Snapshot(2) };
        var unresolved = ResultMatchResolver.Resolve(Date, incoming, [exact, alias], links);
        Assert.True(unresolved.Conflict);
        Assert.Equal(new long[] { 1, 2 }, unresolved.Candidates.Select(x => x.Id));
        PredictionSnapshot[] proof =
        [
            Snapshot(2),
            new() { MatchId = 1, Source = "SoccerVista", SourceMatchId = "soccervista:shared" },
            new() { MatchId = 2, Source = "SoccerVista", SourceMatchId = "soccervista:shared" }
        ];
        var verified = ResultMatchResolver.Resolve(Date, incoming, [exact, alias], proof);
        Assert.False(verified.Conflict);
        Assert.Equal(new long[] { 1, 2 }, verified.Matches.Select(x => x.Id));
        alias.Result = new() { HomeGoals = 1, AwayGoals = 0 };
        Assert.True(ResultMatchResolver.Resolve(Date, incoming, [exact, alias], proof).Conflict);
        alias.Result = null;
        exact.KickoffUtc = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        alias.KickoffUtc = exact.KickoffUtc.Value.AddHours(1);
        Assert.True(ResultMatchResolver.Resolve(Date, incoming, [exact, alias], proof).Conflict);
        alias.KickoffUtc = exact.KickoffUtc;
        incoming.KickoffUtc = exact.KickoffUtc.Value.AddHours(2);
        Assert.True(ResultMatchResolver.Resolve(Date, incoming, [exact, alias], proof).Conflict);
    }

    [Fact]
    public void ConflictDiagnosticsShowBothRowsAndTheirEvidenceWithoutControlCharacters()
    {
        var exact = Fixture(1, "Home", "Away");
        exact.League = "League\nInjected";
        exact.KickoffUtc = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        exact.Result = new() { HomeGoals = 2, AwayGoals = 1,
            HalfTimeHomeGoals = 1, HalfTimeAwayGoals = 0 };
        var other = Fixture(2, "Other", "Team");
        var incoming = Result("Home", "Away");
        var resolution = ResultMatchResolver.Resolve(Date, incoming, [exact, other], [Snapshot(2)]);
        var lines = new List<string>();
        MatchResultPersister.WriteConflictDiagnostics(Date, incoming, resolution.Candidates,
            [Snapshot(2)], lines.Add);
        Assert.Contains(lines, x => x.Contains("DB #1") && x.Contains("FT=2-1 HT=1-0") &&
            x.Contains("2026-10-28T12:00:00") && x.Contains("League Injected"));
        Assert.Contains(lines, x => x.Contains("DB #2") && x.Contains("Forebet:forebet:123"));
        Assert.All(lines, x => Assert.DoesNotContain('\n', x));
        Assert.Equal(2, exact.Result.HomeGoals);
    }

    [Fact]
    public void FreshDatedProviderFixtureCorroboratesExactNamesAndUniqueProviderLink()
    {
        var exact = Fixture(1, "Birmingham", "Millwall");
        var linked = Fixture(2, "Birmingham City", "Millwall");
        exact.KickoffUtc = linked.KickoffUtc = Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var incoming = Result("Birmingham", "Millwall");
        incoming.KickoffUtc = Date.ToDateTime(new(20, 0), DateTimeKind.Utc);
        var resolution = ResultMatchResolver.Resolve(Date, incoming, [exact, linked], [Snapshot(2)]);
        Assert.False(resolution.Conflict);
        Assert.Equal(2, resolution.Matches.Count);
        Assert.Contains("verified live", resolution.Method);
        Assert.Null(exact.Result);
        Assert.Equal("Birmingham City", linked.HomeTeam);
    }

    [Theory]
    [InlineData("APR", "APR FC", "Gasogi United", "Gasogi United")]
    [InlineData("Dorking Wanderers", "Dorking Wanderers", "Hampton & Richmond", "Hampton FC")]
    public void LiveProviderEvidenceAllowsClubDesignatorsWithoutChangingLegacyDuplicateRules(
        string home, string storedHome, string away, string storedAway)
    {
        var exact = Fixture(1, home, away);
        var linked = Fixture(2, storedHome, storedAway);
        var incoming = Result(home, away);
        incoming.KickoffUtc = Date.ToDateTime(new(20, 0), DateTimeKind.Utc);
        Assert.False(ResultMatchResolver.Resolve(Date, incoming, [exact, linked], [Snapshot(2)]).Conflict);
        incoming.KickoffUtc = null;
        Assert.True(ResultMatchResolver.Resolve(Date, incoming, [exact, linked], [Snapshot(2)]).Conflict);
        Assert.False(ResultMatchResolver.AreVerifiedDuplicates([exact, linked], [Snapshot(2)], "Forebet", "forebet:123"));
    }

    [Theory]
    [InlineData("different-id")]
    [InlineData("different-score")]
    [InlineData("different-ht")]
    [InlineData("different-kickoff")]
    [InlineData("wrong-day")]
    [InlineData("different-squad")]
    [InlineData("unverified")]
    public void LiveExactFixtureEvidenceNeverOverridesContradictions(string conflict)
    {
        var exact = Fixture(1, "Birmingham", "Millwall");
        var linked = Fixture(2, "Birmingham City", "Millwall");
        var incoming = Result("Birmingham", "Millwall");
        incoming.KickoffUtc = Date.ToDateTime(new(20, 0), DateTimeKind.Utc);
        var snapshots = new List<PredictionSnapshot> { Snapshot(2) };
        if (conflict == "different-id")
            snapshots.Add(new() { MatchId = 1, Source = "Forebet", SourceMatchId = "other" });
        if (conflict == "different-score")
            linked.Result = new() { HomeGoals = 1, AwayGoals = 0 };
        if (conflict == "different-ht")
        {
            exact.Result = new() { HalfTimeHomeGoals = 0, HalfTimeAwayGoals = 0 };
            linked.Result = new() { HalfTimeHomeGoals = 1, HalfTimeAwayGoals = 0 };
        }
        if (conflict == "different-kickoff")
            exact.KickoffUtc = incoming.KickoffUtc.Value.AddHours(1);
        if (conflict == "wrong-day")
            incoming.KickoffUtc = incoming.KickoffUtc.Value.AddDays(1);
        if (conflict == "different-squad")
            linked.HomeTeam = "Birmingham City U19";
        if (conflict == "unverified")
            incoming.IsRegulationTimeVerified = false;
        Assert.True(ResultMatchResolver.Resolve(Date, incoming, [exact, linked], snapshots).Conflict);
    }

    private static Match Fixture(long id, string home, string away) => new()
    {
        Id = id, MatchDate = Date, HomeTeam = home, AwayTeam = away,
        ExternalMatchId = MatchIdentity.BuildExternalMatchId(Date.ToString("yyyy-MM-dd"), home, away)
    };

    private static ExternalMatchResult Result(string home, string away) => new()
    {
        HomeTeam = home, AwayTeam = away, Source = "Forebet", ExternalId = "forebet:123",
        IsRegulationTimeVerified = true
    };

    private static PredictionSnapshot Snapshot(long matchId) => new()
    {
        MatchId = matchId, Source = "Forebet", SourceMatchId = "forebet:123"
    };
}
