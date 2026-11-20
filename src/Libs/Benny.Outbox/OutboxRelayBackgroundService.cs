using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Benny.Outbox;

public sealed class OutboxRelayBackgroundService(
    IOutboxRelayStore outboxStore,
    IProducer<string, string> kafkaProducer,
    ILogger<OutboxRelayBackgroundService> logger) : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        logger.LogInformation("OutboxRelayBackgroundService started.");

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var batch = await outboxStore.ClaimBatchAsync(BatchSize, stoppingToken);
                if (batch.Count == 0)
                {
                    continue;
                }

                var publishedIds = new List<Guid>(batch.Count);

                foreach (var evt in batch)
                {
                    var headers = new Headers
                    {
                        { "event-type", Encoding.UTF8.GetBytes(evt.EventType) },
                        { "event-id", Encoding.UTF8.GetBytes(evt.Id.ToString()) }
                    };

                    if (!string.IsNullOrWhiteSpace(evt.TraceParent))
                    {
                        headers.Add("traceparent", Encoding.UTF8.GetBytes(evt.TraceParent));
                    }

                    var message = new Message<string, string>
                    {
                        Key = evt.AggregateId,
                        Value = evt.Payload,
                        Headers = headers
                    };

                    await kafkaProducer.ProduceAsync(evt.Topic, message, stoppingToken);
                    publishedIds.Add(evt.Id);
                }

                await outboxStore.MarkPublishedAsync(publishedIds, stoppingToken);
                logger.LogDebug("Relayed {Count} outbox events to Kafka", publishedIds.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error relaying outbox events to Kafka; will retry next tick");
            }
        }
    }
}
