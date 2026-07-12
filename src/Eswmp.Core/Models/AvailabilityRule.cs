using Eswmp.Shared.DTOs;

namespace Eswmp.Core.Models;

public enum AvailabilityProfileStatus
{
    Draft,
    Active,
    Suspended,
    Retired
}

/// <summary>
/// The root of a Resource's availability configuration. One profile per Resource.
/// <see cref="AvailabilityVersion"/> is bumped on every material change to the
/// resolved availability picture (rule/exception/time-off/override added, changed,
/// or retired) — callers can use it as a cheap cache-invalidation signal, distinct
/// from the row-level optimistic-concurrency <c>Version</c> used elsewhere.
/// </summary>
public class AvailabilityProfile : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required string Name { get; set; }
    public required string Timezone { get; set; }
    public AvailabilityProfileStatus Status { get; set; } = AvailabilityProfileStatus.Active;
    public int AvailabilityVersion { get; set; } = 1;
}

public enum AvailabilityRuleType
{
    RegularWorkingHours,
    Shift,
    SeasonalSchedule,
    TemporarySchedule,
    AdditionalAvailability
}

public enum AvailabilityRuleStatus
{
    Active,
    Retired
}

/// <summary>Recurring weekly availability window for a Resource.</summary>
public class AvailabilityRule : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required DayOfWeek DayOfWeek { get; set; }
    public required TimeOnly StartTime { get; set; }
    public required TimeOnly EndTime { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Optional link to the owning profile — null for rules created before profiles existed.</summary>
    public Guid? AvailabilityProfileId { get; set; }

    public AvailabilityRuleType RuleType { get; set; } = AvailabilityRuleType.RegularWorkingHours;

    /// <summary>
    /// JSON-encoded recurrence: <c>{ "frequency": "Weekly", "interval": 1, "daysOfWeek": [1,2,3] }</c>.
    /// Frequency is Daily/Weekly/SelectedWeekdays only — no full RRULE grammar for MVP.
    /// Optional; when absent, DayOfWeek/StartTime/EndTime above are authoritative.
    /// </summary>
    public string? RecurrencePattern { get; set; }

    public int Priority { get; set; }
    public AvailabilityRuleStatus Status { get; set; } = AvailabilityRuleStatus.Active;
    public string? Source { get; set; }
}

public enum AvailabilityExceptionType
{
    Unavailable,
    AdditionalAvailability,
    ModifiedAvailability
}

/// <summary>
/// A one-off exception to the recurring rule — time off, a holiday, an
/// emergency block. Named AvailabilityException rather than "TimeOff" so it
/// reads naturally for non-workforce resources too (a room under maintenance,
/// a van in for service).
/// </summary>
public class AvailabilityException : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public required string Reason { get; set; }

    public AvailabilityExceptionType ExceptionType { get; set; } = AvailabilityExceptionType.Unavailable;
    public int Priority { get; set; }
}

public enum TimeOffApprovalStatus
{
    Draft,
    PendingApproval,
    Approved,
    Rejected,
    Cancelled
}

/// <summary>A formally requested/approved block of time off for a Resource, distinct from ad-hoc exceptions.</summary>
public class TimeOff : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required string Type { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public string? ReasonCode { get; set; }
    public TimeOffApprovalStatus ApprovalStatus { get; set; } = TimeOffApprovalStatus.Draft;
}

public enum AvailabilityOverrideEffect
{
    ForceAvailable,
    ForceUnavailable
}

public enum AvailabilityOverrideStatus
{
    Active,
    Retired
}

/// <summary>A manual, highest-priority (below hard unavailability) override of a Resource's computed availability.</summary>
public class AvailabilityOverride : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public AvailabilityOverrideEffect Effect { get; set; } = AvailabilityOverrideEffect.ForceAvailable;
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public string? ReasonCode { get; set; }
    public AvailabilityOverrideStatus Status { get; set; } = AvailabilityOverrideStatus.Active;
}
