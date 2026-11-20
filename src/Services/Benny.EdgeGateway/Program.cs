using Benny.Caching;
using Benny.EdgeGateway.Infrastructure.RateLimiting;
using Benny.Observability;
using Benny.Observability.Context;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddBennyObservability(builder.Configuration, "benny-edge-gateway");
builder.Services.AddBennyCaching(builder.Configuration);
builder.Services.AddSingleton<ITokenBucketRateLimiter, RedisTokenBucketRateLimiter>();
builder.Services.AddHealthChecks();

builder.Services.AddHttpClient("IngestionClient", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:IngestionUrl"] ?? "http://localhost:8081");
    client.DefaultRequestVersion = new Version(2, 0);
})
.AddHttpMessageHandler<RequestContextPropagationHandler>()
.AddStandardResilienceHandler();

builder.Services.AddHttpClient("QueryClient", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:QueryUrl"] ?? "http://localhost:8085");
    client.DefaultRequestVersion = new Version(2, 0);
})
.AddHttpMessageHandler<RequestContextPropagationHandler>()
.AddStandardResilienceHandler();

var app = builder.Build();

app.UseBennyObservability();
app.MapOpenApi();
app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/startup");

app.Run();

public partial class Program;
