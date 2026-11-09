using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prediction.Services;

public sealed record DailyWorkflowStatus(
    string State, bool Configured, bool CanRetry, int Attempts,
    string? RunUrl, DateTimeOffset? UpdatedAt);

public interface IGitHubDailyWorkflowClient
{
    Task<DailyWorkflowStatus> GetStatusAsync(DateOnly date, CancellationToken cancellationToken);
    Task DispatchAsync(DateOnly date, CancellationToken cancellationToken);
}

public sealed class GitHubDailyWorkflowClient(
    HttpClient httpClient, IConfiguration configuration, ILogger<GitHubDailyWorkflowClient> logger)
    : IGitHubDailyWorkflowClient
{
    private const string WorkflowPath = ".github/workflows/daily-run.yml";
    private const string Repository = "dimecako/Prediction";
    private static readonly TimeZoneInfo LocalZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");

    private string? Token => configuration["GitHub:Token"];

    public async Task<DailyWorkflowStatus> GetStatusAsync(
        DateOnly date, CancellationToken cancellationToken)
    {
        string? token = Token;
        if (string.IsNullOrWhiteSpace(token))
            return new("unconfigured", false, false, 0, null, null);

        using var request = CreateRequest(HttpMethod.Get,
            $"repos/{Repository}/actions/workflows/daily-run.yml/runs?branch=main&per_page=100", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("GitHub workflow status request failed with HTTP {StatusCode}.",
                (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<WorkflowRunsResponse>(stream,
            cancellationToken: cancellationToken) ??
            throw new InvalidDataException("GitHub returned an empty workflow-runs response.");

        var runs = result.Runs
            .Select(run => (Run: run, CreatedAt: DateTimeOffset.Parse(run.CreatedAt)))
            .Where(item => IsRelevantRun(item.Run, item.CreatedAt, date))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Run.RunAttempt)
            .ToList();
        int attempts = runs.Sum(item => Math.Max(item.Run.RunAttempt, 1));
        var latest = runs.FirstOrDefault();
        if (latest.Run == null)
        {
            bool schedulePassed = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, LocalZone).TimeOfDay >=
                new TimeSpan(9, 7, 0);
            return attempts >= 3
                ? new("retry-limit", true, false, attempts, null, null)
                : new(schedulePassed ? "missed" : "waiting", true, schedulePassed, attempts, null, null);
        }

        var state = latest.Run.Status switch
        {
            "queued" or "waiting" or "requested" => "queued",
            "in_progress" => "running",
            "completed" when latest.Run.Conclusion == "success" => "succeeded",
            "completed" => "failed",
            _ => "unknown"
        };
        bool canRetry = state == "failed" && attempts < 3;
        if (state == "failed" && attempts >= 3)
            state = "retry-limit";
        return new(state, true, canRetry, attempts, latest.Run.HtmlUrl, latest.CreatedAt);
    }

    public async Task DispatchAsync(DateOnly date, CancellationToken cancellationToken)
    {
        string? token = Token;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GitHub__Token is not configured on the web service.");

        using var request = CreateRequest(HttpMethod.Post,
            $"repos/{Repository}/actions/workflows/daily-run.yml/dispatches", token);
        request.Content = JsonContent.Create(new
        {
            @ref = "main",
            inputs = new
            {
                target_date = date.ToString("yyyy-MM-dd"),
                count = "15"
            }
        });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("GitHub workflow dispatch failed with HTTP {StatusCode}.",
                (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static bool IsRelevantRun(WorkflowRun run, DateTimeOffset createdAt, DateOnly date)
    {
        if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(createdAt, LocalZone).DateTime) != date)
            return false;
        if (run.Event == "workflow_dispatch")
            return true;
        if (run.Event != "schedule")
            return false;
        TimeSpan localTime = TimeZoneInfo.ConvertTime(createdAt, LocalZone).TimeOfDay;
        return localTime >= new TimeSpan(7, 0, 0) && localTime < new TimeSpan(10, 0, 0);
    }

    private sealed record WorkflowRunsResponse(
        [property: JsonPropertyName("workflow_runs")] IReadOnlyList<WorkflowRun> Runs);

    private sealed record WorkflowRun(
        [property: JsonPropertyName("event")] string Event,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("conclusion")] string? Conclusion,
        [property: JsonPropertyName("created_at")] string CreatedAt,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("run_attempt")] int RunAttempt);
}
