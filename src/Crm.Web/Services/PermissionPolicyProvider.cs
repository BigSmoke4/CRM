using Crm.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Crm.Web.Services;

/// <summary>
/// Treats any policy name matching a known permission (or containing a dot)
/// as a permission policy requiring that claim. Unknown names fall back to
/// default policy resolution.
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Permission policies look like "contacts.view". Anything else defers to defaults.
        if (policyName.Contains('.') &&
            (Permissions.AllPermissions.Contains(policyName, StringComparer.OrdinalIgnoreCase) ||
             policyName == Permissions.All))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return _fallback.GetPolicyAsync(policyName);
    }
}
