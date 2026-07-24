using Eswmp.Work.Models;

namespace Eswmp.Work.Services;

// The wire shape of "what a template resolves to" — used identically for the PUT
// .../templates/{id}/versions/{version}/requirements request body (api spec §4) and the
// GET /work-requirements/{id}/resolved response (api spec §3.2), so template authoring and
// resolved-contract reads speak the same shape. RoleCode (not an id) is how capability/
// certification/capacity requirements attach to a resource role — resolved into a real
// ResourceRoleRequirement.Id by RequirementResolutionService when a template is materialized.

// Id/VisibilityLevel (v2 delta, UX-03/UX-04): trailing optional fields on every per-line DTO so
// the same shape can (a) let a template author set a line's disclosure level at configure-time
// and (b) round-trip that line's live entity Id + resolved VisibilityLevel back out on
// GET .../resolved and into RequirementVersion.SnapshotJson — see
// RequirementResolutionService.BuildVisibility/FilterByAudience. Both default to null/unset so
// existing payloads and stored JSON are unaffected.

public record ResourceRoleRequirementDto(
    string RoleCode,
    ResourceCategory ResourceCategory,
    int MinimumQuantity = 1,
    int? MaximumQuantity = null,
    bool Required = true,
    SelectionMode SelectionMode = SelectionMode.Single,
    bool SameResourceRequired = false,
    int Sequence = 0,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record CapabilityRequirementDto(
    string CapabilityCode,
    string? RoleCode = null,
    string? Level = null,
    int? MinimumExperience = null,
    bool Mandatory = true,
    string? Scope = null,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record CertificationRequirementDto(
    string CertificationTypeCode,
    string? RoleCode = null,
    bool Mandatory = true,
    DateTimeOffset? MustBeValidThrough = null,
    string? VerificationLevel = null,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record CapacityRequirementDto(
    string DimensionCode,
    decimal Quantity,
    string? RoleCode = null,
    string? Unit = null,
    string? AggregationScope = null,
    bool Mandatory = true,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record DurationRequirementDto(
    DurationType DurationType,
    int? EstimatedDurationMinutes = null,
    int? MinimumDurationMinutes = null,
    int? MaximumDurationMinutes = null,
    int? SetupDurationMinutes = null,
    int? CleanupDurationMinutes = null,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record TimeRequirementDto(
    TimeConstraintType TimeConstraintType,
    DateTimeOffset? EarliestStart = null,
    DateTimeOffset? LatestStart = null,
    DateTimeOffset? EarliestFinish = null,
    DateTimeOffset? LatestFinish = null,
    DateTimeOffset? FixedStart = null,
    DateTimeOffset? FixedEnd = null,
    DateTimeOffset? Deadline = null,
    string? Timezone = null,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record LocationRequirementDto(
    LocationMode LocationMode,
    string? LocationReferenceType = null,
    string? LocationReferenceId = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    decimal? ServiceRadius = null,
    string? LocationFlexibility = null,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record ExecutionRequirementDto(
    ExecutionMode ExecutionMode,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record TravelRequirementDto(
    bool TravelRequired = false,
    string? OriginMode = null,
    string? DestinationMode = null,
    int? MaximumTravelTimeMinutes = null,
    decimal? MaximumTravelDistance = null,
    bool TravelTimeIncludedInWork = false,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record BufferRequirementDto(
    BufferType BufferType,
    int DurationMinutes,
    string? AppliesToRole = null,
    bool HardConstraint = false,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record DependencyRequirementDto(
    string DependencyType,
    string? DependsOnReferenceType = null,
    string? DependsOnReferenceId = null,
    int? LagMinutes = null,
    bool HardConstraint = true,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record ConstraintDto(
    string ConstraintType,
    string? Scope = null,
    string? Operator = null,
    string? Value = null,
    bool HardConstraint = true,
    string? Reason = null,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

public record PreferenceDto(
    string PreferenceType,
    string? Value = null,
    decimal? Weight = null,
    string? Source = null,
    Guid? Id = null,
    VisibilityLevel? VisibilityLevel = null);

/// <summary>Required: resourceRequirements, durationRequirement (api spec §3.2). Everything else optional.</summary>
public record RequirementSetDto(
    List<ResourceRoleRequirementDto> ResourceRequirements,
    DurationRequirementDto DurationRequirement,
    List<CapabilityRequirementDto>? CapabilityRequirements = null,
    List<CertificationRequirementDto>? CertificationRequirements = null,
    List<CapacityRequirementDto>? CapacityRequirements = null,
    TimeRequirementDto? TimeRequirement = null,
    LocationRequirementDto? LocationRequirement = null,
    ExecutionRequirementDto? ExecutionRequirement = null,
    TravelRequirementDto? TravelRequirement = null,
    List<BufferRequirementDto>? BufferRequirements = null,
    List<DependencyRequirementDto>? DependencyRequirements = null,
    List<ConstraintDto>? Constraints = null,
    List<PreferenceDto>? Preferences = null);
