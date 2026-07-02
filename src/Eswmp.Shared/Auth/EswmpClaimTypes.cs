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

    // Reservations
    public const string ReservationCreate = "reservation.create";
    public const string ReservationRead = "reservation.read";
    public const string ReservationConfirm = "reservation.confirm";
    public const string ReservationCancel = "reservation.cancel";

    // Assignment
    public const string AssignmentRead = "assignment.read";
    public const string AssignmentExecute = "assignment.execute";

    // Rules / Workflow
    public const string RuleRead = "rule.read";
    public const string RuleWrite = "rule.write";
    public const string WorkflowTransition = "workflow.transition";

    // Admin
    public const string AdminAll = "admin.all";
    public const string AdminTenants = "admin.tenants";
}
