using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Eswmp.Shared.Infrastructure;

public static class ResilienceExtensions
{
    /// <summary>
    /// Applies standard Polly resilience to a named HttpClient:
    /// 3 retries with exponential backoff, circuit breaker, 10s timeout.
    /// </summary>
    public static IHttpClientBuilder AddEswmpResilience(this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.Delay = TimeSpan.FromSeconds(2);
            options.Retry.UseJitter = true;

            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
        });

        return builder;
    }
}
