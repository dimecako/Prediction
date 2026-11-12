using Prediction.Infrastructure;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class DailyRunCheckpointTests
{
    private static readonly DateOnly Date = new(2026, 11, 3);
    private static readonly DailyRunStep Step = new("ticket", ["ticket", "2026-11-03", "15"]);

    [Fact]
    public async Task CompletedStepsArePersistedAndRestoredForSameInputsAndRevision()
    {
        string path = Path.Combine(".automation", "test-checkpoints", Guid.NewGuid().ToString("N"), "checkpoint.json");
        try
        {
            var checkpoint = await DailyRunCheckpoint.LoadAsync(path, Date, 15, "revision-a", _ => { });
            await checkpoint.MarkCompletedAsync(Step);

            var restored = await DailyRunCheckpoint.LoadAsync(path, Date, 15, "revision-a", _ => { });

            Assert.True(restored.IsCompleted(Step));
        }
        finally
        {
            string directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(4, "revision-a")]
    [InlineData(15, "revision-b")]
    public async Task DifferentTicketCountOrRevisionPreservesCompletedStepsAndAttempts(int count, string revision)
    {
        string path = Path.Combine(".automation", "test-checkpoints", Guid.NewGuid().ToString("N"), "checkpoint.json");
        try
        {
            var checkpoint = await DailyRunCheckpoint.LoadAsync(path, Date, 15, "revision-a", _ => { });
            Assert.True(await checkpoint.TryBeginAttemptAsync());
            await checkpoint.MarkCompletedAsync(Step);

            var restored = await DailyRunCheckpoint.LoadAsync(path, Date, count, revision, _ => { });

            Assert.True(restored.IsCompleted(Step));
            Assert.Equal(1, restored.Attempts);
        }
        finally
        {
            string directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExactlyThreeAttemptsPersistAcrossRestoresAndOnlyIncompleteWorkRemains()
    {
        string path = Path.Combine(".automation", "test-checkpoints", Guid.NewGuid().ToString("N"), "checkpoint.json");
        try
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                var checkpoint = await DailyRunCheckpoint.LoadAsync(path, Date, attempt, $"revision-{attempt}", _ => { });
                Assert.True(await checkpoint.TryBeginAttemptAsync());
                Assert.Equal(attempt, checkpoint.Attempts);
                if (attempt == 1)
                {
                    await checkpoint.SetPlanAsync(true);
                    await checkpoint.MarkCompletedAsync(Step);
                }
                Assert.True(checkpoint.IsCompleted(Step));
                Assert.True(checkpoint.IncludesDayBefore);
                Assert.False(checkpoint.IsCompleted(new DailyRunStep("incomplete", ["retry"])));
            }
            var exhausted = await DailyRunCheckpoint.LoadAsync(path, Date, 15, "revision-last", _ => { });
            Assert.False(await exhausted.TryBeginAttemptAsync());
            Assert.Equal(3, exhausted.Attempts);
            var nextDate = await DailyRunCheckpoint.LoadAsync(path, Date.AddDays(1), 15, "revision-last", _ => { });
            Assert.Equal(0, nextDate.Attempts);
            Assert.False(nextDate.IsCompleted(Step));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task LocalAttemptsCanContinueAfterThreeAndPreserveCompletedSteps()
    {
        string path = Path.Combine(".automation", "test-checkpoints", Guid.NewGuid().ToString("N"), "checkpoint.json");
        try
        {
            var checkpoint = await DailyRunCheckpoint.LoadAsync(path, Date, 15, "local", _ => { });
            await checkpoint.MarkCompletedAsync(Step);
            for (int attempt = 1; attempt <= 5; attempt++)
                Assert.True(await checkpoint.TryBeginAttemptAsync(enforceLimit: false));

            var restored = await DailyRunCheckpoint.LoadAsync(path, Date, 15, "local", _ => { });
            Assert.Equal(5, restored.Attempts);
            Assert.True(restored.IsCompleted(Step));
            Assert.False(await restored.TryBeginAttemptAsync());
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidatedCollectionIsRetriedWithoutDiscardingOtherSteps()
    {
        string path = Path.Combine(".automation", "test-checkpoints", Guid.NewGuid().ToString("N"), "checkpoint.json");
        try
        {
            var collection = new DailyRunStep("Collect target predictions", ["2026-11-03"]);
            var checkpoint = await DailyRunCheckpoint.LoadAsync(path, Date, 15, "local", _ => { });
            await checkpoint.MarkCompletedAsync(collection);
            await checkpoint.MarkCompletedAsync(Step);
            await checkpoint.InvalidateAsync(collection);

            var restored = await DailyRunCheckpoint.LoadAsync(path, Date, 15, "local", _ => { });
            Assert.False(restored.IsCompleted(collection));
            Assert.True(restored.IsCompleted(Step));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyCheckpointPreservesStepsAndStartsCountingAttempts()
    {
        string path = Path.Combine(".automation", "test-checkpoints", Guid.NewGuid().ToString("N"), "checkpoint.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path,
                """{"FormatVersion":1,"Date":"2026-11-03","Count":15,"Revision":"old","IncludeDayBefore":true,"CompletedSteps":["done"]}""");
            var checkpoint = await DailyRunCheckpoint.LoadAsync(path, Date, 4, "new", _ => { });
            Assert.True(checkpoint.IsCompleted("done"));
            Assert.True(checkpoint.IncludesDayBefore);
            Assert.Equal(0, checkpoint.Attempts);
            Assert.True(await checkpoint.TryBeginAttemptAsync());
            Assert.Equal(1, (await DailyRunCheckpoint.LoadAsync(path, Date, 4, "new", _ => { })).Attempts);
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
