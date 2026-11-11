using System.IO.Compression;
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

        var successTask = FindArtifactAsync($"daily-success-{date:yyyy-MM-dd}", token,
            requireSuccess: true, cancellationToken);
        var checkpointTask = FindArtifactAsync($"daily-checkpoint-{date:yyyy-MM-dd}", token,
            requireSuccess: false, cancellationToken);
        var runsTask = GetRunsAsync(date, token, cancellationToken);
        await Task.WhenAll(successTask, checkpointTask, runsTask);
        var checkpoint = await checkpointTask;
        int attempts = checkpoint == null ? 0 :
            await ReadAttemptsAsync(checkpoint.Artifact.Id, date, token, cancellationToken);
        var success = await successTask;
        if (success != null)
            return new("succeeded", true, false, attempts, success.Run.HtmlUrl,
                DateTimeOffset.Parse(success.Run.CreatedAt));

        var runs = (await runsTask)
            .Select(run => (Run: run, CreatedAt: DateTimeOffset.Parse(run.CreatedAt)))
            .Where(item => IsRelevantRun(item.Run, item.CreatedAt, date))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Run.RunAttempt)
            .ToList();
        var active = runs.FirstOrDefault(item =>
            item.Run.Status is "queued" or "waiting" or "requested" or "pending" or "in_progress");
        if (active.Run != null)
            return new(active.Run.Status == "in_progress" ? "running" : "queued",
                true, false, attempts, active.Run.HtmlUrl, active.CreatedAt);
        if (runs.Any(item => item.Run.Status != "completed"))
            return new("unknown", true, false, attempts, null, null);

        // A green workflow may only have skipped an inactive cron occurrence.
        var failed = runs.FirstOrDefault(item =>
            item.Run.Status == "completed" && item.Run.Conclusion != "success");
        var latest = failed.Run ?? checkpoint?.Run;
        string? runUrl = latest?.HtmlUrl;
        DateTimeOffset? updatedAt = latest == null ? null : DateTimeOffset.Parse(latest.CreatedAt);
        if (attempts >= 3)
            return new("retry-limit", true, false, attempts, runUrl, updatedAt);
        if (latest != null)
            return new("failed", true, true, attempts, runUrl, updatedAt);

        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, LocalZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        bool schedulePassed = date < today || date == today &&
            localNow.TimeOfDay >= new TimeSpan(9, 0, 0);
        return new(schedulePassed ? "missed" : "waiting", true, schedulePassed, attempts, null, null);
    }

    private async Task<IReadOnlyList<WorkflowRun>> GetRunsAsync(
        DateOnly date, string token, CancellationToken cancellationToken)
    {
        var start = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), LocalZone);
        var end = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), LocalZone)
            .AddSeconds(-1);
        string range = Uri.EscapeDataString($"{start:yyyy-MM-ddTHH:mm:ssZ}..{end:yyyy-MM-ddTHH:mm:ssZ}");
        var runs = new List<WorkflowRun>();
        for (int page = 1; ; page++)
        {
            var result = await GetJsonAsync<WorkflowRunsResponse>(
                $"repos/{Repository}/actions/workflows/daily-run.yml/runs" +
                $"?branch=main&created={range}&per_page=100&page={page}", token, cancellationToken);
            runs.AddRange(result.Runs);
            if (result.Runs.Count < 100)
                return runs;
        }
    }

    private async Task<ArtifactMatch?> FindArtifactAsync(
        string name, string token, bool requireSuccess, CancellationToken cancellationToken)
    {
        for (int page = 1; ; page++)
        {
            string filter = requireSuccess ? $"name={Uri.EscapeDataString(name)}&" : "";
            var result = await GetJsonAsync<WorkflowArtifactsResponse>(
                $"repos/{Repository}/actions/artifacts?{filter}per_page=100&page={page}",
                token, cancellationToken);
            string reservationPrefix = name.Replace("daily-checkpoint-", "daily-attempt-") + "-";
            foreach (var artifact in result.Artifacts.Where(a => !a.Expired &&
                (a.Name == name || !requireSuccess &&
                    (a.Name.StartsWith(name + "-", StringComparison.Ordinal) ||
                     a.Name.StartsWith(reservationPrefix, StringComparison.Ordinal)))))
            {
                var run = await GetJsonAsync<WorkflowRun>(
                    $"repos/{Repository}/actions/runs/{artifact.WorkflowRun.Id}", token, cancellationToken);
                if (run.Path == WorkflowPath && run.Status == "completed" &&
                    (!requireSuccess || run.Conclusion == "success"))
                    return new(artifact, run);
            }
            if (result.Artifacts.Count < 100)
                return null;
        }
    }

    private async Task<int> ReadAttemptsAsync(
        long artifactId, DateOnly date, string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"repos/{Repository}/actions/artifacts/{artifactId}/zip", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("daily-checkpoints.json") ??
            throw new InvalidDataException("GitHub checkpoint artifact is missing daily-checkpoints.json.");
        await using var content = entry.Open();
        var checkpoint = await JsonSerializer.DeserializeAsync<CheckpointStatus>(content,
            cancellationToken: cancellationToken);
        if (checkpoint == null || checkpoint.FormatVersion != 1 ||
            checkpoint.Date != date.ToString("yyyy-MM-dd") ||
            checkpoint.Attempts < 0 || checkpoint.CompletedSteps == null)
            throw new InvalidDataException("GitHub returned an invalid daily-run checkpoint.");
        return checkpoint.Attempts;
    }

    private async Task<T> GetJsonAsync<T>(string path, string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path, token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken) ??
            throw new InvalidDataException("GitHub returned an empty automation response.");
    }

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        logger.LogError("GitHub automation request failed with HTTP {StatusCode}.",
            (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
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
        return run.Event is "workflow_dispatch" or "schedule";
    }

    private sealed record WorkflowRunsResponse(
        [property: JsonPropertyName("workflow_runs")] IReadOnlyList<WorkflowRun> Runs);

    private sealed record WorkflowRun(
        [property: JsonPropertyName("event")] string Event,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("conclusion")] string? Conclusion,
        [property: JsonPropertyName("created_at")] string CreatedAt,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("run_attempt")] int RunAttempt,
        [property: JsonPropertyName("path")] string? Path);

    private sealed record WorkflowArtifactsResponse(
        [property: JsonPropertyName("artifacts")] IReadOnlyList<WorkflowArtifact> Artifacts);

    private sealed record WorkflowArtifact(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("expired")] bool Expired,
        [property: JsonPropertyName("workflow_run")] ArtifactRun WorkflowRun);

    private sealed record ArtifactRun([property: JsonPropertyName("id")] long Id);
    private sealed record ArtifactMatch(WorkflowArtifact Artifact, WorkflowRun Run);
    private sealed record CheckpointStatus(int FormatVersion, string Date, int Attempts, string[] CompletedSteps);
}
