using Eswmp.Shared.Extensions;
using Eswmp.Shared.Infrastructure;
using Eswmp.Shared.Middleware;
using Eswmp.Work.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Work")
    .WriteTo.Console());

// ── EF Core (PostgreSQL) ──────────────────────────────────────
builder.Services.AddDbContext<WorkDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default"),
        npgsql => npgsql.EnableRetryOnFailure(3)));

// ── JWT + RBAC ────────────────────────────────────────────────
builder.Services.AddEswmpAuthentication(builder.Configuration);
builder.Services.AddEswmpAuthorization();

// ── MassTransit ───────────────────────────────────────────────
builder.Services.AddEswmpMessageBus(builder.Configuration);

// ── OpenTelemetry ─────────────────────────────────────────────
builder.Services.AddEswmpObservability(builder.Configuration);

// ── NSwag / OpenAPI ───────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "ESWMP Work API";
    config.Version = "v1";
});

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins")
                       .Get<string[]>() ?? ["http://localhost:5173"])
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

// ── Health Checks ─────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<WorkDbContext>("db", tags: ["ready"])
    .AddEswmpInfrastructureChecks(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}

app.MapControllers();
app.MapEswmpHealthChecks();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WorkDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program { }
