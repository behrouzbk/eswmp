namespace Eswmp.Shared.Auth;

public static class EswmpClaimTypes
{
    public const string TenantId = "tenant_id";
    public const string Role = "eswmp_role";
    public const string Permissions = "permissions";
    public const string UserId = "user_id";
}

/// <summary>
/// Roles are deliberately generic — ESWMP does not know what business a tenant runs.
/// Consuming products map their own domain roles (ShopOwner, Dispatcher, Driver, ...)
/// onto these before minting a JWT.
/// </summary>
public static class EswmpRoles
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string Scheduler = "Scheduler";
    public const string Operator = "Operator";
    public const string ReadOnly = "ReadOnly";

    public static readonly string[] All = [PlatformAdmin, TenantAdmin, Scheduler, Operator, ReadOnly];
}

public static class EswmpPermissions
{
    // Resources
    public const string ResourceRead = "resource.read";
    public const string ResourceWrite = "resource.write";

    // Availability
    public const string AvailabilityRead = "availability.read";
    public const string AvailabilityWrite = "availability.write";

    // Capacity
    public const string CapacityRead = "capacity.read";
    public const string CapacityWrite = "capacity.write";

    // Reservations
    public const string ReservationCreate = "reservation.create";
    public const string ReservationRead = "reservation.read";
    public const string ReservationConfirm = "reservation.confirm";
    public const string ReservationCancel = "reservation.cancel";

    // Assignment
    public const string AssignmentRead = "assignment.read";
    public const string AssignmentExecute = "assignment.execute";

    // Matching
    public const string MatchingRead = "matching.read";
    public const string MatchingExecute = "matching.execute";

    // Rules / Workflow
    public const string RuleRead = "rule.read";
    public const string RuleWrite = "rule.write";
    public const string WorkflowTransition = "workflow.transition";

    // Demand Intake
    public const string DemandCreate = "demand.create";
    public const string DemandRead = "demand.read";
    public const string DemandTransition = "demand.transition";
    // v2 delta — flag-attention/retry-resolution/bulk accept/reject/cancel reuse
    // DemandTransition (same authority, just a different trigger or batched); metrics/
    // audit/history reuse DemandRead. These four are genuinely different authorities,
    // mirroring the Work Requirement module's precedent above of splitting authorities
    // that are "genuinely different" rather than reusing broad ones.
    public const string DemandAssign = "demand.assign";
    public const string DemandEscalate = "demand.escalate";
    public const string DemandSplit = "demand.split";
    public const string DemandMerge = "demand.merge";

    // Requirement Definition (first-generation Work Requirement model — see the
    // provenance note on Eswmp.Work.Models.RequirementDefinition)
    public const string RequirementDefinitionRead = "requirementdefinition.read";
    public const string RequirementDefinitionWrite = "requirementdefinition.write";

    // Work Requirement (docs/api/specs/02-work-requirement-api.md §10.2) — finer-grained
    // than Demand Intake because template authoring, resolution, and revision are
    // genuinely different authorities.
    public const string WorkRequirementTemplateCreate = "workrequirement.template.create";
    public const string WorkRequirementTemplateRead = "workrequirement.template.read";
    public const string WorkRequirementTemplateUpdate = "workrequirement.template.update";
    public const string WorkRequirementTemplateActivate = "workrequirement.template.activate";
    public const string WorkRequirementTemplateRetire = "workrequirement.template.retire";
    public const string WorkRequirementRead = "workrequirement.read";
    public const string WorkRequirementResolve = "workrequirement.resolve";
    public const string WorkRequirementRevise = "workrequirement.revise";
    public const string WorkRequirementValidate = "workrequirement.validate";
    public const string WorkRequirementExplain = "workrequirement.explain";
    public const string WorkRequirementOverrideRestrict = "workrequirement.override.restrict";
    public const string WorkRequirementOverrideRelax = "workrequirement.override.relax";
    public const string WorkRequirementAdmin = "workrequirement.admin";

    // Admin
    public const string AdminAll = "admin.all";
    public const string AdminTenants = "admin.tenants";
}
