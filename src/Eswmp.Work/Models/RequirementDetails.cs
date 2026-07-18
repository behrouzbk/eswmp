using Eswmp.Shared.DTOs;

namespace Eswmp.Work.Models;

// ── Role-scoped requirements (model §3.1, §4.3, §4.4) ──────────────────────
// Capability, certification, and capacity requirements attach to a Resource Role — not to
// the work as a whole — which is what makes multi-resource work expressible (e.g. Mobile
// Grooming: DOG_GROOMING attaches to the Groomer role, a driving licence to the Vehicle role).

public enum ResourceCategory
{
    Person,
    Team,
    Vehicle,
    Facility,
    Room,
    Equipment,
    VirtualResource,
    ResourcePool
}

/// <summary>AnyOneOf is what allows "one of: Standard Van or Large Van" without demanding two vehicles.</summary>
public enum SelectionMode
{
    Single,
    Multiple,
    AnyOneOf,
    AllRequired,
    Optional
}

/// <summary>requirement.ResourceRoleRequirements (model §4.3) — the pivot of the model.</summary>
public class ResourceRoleRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }

    /// <summary>e.g. DOG_WALKER, GROOMER, VEHICLE.</summary>
    public required string RoleCode { get; set; }
    public ResourceCategory ResourceCategory { get; set; }
    public int MinimumQuantity { get; set; } = 1;
    public int? MaximumQuantity { get; set; }
    public bool Required { get; set; } = true;
    public SelectionMode SelectionMode { get; set; } = SelectionMode.Single;

    /// <summary>Same physical resource required across the whole work.</summary>
    public bool SameResourceRequired { get; set; }
    public int Sequence { get; set; }
}

/// <summary>requirement.CapabilityRequirements (model §4.4) — e.g. DOG_GROOMING.</summary>
public class CapabilityRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }

    /// <summary>Scoped to a role, or work-wide when null.</summary>
    public Guid? ResourceRoleRequirementId { get; set; }
    public required string CapabilityCode { get; set; }
    public string? Level { get; set; }
    public int? MinimumExperience { get; set; }
    public bool Mandatory { get; set; } = true;
    public string? Scope { get; set; }
}

/// <summary>requirement.CertificationRequirements (model §4.4) — e.g. ANIMAL_FIRST_AID; validity must cover the work period.</summary>
public class CertificationRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public Guid? ResourceRoleRequirementId { get; set; }
    public required string CertificationTypeCode { get; set; }
    public bool Mandatory { get; set; } = true;
    public DateTimeOffset? MustBeValidThrough { get; set; }
    public string? VerificationLevel { get; set; }
}

/// <summary>
/// requirement.CapacityRequirements (model §4.4) — how much of a dimension the work
/// consumes, e.g. PET_COUNT = 2. Never how much capacity remains — that's the Capacity
/// Service (model §1.3).
/// </summary>
public class CapacityRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public Guid? ResourceRoleRequirementId { get; set; }

    /// <summary>e.g. PET_COUNT.</summary>
    public required string DimensionCode { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>e.g. COUNT.</summary>
    public string? Unit { get; set; }
    public string? AggregationScope { get; set; }
    public bool Mandatory { get; set; } = true;
}

// ── Single-cardinality requirements (model §3.1, §4.5) ─────────────────────
// One per Work Requirement — enforced by a unique constraint on WorkRequirementId.

public enum DurationType
{
    Fixed,
    Estimated,
    Range,
    Derived
}

public class DurationRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public DurationType DurationType { get; set; }
    public int? EstimatedDurationMinutes { get; set; }
    public int? MinimumDurationMinutes { get; set; }
    public int? MaximumDurationMinutes { get; set; }
    public int? SetupDurationMinutes { get; set; }
    public int? CleanupDurationMinutes { get; set; }
}

public enum TimeConstraintType
{
    Flexible,
    Window,
    FixedStart,
    FixedInterval,
    Deadline
}

public class TimeRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public TimeConstraintType TimeConstraintType { get; set; }
    public DateTimeOffset? EarliestStart { get; set; }
    public DateTimeOffset? LatestStart { get; set; }
    public DateTimeOffset? EarliestFinish { get; set; }
    public DateTimeOffset? LatestFinish { get; set; }
    public DateTimeOffset? FixedStart { get; set; }
    public DateTimeOffset? FixedEnd { get; set; }
    public DateTimeOffset? Deadline { get; set; }

    /// <summary>IANA id.</summary>
    public string? Timezone { get; set; }
}

public enum LocationMode
{
    CustomerLocation,
    ProviderLocation,
    FacilityLocation,
    SpecificLocation,
    Remote
}

public class LocationRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public LocationMode LocationMode { get; set; }

    /// <summary>Opaque pointer — never inspected.</summary>
    public string? LocationReferenceType { get; set; }
    public string? LocationReferenceId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? ServiceRadius { get; set; }
    public string? LocationFlexibility { get; set; }
}

public enum ExecutionMode
{
    OnSite,
    Mobile,
    Remote,
    FacilityBased,
    Hybrid
}

public class ExecutionRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public ExecutionMode ExecutionMode { get; set; }
}

/// <summary>Describes travel constraints; does not calculate routes.</summary>
public class TravelRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public bool TravelRequired { get; set; }
    public string? OriginMode { get; set; }
    public string? DestinationMode { get; set; }
    public int? MaximumTravelTimeMinutes { get; set; }
    public decimal? MaximumTravelDistance { get; set; }
    public bool TravelTimeIncludedInWork { get; set; }
}

// ── Multi-cardinality requirements (model §3.2, §4.6) ───────────────────────
// Many per Work Requirement. Constraints gate (hard) or influence (soft); Preferences only
// weight desirability — they are never a gate.

public enum BufferType
{
    BeforeWork,
    AfterWork,
    Travel,
    Cleanup,
    Setup
}

public class BufferRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public BufferType BufferType { get; set; }
    public int DurationMinutes { get; set; }
    public string? AppliesToRole { get; set; }
    public bool HardConstraint { get; set; }
}

/// <summary>No cross-service FK — DependsOnReferenceType/Id is an opaque pointer.</summary>
public class DependencyRequirement : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public required string DependencyType { get; set; }
    public string? DependsOnReferenceType { get; set; }
    public string? DependsOnReferenceId { get; set; }
    public int? LagMinutes { get; set; }
    public bool HardConstraint { get; set; } = true;
}

/// <summary>requirement.Constraints (model §4.6). Hard = must hold (gate). Soft = should hold.</summary>
public class RequirementConstraint : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public required string ConstraintType { get; set; }
    public string? Scope { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public bool HardConstraint { get; set; } = true;
    public string? Reason { get; set; }
}

/// <summary>requirement.Preferences (model §4.6) — weighted desirability. Never a gate.</summary>
public class RequirementPreference : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public required string PreferenceType { get; set; }
    public string? Value { get; set; }
    public decimal? Weight { get; set; }

    /// <summary>e.g. Customer, Template.</summary>
    public string? Source { get; set; }
}
