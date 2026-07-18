using System.Text.Json;
using Eswmp.Shared.Events;
using Eswmp.Work.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Work.Services;

/// <summary>
/// The transactional outbox's other half (api spec §11.3): polls requirement.OutboxMessages
/// for unprocessed rows and publishes them via MassTransit, marking ProcessedAt once
/// delivered. Runs outside any tenant HTTP request — a crash between the state-change commit
/// and this relay leaves the event pending rather than lost, which is what makes the
/// once-per-transition guarantee hold under at-least-once delivery.
/// </summary>
public class OutboxRelayService(IServiceScopeFactory scopeFactory, ILogger<OutboxRelayService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    private static readonly Dictionary<string, Type> EventTypeMap = new()
    {
        ["RequirementTemplateCreated"] = typeof(RequirementTemplateCreatedEvent),
        ["RequirementTemplateVersionCreated"] = typeof(RequirementTemplateVersionCreatedEvent),
        ["RequirementTemplateVersionActivated"] = typeof(RequirementTemplateVersionActivatedEvent),
        ["RequirementTemplateRetired"] = typeof(RequirementTemplateRetiredEvent),
        ["WorkRequirementCreated"] = typeof(WorkRequirementCreatedEvent),
        ["WorkRequirementResolved"] = typeof(WorkRequirementResolvedEvent),
        ["WorkRequirementChanged"] = typeof(WorkRequirementChangedEvent),
        ["WorkRequirementValidated"] = typeof(WorkRequirementValidatedEvent),
        ["WorkRequirementInvalidated"] = typeof(WorkRequirementInvalidatedEvent),
        ["WorkRequirementSuperseded"] = typeof(WorkRequirementSupersededEvent),
        ["WorkRequirementCancelled"] = typeof(WorkRequirementCancelledEvent),
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RelayPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox relay pass failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }
    }

    private async Task RelayPendingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pending = await db.WorkRequirementOutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            if (EventTypeMap.TryGetValue(message.EventType, out var clrType))
            {
                var payload = JsonSerializer.Deserialize(message.PayloadJson, clrType, RequirementResolutionService.JsonOptions);
                if (payload is not null)
                {
                    await publishEndpoint.Publish(payload, clrType, ct);
                }
            }
            else
            {
                logger.LogWarning("Unknown outbox EventType {EventType} for message {MessageId}; marking processed without publishing.", message.EventType, message.Id);
            }

            message.ProcessedAt = DateTimeOffset.UtcNow;
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
