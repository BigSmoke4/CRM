using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Crm.Web.Services;

public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var isSuper = context.User.FindFirst("is_superadmin")?.Value == "true"
                      || context.User.IsInRole("SuperAdmin");

        if (isSuper ||
            context.User.HasClaim("permission", requirement.Permission) ||
            context.User.HasClaim("permission", Crm.Data.Permissions.All))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

public static class AuthorizationExtensions
{
    /// <summary>Requires an authenticated user holding the given permission claim.</summary>
    public static AuthorizationPolicyBuilder RequirePermission(this AuthorizationPolicyBuilder policy, string permission)
        => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission));
}
