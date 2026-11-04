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
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "checkpoint.json");
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
    public async Task DifferentTicketCountOrRevisionDoesNotReuseCompletedSteps(int count, string revision)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "checkpoint.json");
        try
        {
            var checkpoint = await DailyRunCheckpoint.LoadAsync(path, Date, 15, "revision-a", _ => { });
            await checkpoint.MarkCompletedAsync(Step);

            var restored = await DailyRunCheckpoint.LoadAsync(path, Date, count, revision, _ => { });

            Assert.False(restored.IsCompleted(Step));
        }
        finally
        {
            string directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
