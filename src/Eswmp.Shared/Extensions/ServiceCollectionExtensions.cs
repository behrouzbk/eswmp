using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Eswmp.Shared.Auth;
using Eswmp.Shared.Middleware;
using Serilog;
using System.Text;

namespace Eswmp.Shared.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Validates JWTs issued by whichever product embeds this platform.
    /// ESWMP does not issue its own tokens or run its own login system —
    /// see CLAUDE.md "Relationship to PetZiv" for why that's a deliberate scope cut.
    /// </summary>
    public static IServiceCollection AddEswmpAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var secretKey = jwtSection["SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey is not configured.");

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidAudience = jwtSection["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        return services;
    }

    public static IServiceCollection AddEswmpAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddEswmpSerilog(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console()
            .WriteTo.ApplicationInsights(
                configuration["ApplicationInsights:ConnectionString"] ?? string.Empty,
                TelemetryConverter.Traces)
            .CreateLogger();

        services.AddLogging(lb => lb.AddSerilog(dispose: true));
        return services;
    }
}
