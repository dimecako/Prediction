using Prediction.Models;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class MatchResultCollectorTests
{
    private static readonly DateOnly Date = new(2026, 10, 23);

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
    }

    [Fact]
    public async Task ConflictingFinalScoreCannotSupplyHalfTimeOrReplaceFirstSource()
    {
        var conflict = Row("Home", "Away", 1, 0);
        conflict.HomeGoals = 3;
        var result = await Collect(new Provider("primary", Row("Home", "Away")), new Provider("secondary", conflict));
        var row = Assert.Single(result.Results);
        Assert.Equal(2, row.HomeGoals);
        Assert.Null(row.HalfTimeHomeGoals);
        Assert.Equal(1, result.RejectedRows);
    }

    [Fact]
    public async Task ConflictingHalfTimeKeepsFirstExplicitScore()
    {
        var result = await Collect(new Provider("primary", Row("Home", "Away", 1, 0)),
            new Provider("secondary", Row("Home", "Away", 0, 0)));
        Assert.Equal(1, Assert.Single(result.Results).HalfTimeHomeGoals);
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
