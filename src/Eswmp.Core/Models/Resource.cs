using Eswmp.Shared.DTOs;

namespace Eswmp.Core.Models;

/// <summary>
/// Resource lifecycle. Expanded from the original 3-state (Active/Inactive/Suspended)
/// enum to a full onboarding-through-retirement lifecycle — see the architecture
/// reconciliation plan for the target-state Resource module.
/// </summary>
public enum ResourceStatus
{
    Draft,
    PendingVerification,
    Active,
    Suspended,
    Inactive,
    Retired
}

/// <summary>Whether a Resource has completed whatever verification workflow the tenant requires.</summary>
public enum VerificationStatus
{
    NotRequired,
    NotStarted,
    Pending,
    Verified,
    Rejected,
    Expired
}

/// <summary>
/// The generic schedulable unit. "Never schedule employees, schedule resources."
/// A Resource might be a person, a vehicle, a room, or a piece of equipment —
/// ESWMP itself never assumes which. <see cref="ResourceType"/> is a caller-defined
/// label (e.g. "GroomingVan", "MeetingRoom", "DeliveryDriver"), never hardcoded here.
/// </summary>
public class Resource : TenantScopedEntity
{
    /// <summary>Free-text type label — kept for backward compatibility with Assignment scoring's filters.</summary>
    public required string ResourceType { get; set; }

    /// <summary>Optional link to a tenant-defined <see cref="Models.ResourceType"/> row (resource schema).</summary>
    public Guid? ResourceTypeId { get; set; }

    public required string Name { get; set; }
    public required string Timezone { get; set; }
    public ResourceStatus Status { get; set; } = ResourceStatus.Active;
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.NotRequired;

    /// <summary>Optimistic-concurrency token for lifecycle transitions.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Generic capacity metric — party size, seats, concurrent jobs, etc.</summary>
    public int? Capacity { get; set; }

    /// <summary>Comma-separated skill/specialisation tags matched by Assignment scoring.</summary>
    public string? Skills { get; set; }

    public double? LocationLatitude { get; set; }
    public double? LocationLongitude { get; set; }
}
