using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class GitHubDailyWorkflowClientTests
{
    private static readonly DateOnly Date = new(2026, 11, 9);

    [Fact]
    public async Task MissingTokenReportsUnconfiguredWithoutCallingGitHub()
    {
        int calls = 0;
        var client = Client(new Dictionary<string, string?>(), _ =>
        {
            calls++;
            return new(HttpStatusCode.OK);
        });

        var status = await client.GetStatusAsync(Date, CancellationToken.None);

        Assert.False(status.Configured);
        Assert.False(status.CanRetry);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task StatusIncludesDelayedScheduledRunsAndUsesCheckpointAttemptCount()
    {
        var client = StatusClient("""
            {"workflow_runs":[
              {"event":"schedule","status":"completed","conclusion":"failure","created_at":"2026-11-09T05:07:00Z","html_url":"https://github.com/run/early","run_attempt":1},
              {"event":"schedule","status":"completed","conclusion":"success","created_at":"2026-11-09T06:07:00Z","html_url":"https://github.com/run/first","run_attempt":1},
              {"event":"workflow_dispatch","status":"completed","conclusion":"failure","created_at":"2026-11-09T07:00:00Z","html_url":"https://github.com/run/manual","run_attempt":1},
              {"event":"schedule","status":"completed","conclusion":"failure","created_at":"2026-11-09T09:07:00Z","html_url":"https://github.com/run/late","run_attempt":1}
            ]}
            """, attempts: 2);

        var status = await client.GetStatusAsync(Date, CancellationToken.None);

        Assert.Equal("failed", status.State);
        Assert.True(status.CanRetry);
        Assert.Equal(2, status.Attempts);
        Assert.Equal("https://github.com/run/late", status.RunUrl);
    }

    [Fact]
    public async Task ThreeFailedAttemptsDisableManualRetry()
    {
        var client = StatusClient("""
            {"workflow_runs":[
              {"event":"schedule","status":"completed","conclusion":"failure","created_at":"2026-11-09T06:07:00Z","html_url":"https://github.com/run/one","run_attempt":1},
              {"event":"workflow_dispatch","status":"completed","conclusion":"failure","created_at":"2026-11-09T07:07:00Z","html_url":"https://github.com/run/two","run_attempt":1},
              {"event":"schedule","status":"completed","conclusion":"failure","created_at":"2026-11-09T08:07:00Z","html_url":"https://github.com/run/three","run_attempt":1}
            ]}
            """, attempts: 3);

        var status = await client.GetStatusAsync(Date, CancellationToken.None);

        Assert.Equal("retry-limit", status.State);
        Assert.False(status.CanRetry);
        Assert.Equal(3, status.Attempts);
    }

    [Fact]
    public async Task GreenSkippedWorkflowDoesNotCountAsSuccessOrUseAnAttempt()
    {
        var client = StatusClient("""
            {"workflow_runs":[
              {"event":"schedule","status":"completed","conclusion":"success","created_at":"2026-11-09T12:38:00Z","html_url":"https://github.com/run/skipped","run_attempt":1}
            ]}
            """);

        var status = await client.GetStatusAsync(Date, CancellationToken.None);

        Assert.Equal("missed", status.State);
        Assert.True(status.CanRetry);
        Assert.Equal(0, status.Attempts);
    }

    [Fact]
    public async Task DelayedActiveRunBlocksManualDispatchEvenAfterTheMorningWindow()
    {
        var client = StatusClient("""
            {"workflow_runs":[
              {"event":"workflow_dispatch","status":"completed","conclusion":"failure","created_at":"2026-11-09T16:01:00Z","html_url":"https://github.com/run/failed","run_attempt":1},
              {"event":"schedule","status":"in_progress","conclusion":null,"created_at":"2026-11-09T14:04:00Z","html_url":"https://github.com/run/active","run_attempt":1}
            ]}
            """, attempts: 2);

        var status = await client.GetStatusAsync(Date, CancellationToken.None);

        Assert.Equal("running", status.State);
        Assert.False(status.CanRetry);
        Assert.Equal("https://github.com/run/active", status.RunUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistedLimitBlocksRetryEvenWithoutVisibleMorningRuns(bool reservationOnly)
    {
        var client = StatusClient("""{"workflow_runs":[]}""", attempts: 3,
            reservationOnly: reservationOnly);

        var status = await client.GetStatusAsync(Date, CancellationToken.None);

        Assert.Equal("retry-limit", status.State);
        Assert.False(status.CanRetry);
        Assert.Equal(3, status.Attempts);
    }

    [Fact]
    public async Task VerifiedDateSuccessMarkerPreventsRetryAfterLaterFailedOrSkippedRuns()
    {
        var client = StatusClient("""
            {"workflow_runs":[
              {"event":"workflow_dispatch","status":"completed","conclusion":"failure","created_at":"2026-11-09T16:01:00Z","html_url":"https://github.com/run/failed","run_attempt":1}
            ]}
            """, attempts: 2, successMarker: true);

        var status = await client.GetStatusAsync(Date, CancellationToken.None);

        Assert.Equal("succeeded", status.State);
        Assert.False(status.CanRetry);
    }

    [Theory]
    [InlineData(-1, "2026-11-09")]
    [InlineData(1, "2026-11-08")]
    public async Task InvalidCheckpointDoesNotSilentlyEnableManualRetry(int attempts, string checkpointDate)
    {
        var client = StatusClient("""{"workflow_runs":[]}""", attempts, checkpointDate: checkpointDate);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetStatusAsync(Date, CancellationToken.None));
    }

    [Fact]
    public async Task GitHubArtifactFailureIsSurfacedInsteadOfEnablingRetry()
    {
        var client = Client(TokenConfiguration(), request =>
            request.RequestUri!.AbsolutePath.EndsWith("/artifacts")
                ? new(HttpStatusCode.Forbidden)
                : JsonResponse("""{"workflow_runs":[]}"""));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetStatusAsync(Date, CancellationToken.None));
    }

    [Fact]
    public async Task DispatchTargetsMainAndRequestsTheDailyWorkflow()
    {
        HttpRequestMessage? captured = null;
        string? payload = null;
        var client = Client(TokenConfiguration(), request =>
        {
            captured = request;
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new(HttpStatusCode.NoContent);
        });

        await client.DispatchAsync(Date, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("repos/dimecako/Prediction/actions/workflows/daily-run.yml/dispatches",
            captured.RequestUri!.PathAndQuery.TrimStart('/'));
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", captured.Headers.Authorization.Parameter);
        Assert.Contains("\"ref\":\"main\"", payload);
        Assert.Contains("\"target_date\":\"2026-11-09\"", payload);
        Assert.Contains("\"count\":\"15\"", payload);
    }

    private static GitHubDailyWorkflowClient Client(
        IDictionary<string, string?> settings, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var http = new HttpClient(new StubHandler(respond))
        {
            BaseAddress = new Uri("https://api.github.com/")
        };
        return new(http, configuration, NullLogger<GitHubDailyWorkflowClient>.Instance);
    }

    private static GitHubDailyWorkflowClient StatusClient(
        string runs, int? attempts = null, bool successMarker = false, string checkpointDate = "2026-11-09",
        bool reservationOnly = false) =>
        Client(TokenConfiguration(), request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/zip"))
            {
                using var buffer = new MemoryStream();
                using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                using (var writer = new StreamWriter(archive.CreateEntry("daily-checkpoints.json").Open()))
                    writer.Write(JsonSerializer.Serialize(new
                    {
                        FormatVersion = 1, Date = checkpointDate, Attempts = attempts ?? 0,
                        CompletedSteps = Array.Empty<string>()
                    }));
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
            }
            if (path.EndsWith("/actions/runs/99"))
                return JsonResponse($$"""
                    {"path":".github/workflows/daily-run.yml","event":"schedule","status":"completed",
                     "conclusion":"{{(successMarker ? "success" : "failure")}}","created_at":"2026-11-09T15:00:00Z",
                     "html_url":"https://github.com/run/checkpoint","run_attempt":1}
                    """);
            if (path.EndsWith("/artifacts"))
            {
                bool success = request.RequestUri.Query.Contains("daily-success-");
                string name = success ? "daily-success-2026-11-09" :
                    reservationOnly ? "daily-attempt-2026-11-09-99-1" : "daily-checkpoint-2026-11-09";
                return (success ? successMarker : attempts.HasValue)
                    ? JsonResponse(JsonSerializer.Serialize(new
                    {
                        artifacts = new[]
                        {
                            new { id = 123, name, expired = false, workflow_run = new { id = 99 } }
                        }
                    }))
                    : JsonResponse("""{"artifacts":[]}""");
            }
            return JsonResponse(runs);
        });

    private static Dictionary<string, string?> TokenConfiguration() =>
        new() { ["GitHub:Token"] = "test-token" };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
