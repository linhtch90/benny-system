using Benny.Caching;
using Benny.IngestionService.Application.UseCases;
using Benny.IngestionService.Domain.Ports.Out;
using Benny.IngestionService.Infrastructure.Adapters.Out;
using Benny.Observability;
using Benny.Observability.Context;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddBennyObservability(builder.Configuration, "benny-ingestion-service");
builder.Services.AddBennyCaching(builder.Configuration);
builder.Services.AddScoped<IngestionOrchestrationUseCase>();
builder.Services.AddHealthChecks();

builder.Services.AddHttpClient<IRiskEngineClient, HttpRiskEngineClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RiskEngineUrl"] ?? "http://localhost:8082");
    client.DefaultRequestVersion = new Version(2, 0);
})
.AddHttpMessageHandler<RequestContextPropagationHandler>()
.AddStandardResilienceHandler();

builder.Services.AddHttpClient<ILedgerServiceClient, HttpLedgerServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:LedgerServiceUrl"] ?? "http://localhost:8083");
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
