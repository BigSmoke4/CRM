using System.Text.Json;
using Crm.Data;
using Crm.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entityType, string? entityId = null, object? details = null);
}

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IHttpContextAccessor _http;

    public AuditService(ApplicationDbContext db, ITenantContext tenant, IHttpContextAccessor http)
    {
        _db = db;
        _tenant = tenant;
        _http = http;
    }

    public async Task LogAsync(string action, string entityType, string? entityId = null, object? details = null)
    {
        if (!_tenant.IsAuthenticated) return;

        var orgId = _tenant.OrganizationId;
        if (!orgId.HasValue) return; // platform-level actions logged without org are skipped

        _db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = orgId.Value,
            UserId = _tenant.UserId,
            UserName = _tenant.UserName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = details == null ? null : JsonSerializer.Serialize(details),
            IpAddress = _http.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();
    }
}
