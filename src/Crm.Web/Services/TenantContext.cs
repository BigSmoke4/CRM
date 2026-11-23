using System.Security.Claims;
using Crm.Data;

namespace Crm.Web.Services;

/// <summary>Scoped tenant context populated from the authenticated user's claims.</summary>
public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;

    public TenantContext(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public bool IsSuperAdmin =>
        User?.IsInRole("SuperAdmin") == true ||
        User?.FindFirst("is_superadmin")?.Value == "true";

    public Guid? OrganizationId
    {
        get
        {
            var v = User?.FindFirst("org_id")?.Value;
            return Guid.TryParse(v, out var id) ? id : null;
        }
    }

    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => User?.Identity?.Name;
}
