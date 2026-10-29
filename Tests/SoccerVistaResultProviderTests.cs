using System.Globalization;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prediction.Entities;
using Prediction.Models;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class SoccerVistaResultProviderTests
{
    private static readonly DateOnly Date = new(2026, 10, 29);

    [Theory]
    [InlineData("5:1", 5, 1)]
    [InlineData("0:0", 0, 0)]
    public async Task ReadsVerifiedActualFinalScoreNeverPredictionOrInferredHalfTime(string score, int home, int away)
    {
        var row = Row();
        row["score"] = score;
        var detail = Detail();
        detail["finalScore"] = score;
        using var handler = new Handler(Feed(row), detail.ToString());
        using var http = new HttpClient(handler);

        var result = Assert.Single(await new SoccerVistaResultProvider(http).GetResultsAsync(Date));

        Assert.Equal(home, result.HomeGoals);
        Assert.Equal(away, result.AwayGoals);
        Assert.Null(result.HalfTimeHomeGoals);
        Assert.Null(result.HalfTimeAwayGoals);
        Assert.Equal("SoccerVista", result.Source);
        Assert.Equal("soccervista:As77hb15", result.ExternalId);
        Assert.Equal(new DateTime(2026, 10, 29, 17, 30, 0, DateTimeKind.Utc), result.KickoffUtc);
        Assert.Equal(2, handler.Paths.Count);
        Assert.Equal("/events/by/date/29-10-2026/", handler.Paths[0]);
        Assert.Equal("/events/As77hb15/live-updates/", handler.Paths[1]);
    }

    [Theory]
    [InlineData("isFinished", false)]
    [InlineData("isLive", true)]
    [InlineData("isScheduled", true)]
    [InlineData("isPaused", true)]
    [InlineData("isPostponed", true)]
    [InlineData("isCancelled", true)]
    public async Task UnfinishedOrUnavailableMatchesAreNeverCollected(string field, bool value)
    {
        var row = Row();
        row[field] = value;
        using var handler = new Handler(Feed(row), "");
        using var http = new HttpClient(handler);
        Assert.Empty(await new SoccerVistaResultProvider(http).GetResultsAsync(Date));
        Assert.Single(handler.Paths);
    }

    [Theory]
    [InlineData("AET", "isFinished", true)]
    [InlineData("AfP", "isFinished", true)]
    [InlineData("PEN", "isFinished", true)]
    [InlineData("FIN", "isPenaltyShootout", true)]
    [InlineData("FIN", "isHalfTime", true)]
    [InlineData("FIN", "isLive", true)]
    [InlineData("FIN", "isFinished", false)]
    public async Task DetailMustConfirmRegulationTimeFinishedStatus(string status, string field, bool value)
    {
        var detail = Detail();
        detail["status"]!["short_name"] = status;
        detail[field] = value;
        using var handler = new Handler(Feed(Row()), detail.ToString());
        using var http = new HttpClient(handler);
        Assert.Empty(await new SoccerVistaResultProvider(http).GetResultsAsync(Date));
    }

    [Theory]
    [InlineData("id", "other-fixture")]
    [InlineData("homeParticipant.name", "Different Home")]
    [InlineData("awayParticipant.name", "Different Away")]
    [InlineData("date", "Wednesday 28 October 2026")]
    [InlineData("date", "not-a-date")]
    [InlineData("finalScore", "2:1")]
    [InlineData("finalScore", "")]
    [InlineData("status.short_name", "")]
    public async Task ConflictingIdentityDateOrScoreIsAnExplicitProviderFailure(string path, string value)
    {
        var detail = Detail();
        detail.SelectToken(path)!.Replace(new JValue(value));
        await AssertFailed(Feed(Row()), detail.ToString());
    }

    [Theory]
    [InlineData("score", "-1:0")]
    [InlineData("score", "5:1 aet")]
    [InlineData("score", "")]
    [InlineData("homeTeam", "")]
    [InlineData("awayTeam", "")]
    [InlineData("id", "")]
    [InlineData("isFinished", "true")]
    [InlineData("timeStart", "invalid")]
    public async Task InvalidDailyFieldsDoNotBecomeResults(string field, string value)
    {
        var row = Row();
        row[field] = value;
        await AssertFailed(Feed(row), Detail().ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{}]")]
    [InlineData("[{\"events\":{}}]")]
    [InlineData("[{\"events\":[null]}]")]
    [InlineData("<html>challenge</html>")]
    public async Task InvalidFeedIsNotReportedAsAnEmptySuccess(string feed) =>
        await AssertFailed(feed, Detail().ToString());

    [Fact]
    public async Task FinishedWithoutActualScoreNeverUsesPrediction()
    {
        var row = Row();
        row["score"] = JValue.CreateNull();
        using var handler = new Handler(Feed(row), "");
        using var http = new HttpClient(handler);
        Assert.Empty(await new SoccerVistaResultProvider(http).GetResultsAsync(Date));
        Assert.Single(handler.Paths);
    }

    [Fact]
    public async Task EmptyFeedAndEmptyEventsAreValid()
    {
        foreach (string feed in new[] { "[]", "[{\"events\":[]}]" })
        {
            using var handler = new Handler(feed, "");
            using var http = new HttpClient(handler);
            Assert.Empty(await new SoccerVistaResultProvider(http).GetResultsAsync(Date));
        }
    }

    [Fact]
    public async Task MissingDetailStatusAndDuplicateIdsAreExplicitFailures()
    {
        var detail = Detail();
        detail.Remove("status");
        await AssertFailed(Feed(Row()), detail.ToString());
        await AssertFailed(Feed(Row(), Row()), Detail().ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpFailureIsReportedWithoutDiscardingOtherProviders(bool detailFailure)
    {
        using var handler = new Handler(Feed(Row()), Detail().ToString())
        {
            FailurePath = detailFailure ? "/events/As77hb15/live-updates/" : "/events/by/date/29-10-2026/"
        };
        using var http = new HttpClient(handler);
        var collected = await MatchResultCollector.CollectAsync(Date,
            [new SoccerVistaResultProvider(http), new OtherProvider()], _ => { });
        Assert.Equal(1, collected.FailedProviders);
        Assert.Equal("Other", Assert.Single(collected.Results).Source);
    }

    [Fact]
    public async Task DetailApi500FallsBackToVerifiedPageData()
    {
        using var handler = new Handler(Feed(Row()), Detail().ToString())
        {
            FailurePath = "/events/As77hb15/live-updates/",
            PageContent = EventPage(Detail())
        };
        using var http = new HttpClient(handler);
        var collected = await MatchResultCollector.CollectAsync(Date,
            [new SoccerVistaResultProvider(http)], _ => { });
        var result = Assert.Single(collected.Results);
        Assert.Equal(5, result.HomeGoals);
        Assert.Equal(1, result.AwayGoals);
        Assert.Null(result.HalfTimeHomeGoals);
        Assert.Equal(0, collected.FailedProviders);
        Assert.Equal("/event/frosinone-lecce/As77hb15/", handler.Paths[2]);
    }

    [Theory]
    [InlineData("id", "different-id")]
    [InlineData("homeParticipant.name", "Different Home")]
    [InlineData("awayParticipant.name", "Different Away")]
    [InlineData("date", "Wednesday 28 October 2026")]
    [InlineData("finalScore", "2:1")]
    public async Task PageFallbackCannotBypassFixtureVerification(string path, string value)
    {
        var detail = Detail();
        detail.SelectToken(path)!.Replace(new JValue(value));
        using var handler = new Handler(Feed(Row()), Detail().ToString())
        {
            FailurePath = "/events/As77hb15/live-updates/",
            PageContent = EventPage(detail)
        };
        using var http = new HttpClient(handler);
        var collected = await MatchResultCollector.CollectAsync(Date,
            [new SoccerVistaResultProvider(http)], _ => { });
        Assert.Empty(collected.Results);
        Assert.Equal(1, collected.FailedProviders);
    }

    [Theory]
    [InlineData("AET")]
    [InlineData("AfP")]
    public async Task PageFallbackDoesNotAcceptExtraTimeOrPenalties(string status)
    {
        var detail = Detail();
        detail["status"]!["short_name"] = status;
        using var handler = new Handler(Feed(Row()), Detail().ToString())
        {
            FailurePath = "/events/As77hb15/live-updates/",
            PageContent = EventPage(detail)
        };
        using var http = new HttpClient(handler);
        Assert.Empty(await new SoccerVistaResultProvider(http).GetResultsAsync(Date));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneUnavailableFixtureRetainsResultsBeforeAndAfterAndReportsFailure(bool timeout)
    {
        var first = Row();
        var broken = Row();
        broken["id"] = "broken";
        broken["homeTeam"] = "Broken Home";
        var last = Row();
        last["id"] = "last";
        last["homeTeam"] = "Last Home";
        var lastDetail = Detail();
        lastDetail["id"] = "last";
        lastDetail["homeParticipant"]!["name"] = "Last Home";
        using var handler = new Handler(Feed(first, broken, last), Detail().ToString())
        {
            FailurePath = "/events/broken/live-updates/",
            Timeout = timeout
        };
        handler.Responses["/events/last/live-updates/"] = lastDetail.ToString();
        using var http = new HttpClient(handler);
        var logs = new List<string>();
        var collected = await MatchResultCollector.CollectAsync(Date,
            [new SoccerVistaResultProvider(http), new OtherProvider()], logs.Add);
        Assert.Equal(1, collected.FailedProviders);
        Assert.Equal(3, collected.Results.Count);
        Assert.Contains(collected.Results, x => x.ExternalId == "soccervista:As77hb15");
        Assert.Contains(collected.Results, x => x.ExternalId == "soccervista:last");
        Assert.DoesNotContain(collected.Results, x => x.ExternalId == "soccervista:broken");
        Assert.Contains(logs, x => x.Contains("fixture failed: broken Broken Home"));
        Assert.Contains(logs, x => x.Contains("retaining 2 verified results"));
    }

    [Fact]
    public async Task CancellationDuringDetailFetchDoesNotFallBackOrReturnPartialSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler(Feed(Row()), Detail().ToString())
        {
            CancelDuringDetail = cancellation
        };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MatchResultCollector.CollectAsync(Date, [new SoccerVistaResultProvider(http)],
                _ => { }, cancellation.Token));
        Assert.Equal(2, handler.Paths.Count);
    }

    [Theory]
    [InlineData("<html>challenge</html>")]
    [InlineData("<script>getEventOdds(JSON.parse('null'))</script>")]
    [InlineData("<script>getEventOdds(JSON.parse('bad-json'))</script>")]
    public async Task InvalidFallbackPageIsAnExplicitFailure(string page)
    {
        using var handler = new Handler(Feed(Row()), Detail().ToString())
        {
            FailurePath = "/events/As77hb15/live-updates/",
            PageContent = page
        };
        using var http = new HttpClient(handler);
        var collected = await MatchResultCollector.CollectAsync(Date,
            [new SoccerVistaResultProvider(http)], _ => { });
        Assert.Empty(collected.Results);
        Assert.Equal(1, collected.FailedProviders);
    }

    [Fact]
    public async Task ResultsCanResolveBySourceIdWithoutFuzzyMatching()
    {
        using var handler = new Handler(Feed(Row()), Detail().ToString());
        using var http = new HttpClient(handler);
        var result = Assert.Single(await new SoccerVistaResultProvider(http).GetResultsAsync(Date));
        var match = new Match { Id = 1, MatchDate = Date, HomeTeam = "Old Home", AwayTeam = "Old Away" };
        var snapshot = new PredictionSnapshot
        {
            MatchId = 1, Source = "SoccerVista", SourceMatchId = result.ExternalId
        };
        Assert.Same(match, ResultMatchResolver.Resolve(Date, result, [match], [snapshot]).Match);
        Assert.Null(ResultMatchResolver.Resolve(Date, result, [match], []).Match);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var handler = new Handler(Feed(Row()), Detail().ToString());
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SoccerVistaResultProvider(http).GetResultsAsync(Date, cancellation.Token));
    }

    private static async Task AssertFailed(string feed, string detail)
    {
        using var handler = new Handler(feed, detail);
        using var http = new HttpClient(handler);
        var logs = new List<string>();
        var collected = await MatchResultCollector.CollectAsync(Date,
            [new SoccerVistaResultProvider(http)], logs.Add);
        Assert.Empty(collected.Results);
        Assert.Equal(1, collected.FailedProviders);
        Assert.Contains(logs, x => x.Contains("SoccerVista failed:"));
    }

    private static string Feed(params JObject[] rows) =>
        new JArray(new JObject { ["events"] = new JArray(rows) }).ToString();

    private static string EventPage(JObject detail)
    {
        string literal = JsonConvert.SerializeObject(detail.ToString(Formatting.None));
        string payload = literal[1..^1].Replace("\\\"", "\\u0022").Replace("'", "\\u0027");
        return $"<html><script>getEventOdds(JSON.parse('{payload}'));</script></html>";
    }

    private static JObject Row() => new()
    {
        ["id"] = "As77hb15", ["homeTeam"] = "Frosinone", ["awayTeam"] = "Lecce",
        ["homeTeamUrl"] = "frosinone", ["awayTeamUrl"] = "lecce",
        ["score"] = "5:1", ["predictionScore"] = "2:1", ["timeStart"] = 1793295000L,
        ["isFinished"] = true, ["isLive"] = false, ["isScheduled"] = false,
        ["isPaused"] = false, ["isPostponed"] = false, ["isCancelled"] = false
    };

    private static JObject Detail() => new()
    {
        ["id"] = "As77hb15",
        ["homeParticipant"] = new JObject { ["name"] = "Frosinone" },
        ["awayParticipant"] = new JObject { ["name"] = "Lecce" },
        ["date"] = Date.ToString("dddd dd MMMM yyyy", CultureInfo.InvariantCulture),
        ["finalScore"] = "5:1",
        ["status"] = new JObject { ["short_name"] = "FIN" },
        ["isFinished"] = true, ["isLive"] = false, ["isScheduled"] = false,
        ["isPaused"] = false, ["isPostponed"] = false, ["isCancelled"] = false,
        ["isPenaltyShootout"] = false, ["isHalfTime"] = false
    };

    private sealed class Handler(string feed, string detail) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string? FailurePath { get; init; }
        public bool Timeout { get; init; }
        public string? PageContent { get; init; }
        public CancellationTokenSource? CancelDuringDetail { get; init; }
        public Dictionary<string, string> Responses { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path.EndsWith("/live-updates/") && CancelDuringDetail != null)
            {
                CancelDuringDetail.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (path == FailurePath && Timeout)
                throw new TaskCanceledException("detail request timed out");
            bool isPage = path.StartsWith("/event/");
            string content = Responses.TryGetValue(path, out var overrideContent) ? overrideContent :
                isPage ? PageContent ?? "" :
                path.StartsWith("/events/by/date/") ? feed : detail;
            return Task.FromResult(new HttpResponseMessage(path == FailurePath
                ? HttpStatusCode.InternalServerError :
                isPage && PageContent == null ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class OtherProvider : IMatchResultProvider
    {
        public string Name => "Other";
        public Task<IReadOnlyList<ExternalMatchResult>> GetResultsAsync(
            DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExternalMatchResult>>(
                [new ExternalMatchResult { HomeTeam = "Other", AwayTeam = "Team", Source = Name }]);
    }
}
