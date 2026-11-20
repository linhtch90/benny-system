using Benny.Caching;
using Benny.Observability;
using Benny.RiskEngine.Application.UseCases;
using Benny.RiskEngine.Domain.Models;
using Benny.RiskEngine.Infrastructure.Adapters.Out;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddBennyObservability(builder.Configuration, "benny-risk-engine");
builder.Services.AddBennyCaching(builder.Configuration);

builder.Services.AddSingleton<IVelocityStore, RedisVelocityStore>();
builder.Services.AddSingleton<ITenantRuleStore, CachedTenantRuleStore>();
builder.Services.AddSingleton<IAnomalyScorer, HeuristicAnomalyScorer>();
builder.Services.AddScoped<ParallelRiskEvaluationService>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseBennyObservability();
app.MapOpenApi();
app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/startup");

app.Run();

public partial class Program;
