using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.Http.Headers;
using System.Text;

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
            var username = configuration["MessageBus:Username"] ?? "guest";
            var password = configuration["MessageBus:Password"] ?? "guest";
            var managementPort = configuration.GetValue<int>("MessageBus:ManagementPort", 15672);

            // The RabbitMQ management API requires Basic Auth — AddUrlGroup sends no
            // credentials by default, so a plain URL check always comes back 401.
            builder.AddAsyncCheck("rabbitmq", async () =>
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

                try
                {
                    var response = await client.GetAsync(
                        $"http://{rabbitHost}:{managementPort}/api/health/checks/alarms");

                    return response.IsSuccessStatusCode
                        ? HealthCheckResult.Healthy()
                        : HealthCheckResult.Unhealthy(
                            $"RabbitMQ management API returned {(int)response.StatusCode}");
                }
                catch (Exception ex)
                {
                    return HealthCheckResult.Unhealthy("RabbitMQ unreachable", ex);
                }
            }, tags: ["ready"]);
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
