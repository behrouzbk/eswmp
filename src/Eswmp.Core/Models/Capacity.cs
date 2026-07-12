using Eswmp.Shared.DTOs;

namespace Eswmp.Core.Models;

public enum CapacityStatus
{
    Draft,
    Active,
    Suspended,
    Retired
}

/// <summary>
/// A named grouping of capacity definitions for a Resource — e.g. "Van #4's daily
/// capacity profile". One profile can carry several dimensions (concurrent jobs,
/// daily job count, cargo weight, ...) via its child <see cref="CapacityDefinition"/> rows.
/// </summary>
public class CapacityProfile : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required string Name { get; set; }
    public required string Timezone { get; set; }
    public CapacityStatus Status { get; set; } = CapacityStatus.Draft;

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; } = 1;
}

public enum CapacityModel
{
    Exclusive,
    Concurrent,
    Quantity,
    TimeBucket,
    DailyQuota
}

public enum CapacityUnit
{
    Count,
    Kg,
    Litre,
    Minute,
    Hour,
    CubicMetre
}

public enum CapacityTimeBasis
{
    Concurrent,
    PerBucket,
    PerDay,
    PerWeek
}

/// <summary>
/// One measurable capacity dimension on a profile — e.g. "CONCURRENT_WORK" capped
/// at 1, or "APPOINTMENT_COUNT" capped at 8/day.
/// </summary>
public class CapacityDefinition : TenantScopedEntity
{
    public required Guid CapacityProfileId { get; set; }
    public required string Name { get; set; }
    public CapacityModel CapacityModel { get; set; } = CapacityModel.Exclusive;
    public required string DimensionCode { get; set; }
    public int MaximumQuantity { get; set; } = 1;
    public CapacityUnit Unit { get; set; } = CapacityUnit.Count;
    public CapacityTimeBasis TimeBasis { get; set; } = CapacityTimeBasis.Concurrent;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public CapacityStatus Status { get; set; } = CapacityStatus.Draft;
    public int Version { get; set; } = 1;
}

public enum CapacityHoldStatus
{
    Active,
    Committed,
    Released,
    Expired,
    Cancelled
}

/// <summary>
/// A temporary reservation against a capacity dimension — mirrors the Reservation
/// hold/confirm split, but for capacity quantity rather than a resource's calendar.
/// </summary>
public class CapacityHold : TenantScopedEntity
{
    public required Guid CapacityDefinitionId { get; set; }
    public required Guid ResourceId { get; set; }
    public required string DimensionCode { get; set; }
    public required int Quantity { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public CapacityHoldStatus Status { get; set; } = CapacityHoldStatus.Active;
    public required DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Caller-supplied idempotency key — unique per tenant, prevents duplicate holds on retry.</summary>
    public required string IdempotencyKey { get; set; }

    /// <summary>Opaque caller-domain pointer, mirrors Reservation.ExternalReferenceType/Id — never inspected.</summary>
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public int Version { get; set; } = 1;
}

public enum CapacityConsumptionStatus
{
    Committed,
    Released
}

/// <summary>The durable record of capacity actually consumed — created when a Hold is committed.</summary>
public class CapacityConsumption : TenantScopedEntity
{
    public required Guid CapacityDefinitionId { get; set; }
    public required Guid ResourceId { get; set; }
    public required string DimensionCode { get; set; }
    public required int Quantity { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public CapacityConsumptionStatus Status { get; set; } = CapacityConsumptionStatus.Committed;
    public int Version { get; set; } = 1;
}

public enum CapacityOverrideEffect
{
    Replace,
    Increase,
    Decrease,
    Close
}

public enum CapacityOverrideStatus
{
    Draft,
    Active,
    Retired
}

/// <summary>A one-off adjustment to a capacity dimension's effective ceiling for a window — e.g. a holiday surge.</summary>
public class CapacityOverride : TenantScopedEntity
{
    public required Guid CapacityDefinitionId { get; set; }
    public required string OverrideType { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public int Quantity { get; set; }
    public CapacityOverrideEffect Effect { get; set; } = CapacityOverrideEffect.Replace;
    public string? ReasonCode { get; set; }
    public CapacityOverrideStatus Status { get; set; } = CapacityOverrideStatus.Draft;
}

public enum CapacityLedgerEntryType
{
    CapacityDefined,
    CapacityChanged,
    OverrideApplied,
    HoldAcquired,
    HoldReleased,
    ConsumptionCommitted,
    ConsumptionReleased
}

/// <summary>
/// Append-only audit trail of every state-changing capacity operation. Never
/// updated or deleted — write one row per mutation for traceability.
/// </summary>
public class CapacityLedgerEntry : TenantScopedEntity
{
    public required Guid CapacityDefinitionId { get; set; }
    public Guid? ResourceId { get; set; }
    public CapacityLedgerEntryType EntryType { get; set; }
    public string? DimensionCode { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
}
