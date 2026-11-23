using System.ComponentModel.DataAnnotations;
using Crm.Data.Entities;

namespace Crm.Web.ViewModels;

public class ContactFormVm
{
    public Guid? Id { get; set; }

    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [Phone, StringLength(50)] public string? Phone { get; set; }
    [Phone, StringLength(50)] public string? Mobile { get; set; }
    [StringLength(150)] public string? JobTitle { get; set; }
    public Guid? AccountId { get; set; }
    [StringLength(300)] public string? AddressLine1 { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? State { get; set; }
    [StringLength(20)] public string? PostalCode { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [Url, StringLength(300)] public string? Website { get; set; }
    [Url, StringLength(300)] public string? LinkedInUrl { get; set; }
    [Url, StringLength(300)] public string? FacebookUrl { get; set; }
    [StringLength(50)] public string? WhatsAppNumber { get; set; }
    [StringLength(100)] public string? Source { get; set; }
    public CustomerType CustomerType { get; set; } = CustomerType.Lead;
    public LeadStatus LeadStatus { get; set; } = LeadStatus.New;
    public int LeadScore { get; set; }
    public string? AssignedToId { get; set; }
    [StringLength(500)] public string? Tags { get; set; }
    public string? Notes { get; set; }
    public bool EmailOptIn { get; set; } = true;
    public bool SmsOptIn { get; set; }
    public string? ExternalRefId { get; set; }
}

public class AccountFormVm
{
    public Guid? Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(100)] public string? Industry { get; set; }
    [Url, StringLength(300)] public string? Website { get; set; }
    [StringLength(50)] public string? CompanySize { get; set; }
    public decimal? AnnualRevenue { get; set; }
    [StringLength(300)] public string? AddressLine1 { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? State { get; set; }
    [StringLength(20)] public string? PostalCode { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [Phone, StringLength(50)] public string? Phone { get; set; }
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    public string? AssignedToId { get; set; }
    [StringLength(500)] public string? Tags { get; set; }
    public string? Notes { get; set; }
}

public class LeadFormVm
{
    public Guid? Id { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [Phone, StringLength(50)] public string? Phone { get; set; }
    [StringLength(200)] public string? CompanyName { get; set; }
    [StringLength(150)] public string? JobTitle { get; set; }
    [StringLength(100)] public string? Industry { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [Url, StringLength(300)] public string? Website { get; set; }
    [StringLength(100)] public string? Source { get; set; }
    [StringLength(100)] public string? Campaign { get; set; }
    public string? AssignedToId { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.New;
    public string? Notes { get; set; }
}

public class ActivityFormVm
{
    public Guid? Id { get; set; }
    public ActivityType Type { get; set; } = ActivityType.Task;
    [Required, StringLength(300)] public string Subject { get; set; } = "";
    public string? Body { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? DealId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public int? DurationMinutes { get; set; }
    public string? OwnerId { get; set; }
}

public class DealFormVm
{
    public Guid? Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid PipelineId { get; set; }
    public Guid StageId { get; set; }
    [Range(0, double.MaxValue)] public decimal Amount { get; set; }
    [StringLength(3)] public string Currency { get; set; } = "USD";
    [Range(0, 100)] public int? ProbabilityOverride { get; set; }
    public DateTimeOffset? ExpectedCloseDate { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? ContactId { get; set; }
    public string? OwnerId { get; set; }
    [StringLength(200)] public string? Competitor { get; set; }
    public string? LostReason { get; set; }
    [StringLength(500)] public string? Tags { get; set; }
    public string? Notes { get; set; }
}

public class SetupVm
{
    [Required, StringLength(150)] public string OrganizationName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string AdminEmail { get; set; } = "";
    [Required, StringLength(100), DataType(DataType.Password)] public string AdminPassword { get; set; } = "";
    [Required, StringLength(100)] public string AdminFirstName { get; set; } = "";
    [Required, StringLength(100)] public string AdminLastName { get; set; } = "";
}

public class LoginVm
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class IntegrationFormVm
{
    public Guid? Id { get; set; }
    [Required, StringLength(150)] public string Name { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
    [StringLength(50)] public string? TemplateKey { get; set; }
    [Required, StringLength(100)] public string ExternalSystem { get; set; } = "";
    [Required, StringLength(2000)] public string BaseUrl { get; set; } = "";
    [StringLength(2000)] public string? Endpoint { get; set; }
    public string HttpMethod { get; set; } = "GET";
    public IntegrationAuthType AuthType { get; set; } = IntegrationAuthType.None;
    [StringLength(500)] public string? ApiKey { get; set; }
    [StringLength(1000)] public string? BearerToken { get; set; }
    [StringLength(200)] public string? Username { get; set; }
    [StringLength(500)] public string? Password { get; set; }
    [StringLength(100)] public string? CustomHeaderName { get; set; }
    [StringLength(1000)] public string? CustomHeaderValue { get; set; }
    public string? CustomHeadersJson { get; set; }
    public string? QueryParamsJson { get; set; }
    public string? RequestBody { get; set; }
    [Range(5, 120)] public int TimeoutSeconds { get; set; } = 30;
    [Range(1, 1000)] public int PageSize { get; set; } = 100;
    [StringLength(50)] public string? PaginationMode { get; set; }
    [Range(1, 100000)] public int MaxRecordsPerSync { get; set; } = 5000;
    public SyncDirection Direction { get; set; } = SyncDirection.Inbound;
    public bool IsEnabled { get; set; } = true;
    [StringLength(100)] public string? SyncSchedule { get; set; }
    [StringLength(200)] public string? RecordsPath { get; set; }
    [StringLength(50)] public string? TargetEntity { get; set; }
    [StringLength(200)] public string? ExternalKeyField { get; set; }
    public string? MappingRulesJson { get; set; }
    public string? SyncOptionsJson { get; set; }
    public string? OAuthProvider { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthClientSecret { get; set; }
    public string? OAuthScope { get; set; }
    public string? OAuthAuthorizeUrl { get; set; }
    public string? OAuthTokenUrl { get; set; }
}

public class TicketFormVm
{
    [Required, StringLength(300)] public string Subject { get; set; } = "";
    [Required] public string Description { get; set; } = "";
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public string? Category { get; set; }
    public string? AssigneeId { get; set; }
}

public class TicketReplyVm
{
    public Guid TicketId { get; set; }
    [Required] public string Body { get; set; } = "";
    public bool IsInternalNote { get; set; }
}
public class InvoiceFormVm
{
    public Guid? OrderId { get; set; }
    public Guid? ContactId { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string? Notes { get; set; }
}
