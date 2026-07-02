using Microsoft.AspNetCore.Http;
using Eswmp.Shared.Auth;

namespace Eswmp.Shared.Middleware;

public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var tenantIdClaim = context.User.FindFirst(EswmpClaimTypes.TenantId);
        if (tenantIdClaim is not null && Guid.TryParse(tenantIdClaim.Value, out var tenantId))
        {
            tenantContext.TenantId = tenantId;
        }

        await next(context);
    }
}

public interface ITenantContext
{
    Guid? TenantId { get; set; }

    Guid RequiredTenantId => TenantId
        ?? throw new InvalidOperationException("TenantId is required but was not resolved from JWT.");
}

public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
}
