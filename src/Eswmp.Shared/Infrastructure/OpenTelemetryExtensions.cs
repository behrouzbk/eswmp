using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Eswmp.Shared.Infrastructure;

public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Registers OpenTelemetry distributed tracing.
    /// Exports to OTLP (Jaeger/Otel Collector) when Otel:Endpoint is configured;
    /// falls back to console exporter in development.
    /// </summary>
    public static IServiceCollection AddEswmpObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceName = configuration["Otel:ServiceName"] ?? "Eswmp.Unknown";
        var otlpEndpoint = configuration["Otel:Endpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(res => res.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                    })
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(opts =>
                        opts.Endpoint = new Uri(otlpEndpoint));
                }
                else
                {
                    tracing.AddConsoleExporter();
                }
            });

        return services;
    }
}
