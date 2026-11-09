using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Prediction.Services;
using Prediction.Web;
using Xunit;

namespace Prediction.Tests;

public sealed class DailyAutomationEndpointsTests
{
    [Fact]
    public async Task ManualDispatchRequiresAntiforgeryAndOnlyRunsWhenRetryIsAllowed()
    {
        var client = new FakeWorkflowClient
        {
            Status = new("failed", true, true, 1, "https://github.com/run/1", DateTimeOffset.UtcNow)
        };
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAntiforgery();
        builder.Services.AddSingleton<IGitHubDailyWorkflowClient>(client);
        await using var app = builder.Build();
        app.MapGet("/test-token", (HttpContext context, IAntiforgery antiforgery) =>
            antiforgery.GetAndStoreTokens(context).RequestToken);
        app.MapDailyAutomation();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = new Uri(app.Urls.Single())
        };
        try
        {
            var status = await http.GetAsync("/automation/daily-run/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.Contains("\"state\":\"failed\"", await status.Content.ReadAsStringAsync());

            var missingToken = await http.PostAsync("/automation/daily-run", Form(""));
            Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
            Assert.Equal(0, client.Dispatches);

            string token = await http.GetStringAsync("/test-token");
            var accepted = await http.PostAsync("/automation/daily-run", Form(token));
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.Equal(1, client.Dispatches);

            client.Status = client.Status with { CanRetry = false, State = "succeeded" };
            var conflict = await http.PostAsync("/automation/daily-run", Form(token));
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal(1, client.Dispatches);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static FormUrlEncodedContent Form(string token) => new(
        new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

    private sealed class FakeWorkflowClient : IGitHubDailyWorkflowClient
    {
        public DailyWorkflowStatus Status { get; set; } = new("waiting", true, false, 0, null, null);
        public int Dispatches { get; private set; }

        public Task<DailyWorkflowStatus> GetStatusAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(Status);

        public Task DispatchAsync(DateOnly date, CancellationToken cancellationToken)
        {
            Dispatches++;
            return Task.CompletedTask;
        }
    }
}
