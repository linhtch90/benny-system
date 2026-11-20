using System.Threading.Channels;
using Benny.Common.Domain.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Benny.StreamProcessor.Application;

public interface IPolyglotSink
{
    Task WriteBatchAsync(IReadOnlyList<TransactionValidatedEvent> batch, CancellationToken cancellationToken);
}

public interface IDeadLetterPublisher
{
    Task PublishAsync(string rawPayload, string errorReason, string? traceParent, CancellationToken cancellationToken);
}

public sealed class MicroBatchPipeline(
    IEnumerable<IPolyglotSink> sinks,
    ILogger<MicroBatchPipeline> logger)
{
    private readonly Channel<TransactionValidatedEvent> _channel = Channel.CreateBounded<TransactionValidatedEvent>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(200)
        })
        .Build();

    public ValueTask EnqueueAsync(TransactionValidatedEvent evt, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(evt, cancellationToken);

    public async Task ProcessBatchesAsync(int maxBatchSize, CancellationToken cancellationToken)
    {
        var buffer = new List<TransactionValidatedEvent>(maxBatchSize);

        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (buffer.Count < maxBatchSize && _channel.Reader.TryRead(out var item))
            {
                buffer.Add(item);
            }

            if (buffer.Count == 0) continue;

            foreach (var chunk in buffer.Chunk(maxBatchSize))
            {
                await _retryPipeline.ExecuteAsync(async ct =>
                {
                    await Task.WhenAll(sinks.Select(s => s.WriteBatchAsync(chunk, ct)));
                }, cancellationToken);

                logger.LogDebug("Flushed micro-batch of {Count} events to {SinkCount} polyglot sinks", chunk.Length, sinks.Count());
            }

            buffer.Clear();
        }
    }
}
