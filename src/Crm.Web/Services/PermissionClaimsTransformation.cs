using System.Security.Claims;
using Crm.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ApplicationUserEntity = Crm.Data.Entities.ApplicationUser;

namespace Crm.Web.Services;

/// <summary>
/// Adds "permission" claims to the principal based on the user's roles.
/// Role -> permission mapping is static (RolePermissions) plus any custom
/// permission claims stored on the role itself (AspNetRoleClaims, ClaimType="permission").
/// </summary>
public class PermissionClaimsTransformation : IClaimsTransformation
{
    private readonly UserManager<ApplicationUserEntity> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public PermissionClaimsTransformation(UserManager<ApplicationUserEntity> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return principal;

        if (identity.HasClaim(c => c.Type == "permission"))
            return principal;

        var user = await _userManager.GetUserAsync(principal);
        if (user == null) return principal;

        var roles = await _userManager.GetRolesAsync(user);
        var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in roles)
        {
            if (role == "SuperAdmin")
            {
                foreach (var p in Permissions.AllPermissions) perms.Add(p);
                continue;
            }
            foreach (var p in RolePermissions.ForRole(role)) perms.Add(p);

            var identityRole = await _roleManager.FindByNameAsync(role);
            if (identityRole != null)
            {
                var roleClaims = await _roleManager.GetClaimsAsync(identityRole);
                foreach (var c in roleClaims.Where(c => c.Type == "permission"))
                    perms.Add(c.Value);
            }
        }

        foreach (var p in perms)
            identity.AddClaim(new Claim("permission", p));

        if (user.OrganizationId.HasValue)
            identity.AddClaim(new Claim("org_id", user.OrganizationId.Value.ToString()));
        if (user.OrganizationId == null && roles.Contains("SuperAdmin"))
            identity.AddClaim(new Claim("is_superadmin", "true"));

        return principal;
    }
}
