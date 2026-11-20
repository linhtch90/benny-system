using System.Diagnostics;
using System.Diagnostics.Metrics;
using Benny.Observability.Context;
using Benny.Observability.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Benny.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddBennyObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalProblemDetailsExceptionHandler>();
        services.AddSingleton<RequestContextCorrelationMiddleware>();
        services.AddTransient<RequestContextPropagationHandler>();

        services.AddSingleton(new ActivitySource(serviceName));
        services.AddSingleton(new Meter(serviceName));

        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(serviceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(serviceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddPrometheusExporter();
            });

        return services;
    }

    public static WebApplication UseBennyObservability(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<RequestContextCorrelationMiddleware>();
        app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }
}
