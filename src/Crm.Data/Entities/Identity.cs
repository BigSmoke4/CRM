using Microsoft.AspNetCore.Identity;

namespace Crm.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid? TeamId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}
