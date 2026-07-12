using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Eswmp.Core.IntegrationTests;

/// <summary>
/// Boots the real Eswmp.Core host (Program.cs) against a real, disposable Postgres
/// container instead of the in-memory provider — Program.cs's own
/// "if Development: MigrateAsync()" startup step applies migrations for us.
/// </summary>
public class CoreApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("eswmp_core_test")
        .WithUsername("eswmp")
        .WithPassword("eswmp_test")
        .Build();

    public CoreApiFactory()
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
            });
        });
    }
}
