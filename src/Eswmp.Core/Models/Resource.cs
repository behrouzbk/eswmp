using Eswmp.Shared.DTOs;

namespace Eswmp.Core.Models;

public enum ResourceStatus
{
    Active,
    Inactive,
    Suspended
}

/// <summary>
/// The generic schedulable unit. "Never schedule employees, schedule resources."
/// A Resource might be a person, a vehicle, a room, or a piece of equipment —
/// ESWMP itself never assumes which. <see cref="ResourceType"/> is a caller-defined
/// label (e.g. "GroomingVan", "MeetingRoom", "DeliveryDriver"), never hardcoded here.
/// </summary>
public class Resource : TenantScopedEntity
{
    public required string ResourceType { get; set; }
    public required string Name { get; set; }
    public required string Timezone { get; set; }
    public ResourceStatus Status { get; set; } = ResourceStatus.Active;

    /// <summary>Generic capacity metric — party size, seats, concurrent jobs, etc.</summary>
    public int? Capacity { get; set; }

    /// <summary>Comma-separated skill/specialisation tags matched by Assignment scoring.</summary>
    public string? Skills { get; set; }

    public double? LocationLatitude { get; set; }
    public double? LocationLongitude { get; set; }
}
