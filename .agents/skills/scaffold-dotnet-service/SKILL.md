---
name: scaffold-dotnet-service
description: >-
  Use this skill when creating or scaffolding a new microservice project in the
  Benny System using .NET 10, C# 14, and ASP.NET Core 10 Web API Controllers.
---

# Skill: Scaffold ASP.NET Core 10 & C# 14 Microservice

Follow this workflow whenever a new service project (e.g., `src/Services/Benny.IngestionService`) is added to the repository. **Note: Do NOT use Minimal APIs; always use Web API Controllers (`[ApiController]`, `ControllerBase`).**

## Step 1: Register Project in `BennySystem.sln`
1. Create the directory `src/Services/<ServiceName>/`.
2. Add the project to `BennySystem.sln` under the `src/Services` solution folder:
   ```bash
   dotnet sln BennySystem.sln add src/Services/<ServiceName>/<ServiceName>.csproj --solution-folder src/Services
   ```

## Step 2: Create `<ServiceName>.csproj` (.NET 10 SDK + Central Package Management)
Create `src/Services/<ServiceName>/<ServiceName>.csproj` relying on root `Directory.Build.props` (`net10.0`, `LangVersion=14.0`, Server GC, TieredPGO) and `Directory.Packages.props`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="..\..\Libs\Benny.Common.Domain\Benny.Common.Domain.csproj" />
    <ProjectReference Include="..\..\Libs\Benny.Observability\Benny.Observability.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="Microsoft.Extensions.Resilience" />
  </ItemGroup>

</Project>
```

## Step 3: Create Hexagonal Folder & Namespace Structure
Create the standard clean/hexagonal directory layout under `src/Services/<ServiceName>/`:
- `Domain/Models/` — C# 14 `record` / `readonly record struct` entities, value objects, and `abstract record` state machines.
- `Domain/Ports/In/` — Inbound use-case interfaces.
- `Domain/Ports/Out/` — Outbound repository, cache, and event publisher interfaces.
- `Application/UseCases/` — Use-case implementations using `Task.WhenAll` with linked `CancellationTokenSource` deadlines and `AsyncLocal<RequestContext?>`.
- `Infrastructure/Adapters/In/Http/` — ASP.NET Core 10 Web API Controllers (`[ApiController]`, `ControllerBase`) & `ProblemDetails` exception mapping.
- `Infrastructure/Adapters/Out/` — Persistence (`EF Core 10` / `Dapper`), Redis L1/L2 cache, HTTP clients, and Kafka adapters.
- `Infrastructure/Config/` — Dependency injection extension methods.

## Step 4: Configure `Program.cs` & `appsettings.json`
Ensure Web API Controllers (`AddControllers()` / `MapControllers()`), OpenTelemetry, Graceful Shutdown, `ProblemDetails`, and Kubernetes Health Probes (`/health/live`, `/health/ready`, `/health/startup`) are enabled by default:
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddControllers();
builder.Services.AddBennyObservability(builder.Configuration, "<service-name>");
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseBennyObservability();
app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/startup");

app.Run();
```

## Step 5: Verify Build & Architecture Test
Run `dotnet build` and `dotnet test` to verify compilation on .NET 10 / C# 14 and pass `NetArchTest.Rules` / Testcontainers checks.
