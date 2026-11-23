namespace Crm.Data.Entities;

public enum ActivityType
{
    Note, Call, Email, Meeting, Task, Sms, WhatsApp, Deal, Ticket,
    Order, Invoice, Campaign, Sync, AiSummary, System
}

public enum LeadStatus
{
    New, Contacted, Qualified, Unqualified, Converted, Lost
}

public enum CustomerType
{
    Lead, Prospect, Customer, Partner, Churned
}

public enum DealStageType
{
    Open, Won, Lost
}

public class Contact : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? JobTitle { get; set; }

    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Website { get; set; }

    public string? LinkedInUrl { get; set; }
    public string? FacebookUrl { get; set; }
    public string? WhatsAppNumber { get; set; }

    public string? Source { get; set; }          // manual, csv, api, webform, ads...
    public CustomerType CustomerType { get; set; } = CustomerType.Lead;
    public LeadStatus LeadStatus { get; set; } = LeadStatus.New;
    public int LeadScore { get; set; }

    public string? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    /// <summary>Comma-separated tag names.</summary>
    public string? Tags { get; set; }

    /// <summary>JSON object of custom field values: { "fieldName": value }.</summary>
    public string? CustomFieldsJson { get; set; }

    public string? Notes { get; set; }

    // Consent / communication preferences
    public bool EmailOptIn { get; set; } = true;
    public bool SmsOptIn { get; set; }
    public string? ConsentSource { get; set; }
    public DateTimeOffset? ConsentDate { get; set; }

    /// <summary>External system reference id from integrations (e.g. ERP id).</summary>
    public string? ExternalRefId { get; set; }
    public string? ExternalSystem { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedById { get; set; }

    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
    public ICollection<ContactNote> ContactNotes { get; set; } = new List<ContactNote>();
}

public class Account : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? Website { get; set; }
    public string? CompanySize { get; set; }
    public decimal? AnnualRevenue { get; set; }

    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }

    public string? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    public string? Tags { get; set; }
    public string? CustomFieldsJson { get; set; }
    public string? Notes { get; set; }

    public decimal LifetimeValue { get; set; }
    public string? ExternalRefId { get; set; }
    public string? ExternalSystem { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
}

public class Lead : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? CompanyName { get; set; }
    public string? JobTitle { get; set; }
    public string? Industry { get; set; }
    public string? Country { get; set; }
    public string? Website { get; set; }

    public string? Source { get; set; }
    public string? Campaign { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }

    public LeadStatus Status { get; set; } = LeadStatus.New;
    public int Score { get; set; }
    public string? ScoreBreakdownJson { get; set; }

    public string? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    /// <summary>Set when lead is converted into a Contact (+ optionally Account/Deal).</summary>
    public Guid? ConvertedContactId { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }

    public string? ExternalRefId { get; set; }
    public string? ExternalSystem { get; set; }

    public string? CustomFieldsJson { get; set; }
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ContactNote : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ContactId { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Unified timeline entry: calls, emails, meetings, tasks, notes, deals, tickets, sync events, AI summaries.</summary>
public class Activity : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public ActivityType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Body { get; set; }

    // Polymorphic links (at most one typically set)
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? TicketId { get; set; }

    public string? OwnerId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? DurationMinutes { get; set; }

    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Attachment : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string EntityType { get; set; } = string.Empty; // Contact, Account, Ticket...
    public Guid EntityId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? UploadedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
