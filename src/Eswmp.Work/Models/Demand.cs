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
    Expired,

    // v2 delta (UX-09/UX-14): appended last (ordinal 7), never inserted mid-list.
    // This enum has no HasConversion — it's a plain `integer` column (WorkDbContext) —
    // so inserting NeedsAttention at its "natural" spec position (between Ready and
    // Accepted) would silently reshuffle the stored ordinals of every status after it,
    // corrupting every existing row's meaning. Non-terminal: reachable from Received
    // (validation error) and from Accepted (downstream resolution failure) — see
    // Demand.AttentionReason and DemandRequirementLinkService.FlagResolutionFailedAsync.
    NeedsAttention
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

/// <summary>v2 delta (UX-10) — who owns fixing a demand that needs attention.</summary>
public enum DemandAttentionOwner
{
    Customer,
    Dispatcher,
    Administrator
}

/// <summary>v2 delta — split/merge provenance edge kind (demand.DemandLineage.Relation).</summary>
public enum DemandLineageRelation
{
    SplitFrom,
    MergedInto
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

    // ── v2 delta (UX-10): triage ownership ──────────────────────────────────
    /// <summary>Actor id or queue name currently responsible for a NeedsAttention demand.</summary>
    public string? AssignedTo { get; set; }
    public DemandAttentionOwner? AssignedRole { get; set; }

    // ── v2 delta (UX-09/UX-14): why this demand needs attention ─────────────
    /// <summary>e.g. VALIDATION_FAILED, RESOLUTION_FAILED. Required whenever Status == NeedsAttention (CK_Demands_AttentionReason).</summary>
    public string? AttentionReason { get; set; }
    /// <summary>jsonb — a snapshot of issues[] (or the resolution failure detail) at the time this was flagged.</summary>
    public string? AttentionIssuesJson { get; set; }

    // ── v2 delta (UX-14): resolution retry accounting ────────────────────────
    /// <summary>Counted, not capped — see DemandRequirementLinkService.FlagResolutionFailedAsync / the open D-02 product decision.</summary>
    public int ResolutionAttempts { get; set; }
    public string? LastResolutionError { get; set; }

    // ── v2 delta: recurrence made real, not just a FulfillmentMode label ─────
    /// <summary>RFC 5545 RRULE. Only meaningful when FulfillmentMode == Recurring (CK_Demands_Recurrence).</summary>
    public string? RecurrenceRule { get; set; }
    /// <summary>Groups instances of one recurring series.</summary>
    public Guid? SeriesId { get; set; }
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

/// <summary>
/// v2 delta — demand.DemandLineage: split and merge provenance. A split creates N
/// children from one parent; a merge points losers at a surviving demand. Recorded
/// rather than inferred so the audit trail can answer "where did this come from".
/// </summary>
public class DemandLineage : TenantScopedEntity
{
    /// <summary>The child (SplitFrom) or merged-away (MergedInto) demand.</summary>
    public Guid DemandId { get; set; }
    /// <summary>The parent (SplitFrom) or surviving (MergedInto) demand.</summary>
    public Guid RelatedId { get; set; }
    public DemandLineageRelation Relation { get; set; }
    public string? ActorId { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// v2 delta — demand.DemandAuditEntries: real audit trail, replacing the previously
/// always-empty GET /{id}/history. One row per state change or material mutation.
/// </summary>
public class DemandAuditEntry : TenantScopedEntity
{
    public Guid DemandId { get; set; }
    /// <summary>e.g. Created, Validated, Accepted, FlaggedForAttention, Assigned, Escalated, Split, Merged.</summary>
    public required string ChangeType { get; set; }
    public DemandStatus? FromStatus { get; set; }
    public DemandStatus? ToStatus { get; set; }
    public string? ActorId { get; set; }
    public string? ActorRole { get; set; }
    public string? CorrelationId { get; set; }
    public string? Reason { get; set; }
    /// <summary>jsonb — a small before/after summary (e.g. priority change on escalate), not the whole entity.</summary>
    public string? BeforeSummary { get; set; }
    public string? AfterSummary { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
