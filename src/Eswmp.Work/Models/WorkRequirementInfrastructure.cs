using Eswmp.Shared.DTOs;

namespace Eswmp.Work.Models;

/// <summary>
/// requirement.IdempotencyRecords (model §4.7, api §11.1) — mirrors DemandIdempotencyRecord
/// exactly, so both services in the Demand & Intake domain behave identically. Required for
/// template creation, template version creation, resolution, revision, override, cancellation.
/// </summary>
public class WorkRequirementIdempotencyRecord : TenantScopedEntity
{
    public required string IdempotencyKey { get; set; }
    public required string RequestHash { get; set; }

    /// <summary>Which operation the key was used for — e.g. "resolve", "template.create".</summary>
    public required string Operation { get; set; }

    /// <summary>The created/affected aggregate, if any.</summary>
    public Guid? ResourceId { get; set; }
    public required string ResponseBodyJson { get; set; }
}

/// <summary>
/// requirement.OutboxMessages (model §4.7, api §11.3) — transactional outbox. State change
/// and outbox row commit together in the same SaveChanges call; a background relay
/// (<see cref="Eswmp.Work.Services.OutboxRelayService"/>) publishes afterward, so a crash
/// between commit and publish leaves the event pending rather than lost.
/// </summary>
public class WorkRequirementOutboxMessage : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>e.g. WorkRequirementResolved.</summary>
    public required string EventType { get; set; }
    public required string AggregateType { get; set; }
    public Guid AggregateId { get; set; }

    /// <summary>jsonb — the serialized event payload.</summary>
    public required string PayloadJson { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}
