using Benny.Caching;
using Benny.Observability;
using Benny.QueryService.Infrastructure.Adapters.Out;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddBennyObservability(builder.Configuration, "benny-query-service");
builder.Services.AddBennyCaching(builder.Configuration);
builder.Services.AddSingleton<IAnalyticsQueryRepository, PolyglotAnalyticsQueryRepository>();
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
