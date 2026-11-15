# .NET 10, C# 14 & ASP.NET Core 10 Engineering Standards

## 1. .NET 10 Solution & MSBuild Conventions (`Directory.Build.props` & `Directory.Packages.props`)

- All projects MUST target `net10.0` with `LangVersion` set to `14.0` (C# 14).
- Use `Directory.Packages.props` for Central Package Management (CPM) so no `.csproj` file hardcodes `Version="..."` attributes.
- Enforce strict build and runtime settings in root `Directory.Build.props`:
  ```xml
  <Project>
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <LangVersion>14.0</LangVersion>
      <Nullable>enable</Nullable>
      <ImplicitUsings>enable</ImplicitUsings>
      <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
      <ServerGarbageCollection>true</ServerGarbageCollection>
      <GarbageCollectionAdaptationMode>1</GarbageCollectionAdaptationMode>
      <TieredPGO>true</TieredPGO>
    </PropertyGroup>
  </Project>
  ```

## 2. Modern C# 14 & .NET 10 Idioms

- **Immutability First**: All DTOs, Domain Events, Commands, Queries, and Value Objects MUST be `record` or `readonly record struct` types with validation in factory methods or constructors.
- **Exhaustive Domain Modeling**: Model domain states and command outcomes using `abstract record` hierarchies (discriminated union pattern) with private constructor and `sealed record` subtypes, paired with exhaustive `switch` expressions and pattern matching.
- **High-Throughput Concurrency & Async/Await**:
  - Never block ThreadPool threads with `.Result`, `.Wait()`, or synchronous I/O; always use `async`/`await` (`Task<T>` or `ValueTask<T>` on hot cached paths) and propagate `CancellationToken` to every async boundary.
  - Use **`Task.WhenAll` with a linked `CancellationTokenSource` deadline** (`CancellationTokenSource.CreateLinkedTokenSource`) when aggregating multiple downstream calls in parallel (e.g., Risk Engine querying Redis velocity counters + Fraud rules concurrently within 5ms).
  - Use **`AsyncLocal<RequestContext?>`** and **`System.Diagnostics.Activity.Current`** for propagating request-scoped context (`CorrelationId`, `TenantId`, `IdempotencyKey`) safely across child tasks.
- **Stream & Batch Processing**: Use `System.Threading.Channels.Channel<T>` (bounded channels for backpressure) and `IEnumerable<T>.Chunk(size)` / `IAsyncEnumerable<T>` for in-memory micro-batching and sliding-window transformations.
- **Zero-Reflection Serialization**: Use `System.Text.Json` Source Generators (`[JsonSerializable]`, `JsonSerializerContext`) for ultra-low latency and Native AOT / ReadyToRun readiness.

## 3. ASP.NET Core 10 Best Practices

- **Web API Controllers & Kestrel (No Minimal APIs)**:
  - **Do NOT use Minimal APIs** (`app.MapGet`, `app.MapPost`, `MapGroup`, etc.) for application endpoints.
  - Always use **ASP.NET Core 10 Web API Controllers** (`[ApiController]`, `ControllerBase`, `[Route("api/v1/...")]`, `ActionResult<T>`) registered via `builder.Services.AddControllers()` and `app.MapControllers()`.
  - Configure graceful shutdown (`HostOptions.ShutdownTimeout = TimeSpan.FromSeconds(30)`) and Kubernetes health probes (`/health/live`, `/health/ready`, `/health/startup`).
- **API Contracts & Error Handling**:
  - Register `builder.Services.AddProblemDetails()` and a global `IExceptionHandler` returning standardized RFC 9457 `ProblemDetails` for all error responses.
  - Validate inbound payloads at the HTTP/gRPC boundary before invoking domain logic.
- **Observability**:
  - Instrument services using **OpenTelemetry .NET** (`ActivitySource` for distributed tracing and `Meter` for Prometheus/OTLP metrics).
- **Testing**:
  - Use **xUnit v3** and **Testcontainers for .NET** (`Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Testcontainers.Kafka`, `Testcontainers.ClickHouse`) for integration tests against real containers.
  - Use **NetArchTest.Rules** to enforce hexagonal/clean architecture boundaries within each microservice.
