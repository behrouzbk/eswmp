using Eswmp.Shared.DTOs;

namespace Eswmp.Core.Models;

public enum ResourceBaseType
{
    Person,
    Team,
    Vehicle,
    Room,
    Facility,
    Equipment,
    Virtual,
    Other
}

public enum ResourceTypeStatus
{
    Active,
    Inactive
}

/// <summary>
/// A tenant-defined resource classification (e.g. "GroomingVan", "DeliveryDriver").
/// Purely descriptive metadata — ESWMP never branches business logic on this beyond
/// what the tenant configures via <c>BusinessRule</c>/<c>DurationTagRule</c> rows.
/// </summary>
public class ResourceType : TenantScopedEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public ResourceBaseType BaseType { get; set; } = ResourceBaseType.Other;
    public string? Description { get; set; }
    public ResourceTypeStatus Status { get; set; } = ResourceTypeStatus.Active;
}

public enum ResourceCapabilityStatus
{
    Pending,
    Active,
    Suspended,
    Expired,
    Revoked
}

/// <summary>A capability a Resource has been granted — e.g. "HEAVY_LIFTING" at level 3.</summary>
public class ResourceCapability : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required string CapabilityCode { get; set; }
    public int Level { get; set; } = 1;
    public ResourceCapabilityStatus Status { get; set; } = ResourceCapabilityStatus.Pending;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>A skill tag a Resource has been assigned, with proficiency metadata used by Assignment scoring.</summary>
public class ResourceSkill : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required string SkillCode { get; set; }
    public int Level { get; set; } = 1;
    public decimal? YearsOfExperience { get; set; }
    public bool Verified { get; set; }
}

public enum ResourceCertificationStatus
{
    Submitted,
    PendingVerification,
    Valid,
    Rejected,
    Expired,
    Revoked
}

/// <summary>A certification/credential record attached to a Resource.</summary>
public class ResourceCertification : TenantScopedEntity
{
    public required Guid ResourceId { get; set; }
    public required string CertificationTypeCode { get; set; }
    public string? CredentialReference { get; set; }
    public DateOnly? IssuedAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public ResourceCertificationStatus Status { get; set; } = ResourceCertificationStatus.Submitted;
}
