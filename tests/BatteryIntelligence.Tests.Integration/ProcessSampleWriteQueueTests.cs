using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// The dedicated <c>ProcessSample</c> pipeline (docs/database.md section 4;
/// docs/roadmap.md Phase 7): batched writes, the count trigger, and the permanent
/// Estimated grade.
/// </summary>
public sealed class ProcessSampleWriteQueueTests
{
    [Fact]
    public async Task FlushAsync_PersistsRows_WithTheEstimatedGradeAndModelSource()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        ProcessSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<ProcessSampleWriteQueue>.Instance);

        queue.Enqueue(Batch(sessionId: null,
            new ProcessSampleRecord(0, "Google Chrome", "chrome", 22.5, 800_000_000, true, 1_240, 34.2),
            new ProcessSampleRecord(0, "System baseline", "__baseline__", null, null, false, 4_000, 40.0)));
        await queue.FlushAsync();

        long rows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM ProcessSample;");
        long quality = await db.ScalarAsync<long>("SELECT DataQuality FROM ProcessSample WHERE ApplicationKey = 'chrome';");
        long source = await db.ScalarAsync<long>("SELECT MeasurementSource FROM ProcessSample WHERE ApplicationKey = 'chrome';");
        long power = await db.ScalarAsync<long>("SELECT EstimatedPowerMw FROM ProcessSample WHERE ApplicationKey = 'chrome';");

        Assert.Equal(2, rows);
        Assert.Equal((long)DataQuality.Estimated, quality);
        Assert.Equal((long)MeasurementSource.Model, source);
        Assert.Equal(1_240, power);
        Assert.NotNull(queue.LastFlushUtc);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task Enqueue_ReachingCountThreshold_FlushesWithoutAnExplicitCall()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        ProcessSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<ProcessSampleWriteQueue>.Instance);

        for (int i = 0; i < 210; i++)
        {
            queue.Enqueue(Batch(null, new ProcessSampleRecord(0, "App", "app", 1.0, 1, false, 10, 1.0)));
        }

        await WaitUntilAsync(() => queue.PendingCount == 0, TimeSpan.FromSeconds(5));

        long rows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM ProcessSample;");
        Assert.True(rows >= 200);
    }

    [Fact]
    public async Task Enqueue_EmptyBatch_WritesNothing()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        ProcessSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<ProcessSampleWriteQueue>.Instance);
        queue.Enqueue(new ProcessSampleBatch(DateTimeOffset.UtcNow, null, "AppEnergyV1", []));
        await queue.FlushAsync();

        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM ProcessSample;"));
    }

    private static ProcessSampleBatch Batch(long? sessionId, params ProcessSampleRecord[] rows) =>
        new(DateTimeOffset.UtcNow, sessionId, "AppEnergyV1", rows);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }
}
