using Benny.LedgerService.Application.UseCases;
using Benny.Observability;
using Benny.Outbox;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

var pgConnection = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=benny_ledger;Username=benny;Password=benny_secret;Maximum Pool Size=25";

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(pgConnection));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddBennyObservability(builder.Configuration, "benny-ledger-service");
builder.Services.AddBennyOutbox(builder.Configuration, runRelay: true);
builder.Services.AddScoped<LedgerCommandService>();
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
