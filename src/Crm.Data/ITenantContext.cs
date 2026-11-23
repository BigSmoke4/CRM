namespace Crm.Data;

/// <summary>Abstraction over the current tenant; implemented in the web layer per request.</summary>
public interface ITenantContext
{
    /// <summary>Null for platform/super-admin contexts with no org scope.</summary>
    Guid? OrganizationId { get; }
    string? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    /// <summary>True when the current user is a platform super-admin (cross-tenant).</summary>
    bool IsSuperAdmin { get; }
}
