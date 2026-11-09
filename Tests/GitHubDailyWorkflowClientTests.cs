using System.Net;
using System.Text;
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
    public async Task StatusUsesOnlyActiveScheduleAndManualRunsForSkopjeDate()
    {
        var client = Client(TokenConfiguration(), _ => JsonResponse("""
            {"workflow_runs":[
              {"event":"schedule","status":"completed","conclusion":"failure","created_at":"2026-11-09T05:07:00Z","html_url":"https://github.com/run/early","run_attempt":1},
              {"event":"schedule","status":"completed","conclusion":"success","created_at":"2026-11-09T06:07:00Z","html_url":"https://github.com/run/first","run_attempt":1},
              {"event":"workflow_dispatch","status":"completed","conclusion":"failure","created_at":"2026-11-09T07:00:00Z","html_url":"https://github.com/run/manual","run_attempt":1},
              {"event":"schedule","status":"completed","conclusion":"failure","created_at":"2026-11-09T09:07:00Z","html_url":"https://github.com/run/late","run_attempt":1}
            ]}
            """));

        var status = await client.GetStatusAsync(Date, CancellationToken.None);

        Assert.Equal("failed", status.State);
        Assert.True(status.CanRetry);
        Assert.Equal(2, status.Attempts);
        Assert.Equal("https://github.com/run/manual", status.RunUrl);
    }

    [Fact]
    public async Task ThreeFailedAttemptsDisableManualRetry()
    {
        var client = Client(TokenConfiguration(), _ => JsonResponse("""
            {"workflow_runs":[
              {"event":"schedule","status":"completed","conclusion":"failure","created_at":"2026-11-09T06:07:00Z","html_url":"https://github.com/run/one","run_attempt":1},
              {"event":"workflow_dispatch","status":"completed","conclusion":"failure","created_at":"2026-11-09T07:07:00Z","html_url":"https://github.com/run/two","run_attempt":1},
              {"event":"schedule","status":"completed","conclusion":"failure","created_at":"2026-11-09T08:07:00Z","html_url":"https://github.com/run/three","run_attempt":1}
            ]}
            """));

        var status = await client.GetStatusAsync(Date, CancellationToken.None);

        Assert.Equal("retry-limit", status.State);
        Assert.False(status.CanRetry);
        Assert.Equal(3, status.Attempts);
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
