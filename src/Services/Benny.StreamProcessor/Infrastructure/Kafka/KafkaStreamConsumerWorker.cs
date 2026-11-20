using System.Text;
using System.Text.Json;
using Benny.Common.Domain.Models;
using Benny.Common.Domain.Serialization;
using Benny.StreamProcessor.Application;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Benny.StreamProcessor.Infrastructure.Kafka;

public sealed class KafkaDeadLetterPublisher(IConfiguration configuration) : IDeadLetterPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer = new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
        Acks = Acks.All,
        EnableIdempotence = true
    }).Build();

    public async Task PublishAsync(string rawPayload, string errorReason, string? traceParent, CancellationToken cancellationToken)
    {
        var headers = new Headers
        {
            { "dlq-reason", Encoding.UTF8.GetBytes(errorReason) }
        };

        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            headers.Add("traceparent", Encoding.UTF8.GetBytes(traceParent));
        }

        await _producer.ProduceAsync("benny.dlq", new Message<string, string>
        {
            Key = Guid.CreateVersion7().ToString(),
            Value = rawPayload,
            Headers = headers
        }, cancellationToken);
    }

    public void Dispose() => _producer.Dispose();
}

public sealed class KafkaStreamConsumerWorker(
    MicroBatchPipeline pipeline,
    IDeadLetterPublisher dlqPublisher,
    IConfiguration configuration,
    ILogger<KafkaStreamConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batchTask = Task.Run(() => pipeline.ProcessBatchesAsync(100, stoppingToken), stoppingToken);

        var config = new ConsumerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = configuration["Kafka:GroupId"] ?? "benny-stream-processor",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe("benny.transactions.validated");

        logger.LogInformation("KafkaStreamConsumerWorker subscribed to benny.transactions.validated");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(100));
                if (result is null) continue;

                try
                {
                    var evt = JsonSerializer.Deserialize(result.Message.Value, BennyJsonContext.Default.TransactionValidatedEvent);
                    if (evt is null)
                    {
                        await dlqPublisher.PublishAsync(result.Message.Value, "Deserialized null event", null, stoppingToken);
                    }
                    else
                    {
                        await pipeline.EnqueueAsync(evt, stoppingToken);
                    }

                    consumer.Commit(result);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Poison pill or processing error at offset {Offset}; routing to benny.dlq", result.Offset);
                    await dlqPublisher.PublishAsync(result.Message.Value, ex.Message, null, stoppingToken);
                    consumer.Commit(result);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        finally
        {
            consumer.Close();
            await batchTask;
        }
    }
}
