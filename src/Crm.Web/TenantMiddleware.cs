using System.Security.Claims;
using Crm.Data;
using Crm.Web.Services;

namespace Crm.Web;

/// <summary>Ensures TenantContext is current; also logs org_id into TraceIdentifier for diagnostics.</summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Touch the scoped tenant so DbContext picks up claims (DbContext is created lazily per scope).
        var tenant = context.RequestServices.GetRequiredService<ITenantContext>();
        _ = tenant.OrganizationId;
        await _next(context);
    }
}
