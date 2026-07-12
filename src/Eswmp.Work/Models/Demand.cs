using Eswmp.Shared.DTOs;

namespace Eswmp.Work.Models;

/// <summary>
/// The lifecycle a Demand moves through from raw intake to a state that can be
/// handed off to become (or be linked to) a WorkRequirement / scheduling flow.
/// See docs/api "Demand Intake #01".
/// </summary>
public enum DemandStatus
{
    Received,
    Validating,
    Ready,
    Accepted,
    Rejected,
    Cancelled,
    Expired
}

public enum DemandPriority
{
    Low,
    Normal,
    High,
    Urgent,
    Critical
}

/// <summary>
/// The timing axis — how a demand is fulfilled in time. Orthogonal to DemandType
/// (the nature-of-work axis, caller-defined): any DemandType can pair with any
/// FulfillmentMode. Platform-owned and closed, because matching/booking/notification
/// logic branches on it. See docs/api/specs/01-demand-intake-model-v2.md §3.2.
/// </summary>
public enum DemandFulfillmentMode
{
    // Scheduled is first (ordinal 0, the CLR default) deliberately — it's also the
    // configured DB default (WorkDbContext), so an unset property and an explicit
    // Scheduled agree. If OnDemand were 0 instead, EF would treat an explicit OnDemand
    // as "unset" (matches the CLR default sentinel) and silently persist the DB default
    // (Scheduled) instead — a real data-corruption trap, not just a style nit.
    Scheduled,
    OnDemand,
    Recurring,
    Standby
}

/// <summary>
/// A raw unit of intake from a caller's own domain (an opaque
/// ExternalReferenceType/ExternalReferenceId pair — this platform never inspects
/// what a "demand" means to the caller). Mirrors the Reservation opaque-pointer
/// pattern in Eswmp.Core — see CLAUDE.md rule 1.
/// </summary>
public class Demand : TenantScopedEntity
{
    public Guid? OrganizationId { get; set; }
    public required string DemandType { get; set; }
    public DemandFulfillmentMode FulfillmentMode { get; set; } = DemandFulfillmentMode.Scheduled;
    public required string SourceSystem { get; set; }
    public string? SourceChannel { get; set; }
    public DemandStatus Status { get; set; } = DemandStatus.Received;
    public DemandPriority Priority { get; set; } = DemandPriority.Normal;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? RequestedStartAtUtc { get; set; }
    public DateTimeOffset? RequestedEndAtUtc { get; set; }
    public string? RequestedTimezone { get; set; }

    /// <summary>Opaque JSON blob — no Location service exists yet in this platform.</summary>
    public string? LocationReference { get; set; }

    /// <summary>FK-like pointer to a WorkRequirement, nullable — set once this demand
    /// has been matched to a requirement definition.</summary>
    public Guid? RequirementReferenceId { get; set; }

    public required string ExternalReferenceType { get; set; }
    public required string ExternalReferenceId { get; set; }

    /// <summary>Optimistic-concurrency counter, required as `expectedVersion` on PATCH.</summary>
    public int Version { get; set; } = 1;
}

public enum DemandValidationStatus
{
    Invalid,
    ValidWithWarnings,
    Valid
}

public class DemandValidationResult : TenantScopedEntity
{
    public Guid DemandId { get; set; }
    public DemandValidationStatus Status { get; set; }
    public DateTimeOffset ValidatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>jsonb — a serialized list of { code, severity, message }.</summary>
    public required string IssuesJson { get; set; }
}

/// <summary>
/// Backs POST /api/v1/demands' required Idempotency-Key header: the same key with
/// the same effective request body replays the original response; the same key with
/// a different body is rejected as a conflict.
/// </summary>
public class DemandIdempotencyRecord : TenantScopedEntity
{
    public required string IdempotencyKey { get; set; }
    public required string RequestHash { get; set; }
    public Guid DemandId { get; set; }
    public required string ResponseBodyJson { get; set; }
}
