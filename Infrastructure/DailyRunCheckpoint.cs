using System.Text.Json;
using Prediction.Services;

namespace Prediction.Infrastructure;

public sealed class DailyRunCheckpoint
{
    private const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string? filePath;
    private readonly HashSet<string> completedSteps;
    private readonly string date;
    private readonly int count;
    private readonly string revision;
    private bool includeDayBefore;

    private DailyRunCheckpoint(string? filePath, string date, int count, string revision,
        IEnumerable<string>? completedSteps = null, bool includeDayBefore = false)
    {
        this.filePath = filePath;
        this.date = date;
        this.count = count;
        this.revision = revision;
        this.completedSteps = new(completedSteps ?? [], StringComparer.Ordinal);
        this.includeDayBefore = includeDayBefore;
    }

    public static async Task<DailyRunCheckpoint> LoadAsync(
        string? filePath, DateOnly date, int count, string revision, Action<string> log)
    {
        string dateText = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new(filePath, dateText, count, revision);

        await using var stream = File.OpenRead(filePath);
        var saved = await JsonSerializer.DeserializeAsync<CheckpointFile>(stream, JsonOptions)
            ?? throw new InvalidDataException("Daily-run checkpoint file is empty.");
        if (saved.FormatVersion != CurrentFormatVersion || saved.Date != dateText ||
            saved.Count != count || saved.Revision != revision)
        {
            log("[DAILY-RUN] Checkpoint does not match this date, ticket count, or deployed revision; starting without saved steps.");
            return new(filePath, dateText, count, revision);
        }

        log($"[DAILY-RUN] Restored {saved.CompletedSteps.Length} completed steps from checkpoint.");
        return new(filePath, dateText, count, revision, saved.CompletedSteps, saved.IncludeDayBefore);
    }

    public bool IncludesDayBefore => includeDayBefore;

    public bool IsCompleted(DailyRunStep step) => completedSteps.Contains(step.CheckpointKey);

    public bool IsCompleted(string checkpointKey) => completedSteps.Contains(checkpointKey);

    public async Task SetPlanAsync(bool includeDayBefore)
    {
        if (this.includeDayBefore == includeDayBefore)
            return;
        this.includeDayBefore = includeDayBefore;
        await SaveAsync();
    }

    public async Task MarkCompletedAsync(DailyRunStep step)
    {
        if (!completedSteps.Add(step.CheckpointKey))
            return;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, new CheckpointFile(
                    CurrentFormatVersion, date, count, revision, includeDayBefore,
                    completedSteps.Order().ToArray()), JsonOptions);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed record CheckpointFile(
        int FormatVersion, string Date, int Count, string Revision, bool IncludeDayBefore,
        string[] CompletedSteps);
}
