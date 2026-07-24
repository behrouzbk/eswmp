using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Eswmp.Work.IntegrationTests;

/// <summary>
/// Boots the real Eswmp.Work host (Program.cs) against a real, disposable Postgres
/// container instead of the in-memory provider — Program.cs's own
/// "if Development: MigrateAsync()" startup step applies migrations for us.
/// Mirrors Eswmp.Core.IntegrationTests/CoreApiFactory.cs.
/// </summary>
public class WorkApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("eswmp_work_test")
        .WithUsername("eswmp")
        .WithPassword("eswmp_test")
        .Build();

    public WorkApiFactory()
    {
        // AddEswmpAuthentication reads Jwt:SecretKey into a plain `string` synchronously,
        // before WebApplicationBuilder.Build() runs — too early for a ConfigureAppConfiguration
        // override (added at Build() time) to reach it. Env vars are read as part of the very
        // first configuration pass, so they're visible in time. (ConnectionStrings:Default is
        // read lazily inside AddDbContext's options callback, so the ConfigureAppConfiguration
        // override below is sufficient for that one.)
        Environment.SetEnvironmentVariable("Jwt__SecretKey", TestJwtFactory.SecretKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestJwtFactory.Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestJwtFactory.Audience);
    }

    Task IAsyncLifetime.InitializeAsync() => _postgres.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                ["Jwt:SecretKey"] = TestJwtFactory.SecretKey,
                ["Jwt:Issuer"] = TestJwtFactory.Issuer,
                ["Jwt:Audience"] = TestJwtFactory.Audience,
                // Without this, MassTransit's RabbitMQ bus tries the default localhost:5672,
                // where nothing listens (docker-compose maps eswmp-rabbitmq to host port 6673)
                // — every test that calls IPublishEndpoint.Publish (Accept/Reject/Cancel/Validate
                // and the v2 delta's flag-attention/retry-resolution/assign/escalate/bulk/split/
                // merge) then hangs for the connection's full retry/backoff window instead of
                // failing fast or succeeding. Unlike Jwt:SecretKey above, MassTransit's actual
                // RabbitMQ connection is built lazily (when the bus is resolved at startup, not
                // at AddMassTransit() registration time in Program.cs), so it does see this
                // ConfigureAppConfiguration override.
                ["MessageBus:Port"] = "6673",
            });
        });
    }
}
