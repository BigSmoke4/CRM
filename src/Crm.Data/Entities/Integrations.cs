namespace Crm.Data.Entities;

public enum IntegrationAuthType
{
    None, ApiKey, BearerToken, BasicAuth, OAuth2, CustomHeader
}

public enum SyncDirection
{
    Inbound,      // external -> CRM
    Outbound,     // CRM -> external
    Bidirectional
}

public enum SyncStatus
{
    Never, Running, Success, Partial, Failed
}

/// <summary>A configured external data source / API connection.</summary>
public class IntegrationConfig : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? TemplateKey { get; set; }        // e.g. "generic-rest", "erp-customers"
    public string ExternalSystem { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public IntegrationAuthType AuthType { get; set; } = IntegrationAuthType.None;

    // Secrets are stored encrypted (see ISecretProtector). Never returned to the client.
    public string? ApiKeyEncrypted { get; set; }
    public string? BearerTokenEncrypted { get; set; }
    public string? UsernameEncrypted { get; set; }
    public string? PasswordEncrypted { get; set; }
    public string? CustomHeaderName { get; set; }
    public string? CustomHeaderValueEncrypted { get; set; }
    public string? OAuthProvider { get; set; }        // gmail, m365, slack, custom
    public string? OAuthClientIdEncrypted { get; set; }
    public string? OAuthClientSecretEncrypted { get; set; }
    public string? OAuthAuthorizeUrl { get; set; }
    public string? OAuthTokenUrl { get; set; }
    public string? OAuthScope { get; set; }
    public string? OAuthTokenEncrypted { get; set; }
    public string? OAuthRefreshTokenEncrypted { get; set; }
    public DateTimeOffset? OAuthExpiresAt { get; set; }

    /// <summary>JSON array of {name,value} custom headers.</summary>
    public string? CustomHeadersJson { get; set; }
    /// <summary>JSON array of {name,value} query parameters.</summary>
    public string? QueryParamsJson { get; set; }
    /// <summary>Raw request body template (JSON/text).</summary>
    public string? RequestBody { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
    public int PageSize { get; set; } = 100;
    public string? PaginationMode { get; set; }     // none, page, offset, cursor
    public int MaxRecordsPerSync { get; set; } = 5000;

    public SyncDirection Direction { get; set; } = SyncDirection.Inbound;
    public bool IsEnabled { get; set; } = true;
    /// <summary>Cron-like or interval schedule: e.g. "0 */30 * * *" or simple "every:30m".</summary>
    public string? SyncSchedule { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset? NextSyncAt { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Never;
    public string? LastError { get; set; }

    /// <summary>JSON: field mappings [{"sourcePath":"customers[].name","target":"Contact.FirstName","transform":"trim"}].</summary>
    public string? MappingRulesJson { get; set; }
    /// <summary>JSON path to the record array, e.g. "customers" or "data.items".</summary>
    public string? RecordsPath { get; set; }
    /// <summary>Target CRM entity: Contact, Account, Lead, Product.</summary>
    public string? TargetEntity { get; set; }
    /// <summary>External field used as unique key for duplicate detection.</summary>
    public string? ExternalKeyField { get; set; }
    /// <summary>JSON: sync options {incrementalField, deleteHandling, conflictResolution}.</summary>
    public string? SyncOptionsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SyncLog : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IntegrationId { get; set; }
    public IntegrationConfig? Integration { get; set; }

    public SyncStatus Status { get; set; }
    public SyncTrigger Trigger { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int RecordsFetched { get; set; }
    public int RecordsCreated { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsSkipped { get; set; }
    public int RecordsFailed { get; set; }
    public string? ErrorSummary { get; set; }
    public string? DetailJson { get; set; }
}

public enum SyncTrigger { Manual, Scheduled, Test, Webhook }

public class SyncRecordError : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SyncLogId { get; set; }
    public SyncLog? SyncLog { get; set; }
    public string? ExternalKey { get; set; }
    public string? PayloadJson { get; set; }
    public string Error { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
}
