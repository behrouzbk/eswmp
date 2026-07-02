using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Eswmp.Shared.Extensions;

public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds Redis and RabbitMQ infrastructure health checks.
    /// Call before service-specific checks (e.g., AddDbContextCheck).
    /// </summary>
    public static IHealthChecksBuilder AddEswmpInfrastructureChecks(
        this IHealthChecksBuilder builder,
        IConfiguration configuration)
    {
        var redisConnStr = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnStr))
        {
            builder.AddRedis(redisConnStr, name: "redis", tags: ["ready"]);
        }

        var rabbitHost = configuration["MessageBus:Host"];
        if (!string.IsNullOrWhiteSpace(rabbitHost))
        {
            builder.AddUrlGroup(
                new Uri($"http://{rabbitHost}:15672/api/health/checks/alarms"),
                name: "rabbitmq",
                tags: ["ready"]);
        }

        return builder;
    }

    /// <summary>
    /// Maps /health/live (always 200) and /health/ready (infra + DB checks).
    /// </summary>
    public static WebApplication MapEswmpHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = 200,
                [HealthStatus.Degraded] = 200,
                [HealthStatus.Unhealthy] = 200
            }
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = 200,
                [HealthStatus.Degraded] = 200,
                [HealthStatus.Unhealthy] = 503
            }
        });

        return app;
    }
}
