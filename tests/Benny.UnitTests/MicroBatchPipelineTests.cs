using System.Collections.Concurrent;
using Benny.Common.Domain.Models;
using Benny.StreamProcessor.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Benny.UnitTests;

public sealed class MicroBatchPipelineTests
{
    private sealed class RecordingSink : IPolyglotSink
    {
        public ConcurrentBag<int> BatchSizes { get; } = [];
        public int TotalWritten => BatchSizes.Sum();

        public Task WriteBatchAsync(IReadOnlyList<TransactionValidatedEvent> batch, CancellationToken cancellationToken)
        {
            BatchSizes.Add(batch.Count);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProcessBatchesAsync_FansOutMicroBatchesToAllSinksInParallel()
    {
        var sink1 = new RecordingSink();
        var sink2 = new RecordingSink();
        var pipeline = new MicroBatchPipeline([sink1, sink2], NullLogger<MicroBatchPipeline>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        for (var i = 0; i < 15; i++)
        {
            await pipeline.EnqueueAsync(new TransactionValidatedEvent(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "tenant-1",
                "acc-1",
                10m + i,
                "USD",
                "m-1",
                0.02m,
                "APPROVED",
                DateTimeOffset.UtcNow), cts.Token);
        }

        var worker = pipeline.ProcessBatchesAsync(10, cts.Token);

        while (sink1.TotalWritten < 15 || sink2.TotalWritten < 15)
        {
            await Task.Delay(10, cts.Token);
        }

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker);

        Assert.Equal(15, sink1.TotalWritten);
        Assert.Equal(15, sink2.TotalWritten);
    }
}
