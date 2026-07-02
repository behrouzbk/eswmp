using Microsoft.AspNetCore.Authorization;

namespace Eswmp.Shared.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public string Permission { get; }

    public RequirePermissionAttribute(string permission) : base(policy: $"Permission:{permission}")
    {
        Permission = permission;
    }
}
