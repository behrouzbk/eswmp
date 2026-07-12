using Eswmp.Shared.DTOs;

namespace Eswmp.Work.Models;

/// <summary>
/// See docs/api "Work Requirement #02". A WorkRequirement is the stable, named
/// definition of "what kind of work this is" (resource/capability/skill/certification
/// needs, duration, location constraints) — versioned so that in-flight work keeps
/// referencing the version it was created against even as the definition evolves.
/// </summary>
public enum WorkRequirementStatus
{
    Draft,
    Active,
    Inactive,
    Retired
}

public enum RequirementVersionStatus
{
    Draft,
    Validated,
    Active,
    Superseded,
    Retired
}

public enum DurationType
{
    Fixed,
    Range
}

public enum CapabilityImportance
{
    Mandatory,
    Preferred,
    Optional
}

public enum LocationConstraintMode
{
    FixedLocation,
    CustomerLocation,
    ResourceLocation,
    Virtual,
    Flexible
}

public class WorkRequirement : TenantScopedEntity
{
    /// <summary>Unique per tenant.</summary>
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public WorkRequirementStatus Status { get; set; } = WorkRequirementStatus.Draft;
    public int CurrentVersionNumber { get; set; }
    public int? ActiveVersionNumber { get; set; }

    /// <summary>
    /// Optimistic-concurrency counter used as `expectedVersion` on version-activate/retire
    /// commands. Distinct from the domain's own per-version `VersionNumber` sequence.
    /// </summary>
    public int ConcurrencyVersion { get; set; } = 1;

    public List<RequirementVersion> Versions { get; set; } = [];
}

/// <summary>
/// Immutable once Status is Active/Superseded/Retired — the controller rejects PATCH
/// against any version that isn't still Draft.
/// </summary>
public class RequirementVersion : TenantScopedEntity
{
    public Guid WorkRequirementId { get; set; }
    public int VersionNumber { get; set; }
    public RequirementVersionStatus Status { get; set; } = RequirementVersionStatus.Draft;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? ChangeSummary { get; set; }

    public DurationType DurationType { get; set; }
    public int? FixedDurationMinutes { get; set; }
    public int? MinimumDurationMinutes { get; set; }
    public int? ExpectedDurationMinutes { get; set; }
    public int? MaximumDurationMinutes { get; set; }
    public int PreWorkBufferMinutes { get; set; }
    public int PostWorkBufferMinutes { get; set; }

    public List<ResourceRequirement> ResourceRequirements { get; set; } = [];
    public List<LocationConstraint> LocationConstraints { get; set; } = [];
}

/// <summary>
/// A frozen copy of a RequirementVersion (+ its children) at a point in time —
/// e.g. taken when a Demand is accepted against it, so later edits to the live
/// version never retroactively change already-accepted work. Immutable once
/// created; there is no update endpoint.
/// </summary>
public class RequirementSnapshot : TenantScopedEntity
{
    public Guid SourceRequirementId { get; set; }
    public int SourceVersionNumber { get; set; }

    /// <summary>jsonb — a serialized copy of the RequirementVersion + its children at freeze time.</summary>
    public required string DefinitionJson { get; set; }
    public string? Reason { get; set; }
}

public class ResourceRequirement : BaseEntity
{
    public Guid RequirementVersionId { get; set; }
    public required string ResourceTypeCode { get; set; }
    public string? Role { get; set; }
    public int MinimumQuantity { get; set; }
    public int PreferredQuantity { get; set; }
    public int MaximumQuantity { get; set; }
    public bool Mandatory { get; set; }

    public List<CapabilityRequirement> CapabilityRequirements { get; set; } = [];
    public List<SkillRequirement> SkillRequirements { get; set; } = [];
    public List<CertificationRequirement> CertificationRequirements { get; set; } = [];
}

public class CapabilityRequirement : BaseEntity
{
    public Guid ResourceRequirementId { get; set; }
    public required string CapabilityCode { get; set; }
    public int? MinimumLevel { get; set; }
    public CapabilityImportance Importance { get; set; } = CapabilityImportance.Preferred;
}

public class SkillRequirement : BaseEntity
{
    public Guid ResourceRequirementId { get; set; }
    public required string SkillCode { get; set; }
    public int? MinimumLevel { get; set; }
    public bool Mandatory { get; set; }
}

public class CertificationRequirement : BaseEntity
{
    public Guid ResourceRequirementId { get; set; }
    public required string CertificationTypeCode { get; set; }
    public bool Mandatory { get; set; }
}

public class LocationConstraint : BaseEntity
{
    public Guid RequirementVersionId { get; set; }
    public LocationConstraintMode Mode { get; set; }
    public decimal? MaximumTravelDistanceKm { get; set; }
    public int? MaximumTravelTimeMinutes { get; set; }
}
