using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Benny.Outbox;

public static class OutboxExtensions
{
    public static IServiceCollection AddBennyOutbox(this IServiceCollection services, IConfiguration configuration, bool runRelay = true)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        services.AddSingleton<IProducer<string, string>>(_ =>
        {
            var config = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true,
                CompressionType = CompressionType.Lz4,
                LingerMs = 5,
                BatchSize = 65536
            };
            return new ProducerBuilder<string, string>(config).Build();
        });

        services.AddSingleton<NpgsqlOutboxStore>();
        services.AddSingleton<IOutboxPublisher>(sp => sp.GetRequiredService<NpgsqlOutboxStore>());
        services.AddSingleton<IOutboxRelayStore>(sp => sp.GetRequiredService<NpgsqlOutboxStore>());

        if (runRelay)
        {
            services.AddHostedService<OutboxRelayBackgroundService>();
        }

        return services;
    }
}
