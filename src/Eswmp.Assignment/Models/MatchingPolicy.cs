using Eswmp.Shared.DTOs;

namespace Eswmp.Assignment.Models;

public enum MatchingPolicyStatus
{
    Active,
    Inactive,
}

/// <summary>
/// A tenant-owned, named matching configuration (e.g. "BALANCED",
/// "PROXIMITY_FIRST"). Holds one or more versioned factor configurations —
/// see MatchingPolicyVersion. Code is unique per tenant.
/// </summary>
public class MatchingPolicy : TenantScopedEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public MatchingPolicyStatus Status { get; set; } = MatchingPolicyStatus.Active;
    public int CurrentVersionNumber { get; set; }

    public List<MatchingPolicyVersion> Versions { get; set; } = [];
}

public enum MatchingPolicyVersionStatus
{
    Draft,
    Active,
    Superseded,
    Retired,
}

/// <summary>
/// One immutable-once-Active version of a MatchingPolicy's factor weights.
/// FactorConfigurationJson is a jsonb array of
/// { factorCode, enabled, weight, normalizationMethod }, parsed by
/// MatchingController into Services.MatchFactorWeight before scoring.
/// </summary>
public class MatchingPolicyVersion : TenantScopedEntity
{
    public required Guid MatchingPolicyId { get; set; }
    public required int VersionNumber { get; set; }
    public MatchingPolicyVersionStatus Status { get; set; } = MatchingPolicyVersionStatus.Draft;
    public required string StrategyCode { get; set; }
    public required string FactorConfigurationJson { get; set; }

    public MatchingPolicy? MatchingPolicy { get; set; }
}
