namespace Crm.Data.Entities;

/// <summary>Marks an entity as belonging to a single tenant (organization).</summary>
public interface ITenantEntity
{
    Guid OrganizationId { get; set; }
}

public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string TimeZone { get; set; } = "UTC";
    public string Currency { get; set; } = "USD";
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}

public class Team : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Definition of a user-defined custom field for a CRM entity type.</summary>
public class CustomFieldDefinition : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    /// <summary>Target entity: Contact, Account, Lead, Deal, Ticket, Product...</summary>
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>text, number, date, boolean, select, multiselect, url, email, phone</summary>
    public string FieldType { get; set; } = "text";
    /// <summary>JSON array of options for select/multiselect.</summary>
    public string? OptionsJson { get; set; }
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class AuditLog : ITenantEntity
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty;      // e.g. Contact.Created
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Notification : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string? UserId { get; set; }   // null = broadcast to org
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public string Type { get; set; } = "info"; // info, success, warning, error
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
