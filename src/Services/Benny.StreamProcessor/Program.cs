using Benny.Caching;
using Benny.Observability;
using Benny.StreamProcessor.Application;
using Benny.StreamProcessor.Infrastructure.Kafka;
using Benny.StreamProcessor.Infrastructure.Sinks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddControllers();
builder.Services.AddBennyObservability(builder.Configuration, "benny-stream-processor");
builder.Services.AddBennyCaching(builder.Configuration);

builder.Services.AddSingleton<IPolyglotSink, ScyllaTelemetrySink>();
builder.Services.AddSingleton<IPolyglotSink, ClickHouseAnalyticsSink>();
builder.Services.AddSingleton<IPolyglotSink, RedisMetricsSink>();
builder.Services.AddSingleton<IDeadLetterPublisher, KafkaDeadLetterPublisher>();
builder.Services.AddSingleton<MicroBatchPipeline>();
builder.Services.AddHostedService<KafkaStreamConsumerWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseBennyObservability();
app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/startup");

app.Run();

public partial class Program;
