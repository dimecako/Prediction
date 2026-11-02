using Prediction.Models;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class MatchResultCollectorTests
{
    private static readonly DateOnly Date = new(2026, 10, 23);

    [Fact]
    public void DefaultCollectionExplicitlyDisablesSourcesWithoutVerifiedRegulationScores()
    {
        using var http = new HttpClient();
        var messages = new List<string>();
        var providers = MatchResultCollector.CreateDefaultProviders(http, messages.Add);
        Assert.Equal(["Forebet", "SoccerVista", "OpenFootball"], providers.Select(x => x.Name));
        Assert.DoesNotContain(providers, x => x is FootballResultsOnlineProvider);
        Assert.Contains(messages, x => x.Contains("FootballResultsOnline disabled") &&
            x.Contains("cannot verify regulation-time FT"));
    }

    [Fact]
    public async Task PartialPrimaryResultsStillQueryEveryProviderAndMergeUniqueFixtures()
    {
        var first = new Provider("primary", Row("Home", "Away"));
        var second = new Provider("secondary", Row("Other", "Away"));
        var third = new Provider("third", Row("Third", "Away"));
        var result = await Collect(first, second, third);
        Assert.Equal(3, result.Results.Count);
        Assert.All(new[] { first, second, third }, x => Assert.Equal(1, x.Calls));
        Assert.Equal(0, result.FailedProviders);
        Assert.Equal(0, result.RejectedRows);
        Assert.Equal(3, result.SourceResults.Count);
    }

    [Fact]
    public async Task DuplicateFixtureKeepsPrimaryScoreAndSupplementsMatchingHalfTime()
    {
        var primary = Row("Hôme", "Away");
        var fallback = Row("Home", "Away", 1, 0);
        fallback.Source = "secondary";
        var result = await Collect(new Provider("primary", primary), new Provider("secondary", fallback));
        var row = Assert.Single(result.Results);
        Assert.Equal("primary", row.Source);
        Assert.Equal(1, row.HalfTimeHomeGoals);
        Assert.Equal(0, row.HalfTimeAwayGoals);
        Assert.Equal(0, result.RejectedRows);
        Assert.Equal(2, result.SourceResults.Count);
        Assert.Contains(fallback, result.SourceResults);
    }

    [Fact]
    public async Task ConflictingFinalScoreLeavesBothSourcesUnresolved()
    {
        var conflict = Row("Home", "Away", 1, 0);
        conflict.HomeGoals = 3;
        var result = await Collect(new Provider("primary", Row("Home", "Away")), new Provider("secondary", conflict));
        Assert.Empty(result.Results);
        Assert.Empty(result.SourceResults);
        Assert.Equal(1, result.RejectedRows);
        Assert.DoesNotContain(conflict, result.SourceResults);
    }

    [Fact]
    public async Task ConflictingHalfTimeLeavesFixtureUnresolved()
    {
        var result = await Collect(new Provider("primary", Row("Home", "Away", 1, 0)),
            new Provider("secondary", Row("Home", "Away", 0, 0)));
        Assert.Empty(result.Results);
        Assert.Empty(result.SourceResults);
        Assert.Equal(1, result.RejectedRows);
    }

    [Fact]
    public async Task DifferentNamesAndReversedTeamsAreNotFuzzyMatched()
    {
        var result = await Collect(new Provider("primary", Row("Home", "Away")),
            new Provider("secondary", Row("Home FC", "Away"), Row("Away", "Home")));
        Assert.Equal(3, result.Results.Count);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 0)]
    [InlineData(1, null)]
    public async Task InvalidOrIncompleteHalfTimeIsRejected(int? home, int? away)
    {
        var result = await Collect(new Provider("invalid", Row("Home", "Away", home, away)));
        Assert.Empty(result.Results);
        Assert.Equal(1, result.RejectedRows);
    }

    [Fact]
    public async Task InvalidFinalScoreAndEmptyIdentityAreRejected()
    {
        var invalid = Row("Home", "Away");
        invalid.HomeGoals = -1;
        var result = await Collect(new Provider("invalid", invalid, Row("", "Away")));
        Assert.Empty(result.Results);
        Assert.Equal(2, result.RejectedRows);
    }

    [Fact]
    public async Task UnverifiedPeriodCannotBePersistedEvenWhenAnotherSourceHasTheSameScore()
    {
        var unverified = Row("Home", "Away");
        unverified.IsRegulationTimeVerified = false;
        var messages = new List<string>();
        var result = await MatchResultCollector.CollectAsync(Date,
            [new Provider("unknown-period", unverified)], messages.Add);
        Assert.Empty(result.Results);
        Assert.Empty(result.SourceResults);
        Assert.Equal(1, result.RejectedRows);
        Assert.Contains(messages, x => x.Contains("regulation-time FT not verified"));
    }

    [Fact]
    public async Task FootballResultsOnlineBareScoreDoesNotProveRegulationTime()
    {
        string content = "var scrollercontent = '<table><tr><td>23/10/2026</td>" +
            "<td>Home</td><td>2</td><td>v</td><td>1</td><td>Away</td></tr></table>';";
        using var handler = new ResponseHandler(System.Net.HttpStatusCode.OK, content);
        using var http = new HttpClient(handler);
        var messages = new List<string>();
        var result = await MatchResultCollector.CollectAsync(Date,
            [new FootballResultsOnlineProvider(http)], messages.Add);
        Assert.Empty(result.Results);
        Assert.Empty(result.SourceResults);
        Assert.Equal(1, result.RejectedRows);
        Assert.Contains(messages, x => x.Contains("regulation-time FT not verified"));
    }

    [Theory]
    [InlineData("{\"ft\":[1,1],\"et\":[2,1],\"p\":[5,4]}", true)]
    [InlineData("{\"et\":[2,1],\"p\":[5,4]}", false)]
    [InlineData("[2,1]", false)]
    public async Task OpenFootballUsesOnlyTheExplicitRegulationTimeField(string score, bool expected)
    {
        string content = "{\"matches\":[{\"date\":\"2026-10-23\",\"team1\":\"Home\"," +
            "\"team2\":\"Away\",\"score\":" + score + "}]}";
        using var handler = new ResponseHandler(System.Net.HttpStatusCode.OK, content);
        using var http = new HttpClient(handler);
        var result = await new OpenFootballResultProvider(http).GetResultsAsync(Date);
        Assert.Equal(expected ? 7 : 0, result.Count);
        Assert.All(result, row =>
        {
            Assert.Equal(1, row.HomeGoals);
            Assert.Equal(1, row.AwayGoals);
            Assert.True(row.IsRegulationTimeVerified);
        });
    }

    [Fact]
    public async Task ProviderFailureIsLoggedAndDoesNotDiscardOtherSources()
    {
        var messages = new List<string>();
        var result = await MatchResultCollector.CollectAsync(Date,
            new[] { new Provider("failed") { Failure = new HttpRequestException("offline") },
                new Provider("secondary", Row("Home", "Away")) }, messages.Add);
        Assert.Single(result.Results);
        Assert.Equal(1, result.FailedProviders);
        Assert.Contains(messages, x => x.Contains("failed: offline"));
    }

    [Fact]
    public async Task PartialProviderFailureStillValidatesAndMergesVerifiedResults()
    {
        var supplement = Row("Home", "Away", 1, 0);
        supplement.Source = "partial";
        var invalid = Row("Invalid", "Away");
        invalid.HomeGoals = -1;
        var conflict = Row("Home", "Away");
        conflict.HomeGoals = 9;
        var messages = new List<string>();
        var result = await MatchResultCollector.CollectAsync(Date,
            [new Provider("primary", Row("Home", "Away")),
             new Provider("partial")
             {
                 Failure = new PartialMatchResultException(
                     [supplement, invalid, conflict, Row("Unique", "Away")], ["fixture unavailable"])
             },
             new Provider("last", Row("Last", "Away"))], messages.Add);
        Assert.Equal(1, result.FailedProviders);
        Assert.Equal(2, result.RejectedRows);
        Assert.Equal(2, result.Results.Count);
        Assert.DoesNotContain(result.Results, x => x.HomeTeam == "Home");
        Assert.DoesNotContain(result.SourceResults, x => x.HomeTeam == "Home");
        Assert.Contains(messages, x => x.Contains("fixture failed: fixture unavailable"));
    }

    [Fact]
    public async Task TimeoutContinuesToNextProviderButCallerCancellationPropagates()
    {
        var timeout = await Collect(new Provider("timeout") { Failure = new TaskCanceledException("timeout") },
            new Provider("secondary", Row("Home", "Away")));
        Assert.Single(timeout.Results);
        Assert.Equal(1, timeout.FailedProviders);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MatchResultCollector.CollectAsync(Date, new[] { new Provider("primary") }, _ => { }, cancellation.Token));
    }

    [Fact]
    public async Task LaterOrLargerScoreCannotResolveAnEarlierConflict()
    {
        var larger = Row("Home", "Away");
        larger.HomeGoals = 5;
        var messages = new List<string>();
        var result = await MatchResultCollector.CollectAsync(Date,
            [new Provider("first", Row("Home", "Away")),
             new Provider("second", larger),
             new Provider("latest", larger, Row("Other", "Away"))], messages.Add);
        Assert.Equal("Other", Assert.Single(result.Results).HomeTeam);
        Assert.Equal("Other", Assert.Single(result.SourceResults).HomeTeam);
        Assert.Equal(2, result.RejectedRows);
        Assert.Contains(messages, x => x.Contains("left unresolved"));
    }

    [Fact]
    public async Task MissingOpenFootballSeasonIsExplicitlyEmptyNotAFailedRequest()
    {
        using var handler = new ResponseHandler(System.Net.HttpStatusCode.NotFound, "");
        using var http = new HttpClient(handler);
        var result = await Collect(new OpenFootballResultProvider(http));
        Assert.Empty(result.Results);
        Assert.Equal(0, result.FailedProviders);
        Assert.Equal(7, handler.Calls);
    }

    [Theory]
    [InlineData(503, "unavailable")]
    [InlineData(200, "not-json")]
    [InlineData(200, "{}")]
    [InlineData(200, "{\"matches\":{}}")]
    [InlineData(200, "[]")]
    public async Task OpenFootballHttpAndSchemaFailuresDoNotMasqueradeAsEmptySuccess(int status, string content)
    {
        using var handler = new ResponseHandler((System.Net.HttpStatusCode)status, content);
        using var http = new HttpClient(handler);
        var result = await Collect(new OpenFootballResultProvider(http),
            new Provider("secondary", Row("Home", "Away")));
        Assert.Equal(1, result.FailedProviders);
        Assert.Single(result.Results);
    }

    private static Task<CollectedMatchResults> Collect(params IMatchResultProvider[] providers) =>
        MatchResultCollector.CollectAsync(Date, providers, _ => { });

    private static ExternalMatchResult Row(string home, string away, int? htHome = null, int? htAway = null) => new()
    {
        HomeTeam = home, AwayTeam = away, HomeGoals = 2, AwayGoals = 1,
        IsRegulationTimeVerified = true,
        HalfTimeHomeGoals = htHome, HalfTimeAwayGoals = htAway, Source = "primary"
    };

    private sealed class Provider(string name, params ExternalMatchResult[] rows) : IMatchResultProvider
    {
        public string Name => name;
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
            DateOnly date, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Date, date);
            Calls++;
            return Failure == null ? Task.FromResult<IReadOnlyList<ExternalMatchResult>>(rows) :
                Task.FromException<IReadOnlyList<ExternalMatchResult>>(Failure);
        }
    }

    private sealed class ResponseHandler(System.Net.HttpStatusCode status, string content) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
        }
    }
}
