using System.Text.Json;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Services.Integrations;

public class FieldMapping
{
    public string SourcePath { get; set; } = "";   // e.g. "name" or "customer.email"
    public string Target { get; set; } = "";       // e.g. "FirstName" or "Custom:field"
    public string? Transform { get; set; }         // trim, lowercase, uppercase, date:yyyy-MM-dd, number, default:x
}

public class SyncResult
{
    public int Fetched, Created, Updated, Skipped, Failed;
    public List<string> Errors = new();
}

/// <summary>Applies mapping rules from external JSON records into CRM entities.</summary>
public class IntegrationSyncService
{
    private readonly ApplicationDbContext _db;
    private readonly IntegrationHttpService _http;
    private readonly ISecretProtector _secrets;
    private readonly ITenantContext _tenant;

    public IntegrationSyncService(ApplicationDbContext db, IntegrationHttpService http,
        ISecretProtector secrets, ITenantContext tenant)
    {
        _db = db; _http = http; _secrets = secrets; _tenant = tenant;
    }

    public static List<FieldMapping> ParseMappings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<FieldMapping>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>Test connection: sends request, returns result (does not persist).</summary>
    public async Task<IntegrationRequestResult> TestConnectionAsync(IntegrationConfig cfg, CancellationToken ct = default)
        => await _http.SendAsync(cfg,
            _secrets.UnprotectOrNull(cfg.ApiKeyEncrypted),
            _secrets.UnprotectOrNull(cfg.BearerTokenEncrypted) ?? _secrets.UnprotectOrNull(cfg.OAuthTokenEncrypted),
            _secrets.UnprotectOrNull(cfg.UsernameEncrypted),
            _secrets.UnprotectOrNull(cfg.PasswordEncrypted),
            _secrets.UnprotectOrNull(cfg.CustomHeaderValueEncrypted),
            ct: ct);

    /// <summary>Run a full inbound sync for an integration.</summary>
    public async Task<SyncResult> RunSyncAsync(IntegrationConfig cfg, SyncTrigger trigger, CancellationToken ct = default)
    {
        var result = new SyncResult();
        var orgId = cfg.OrganizationId;

        var log = new SyncLog
        {
            OrganizationId = orgId, IntegrationId = cfg.Id, Trigger = trigger,
            Status = SyncStatus.Running, StartedAt = DateTimeOffset.UtcNow
        };
        _db.SyncLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var response = await _http.SendAsync(cfg,
                _secrets.UnprotectOrNull(cfg.ApiKeyEncrypted),
                _secrets.UnprotectOrNull(cfg.BearerTokenEncrypted) ?? _secrets.UnprotectOrNull(cfg.OAuthTokenEncrypted),
                _secrets.UnprotectOrNull(cfg.UsernameEncrypted),
                _secrets.UnprotectOrNull(cfg.PasswordEncrypted),
                _secrets.UnprotectOrNull(cfg.CustomHeaderValueEncrypted),
                ct: ct);

            if (!response.Success || response.Json == null)
            {
                result.Failed = 1;
                result.Errors.Add(response.Error ?? "No JSON response");
                log.Status = SyncStatus.Failed;
                log.ErrorSummary = response.Error;
                log.FinishedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                return result;
            }

            var records = JsonExplorer.GetRecords(response.Json.Value, cfg.RecordsPath)
                .Take(cfg.MaxRecordsPerSync).ToList();
            result.Fetched = records.Count;

            var mappings = ParseMappings(cfg.MappingRulesJson);
            if (mappings.Count == 0)
            {
                result.Errors.Add("No field mappings configured.");
            }

            foreach (var record in records)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var outcome = await ApplyRecordAsync(orgId, cfg, mappings, record, ct);
                    switch (outcome)
                    {
                        case "created": result.Created++; break;
                        case "updated": result.Updated++; break;
                        default: result.Skipped++; break;
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add(ex.Message);
                    _db.SyncRecordErrors.Add(new SyncRecordError
                    {
                        OrganizationId = orgId, SyncLogId = log.Id,
                        ExternalKey = cfg.ExternalKeyField != null ? JsonExplorer.GetFieldValue(record, cfg.ExternalKeyField) : null,
                        PayloadJson = record.GetRawText(), Error = ex.Message
                    });
                }
            }

            log.Status = result.Failed > 0 ? (result.Created + result.Updated > 0 ? SyncStatus.Partial : SyncStatus.Failed) : SyncStatus.Success;
            log.RecordsFetched = result.Fetched; log.RecordsCreated = result.Created;
            log.RecordsUpdated = result.Updated; log.RecordsSkipped = result.Skipped; log.RecordsFailed = result.Failed;
            log.ErrorSummary = result.Errors.Count > 0 ? string.Join("; ", result.Errors.Take(5)) : null;
        }
        catch (Exception ex)
        {
            log.Status = SyncStatus.Failed;
            log.ErrorSummary = ex.Message;
            result.Errors.Add(ex.Message);
        }
        finally
        {
            log.FinishedAt = DateTimeOffset.UtcNow;
            cfg.LastSyncAt = DateTimeOffset.UtcNow;
            cfg.SyncStatus = log.Status;
            cfg.LastError = log.ErrorSummary;
            await _db.SaveChangesAsync(ct);
        }

        return result;
    }

    private async Task<string> ApplyRecordAsync(Guid orgId, IntegrationConfig cfg,
        List<FieldMapping> mappings, JsonElement record, CancellationToken ct)
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
            fields[m.Target] = Transform(JsonExplorer.GetFieldValue(record, m.SourcePath), m.Transform);

        // duplicate detection via external key
        var extKey = cfg.ExternalKeyField != null ? JsonExplorer.GetFieldValue(record, cfg.ExternalKeyField) : null;

        switch (cfg.TargetEntity?.ToLowerInvariant())
        {
            case "contact":
                return await UpsertContactAsync(orgId, cfg, fields, extKey, ct);
            case "account":
                return await UpsertAccountAsync(orgId, cfg, fields, extKey, ct);
            case "lead":
                return await UpsertLeadAsync(orgId, cfg, fields, extKey, ct);
            default:
                return "skipped";
        }
    }

    private async Task<string> UpsertContactAsync(Guid orgId, IntegrationConfig cfg,
        Dictionary<string, string?> f, string? extKey, CancellationToken ct)
    {
        Contact? existing = null;
        if (!string.IsNullOrEmpty(extKey))
            existing = await _db.Contacts.FirstOrDefaultAsync(c => c.ExternalRefId == extKey && c.ExternalSystem == cfg.ExternalSystem, ct);
        else if (!string.IsNullOrEmpty(f.GetValueOrDefault("Email")))
            existing = await _db.Contacts.FirstOrDefaultAsync(c => c.Email == f["Email"] && !c.IsDeleted, ct);

        if (existing == null)
        {
            var c = new Contact
            {
                OrganizationId = orgId,
                FirstName = f.GetValueOrDefault("FirstName") ?? "Unknown",
                LastName = f.GetValueOrDefault("LastName") ?? "",
                Email = f.GetValueOrDefault("Email"), Phone = f.GetValueOrDefault("Phone"),
                Mobile = f.GetValueOrDefault("Mobile"), JobTitle = f.GetValueOrDefault("JobTitle"),
                Website = f.GetValueOrDefault("Website"), City = f.GetValueOrDefault("City"),
                Country = f.GetValueOrDefault("Country"), Source = cfg.ExternalSystem,
                ExternalRefId = extKey, ExternalSystem = cfg.ExternalSystem
            };
            ApplyCustomFields(c, f);
            _db.Contacts.Add(c);
            await _db.SaveChangesAsync(ct);
            return "created";
        }

        existing.FirstName = f.GetValueOrDefault("FirstName") ?? existing.FirstName;
        existing.LastName = f.GetValueOrDefault("LastName") ?? existing.LastName;
        existing.Email = f.GetValueOrDefault("Email") ?? existing.Email;
        existing.Phone = f.GetValueOrDefault("Phone") ?? existing.Phone;
        existing.JobTitle = f.GetValueOrDefault("JobTitle") ?? existing.JobTitle;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        ApplyCustomFields(existing, f);
        await _db.SaveChangesAsync(ct);
        return "updated";
    }

    private async Task<string> UpsertAccountAsync(Guid orgId, IntegrationConfig cfg,
        Dictionary<string, string?> f, string? extKey, CancellationToken ct)
    {
        Account? existing = null;
        if (!string.IsNullOrEmpty(extKey))
            existing = await _db.Accounts.FirstOrDefaultAsync(a => a.ExternalRefId == extKey && a.ExternalSystem == cfg.ExternalSystem, ct);
        else if (!string.IsNullOrEmpty(f.GetValueOrDefault("Name")))
            existing = await _db.Accounts.FirstOrDefaultAsync(a => a.Name == f["Name"] && !a.IsDeleted, ct);

        if (existing == null)
        {
            _db.Accounts.Add(new Account
            {
                OrganizationId = orgId, Name = f.GetValueOrDefault("Name") ?? "Unknown",
                Industry = f.GetValueOrDefault("Industry"), Website = f.GetValueOrDefault("Website"),
                Phone = f.GetValueOrDefault("Phone"), Email = f.GetValueOrDefault("Email"),
                City = f.GetValueOrDefault("City"), Country = f.GetValueOrDefault("Country"),
                ExternalRefId = extKey, ExternalSystem = cfg.ExternalSystem
            });
            await _db.SaveChangesAsync(ct);
            return "created";
        }
        existing.Industry = f.GetValueOrDefault("Industry") ?? existing.Industry;
        existing.Website = f.GetValueOrDefault("Website") ?? existing.Website;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return "updated";
    }

    private async Task<string> UpsertLeadAsync(Guid orgId, IntegrationConfig cfg,
        Dictionary<string, string?> f, string? extKey, CancellationToken ct)
    {
        Lead? existing = null;
        if (!string.IsNullOrEmpty(extKey))
            existing = await _db.Leads.FirstOrDefaultAsync(l => l.ExternalRefId == extKey && l.ExternalSystem == cfg.ExternalSystem, ct);
        else if (!string.IsNullOrEmpty(f.GetValueOrDefault("Email")))
            existing = await _db.Leads.FirstOrDefaultAsync(l => l.Email == f["Email"] && !l.IsDeleted, ct);

        if (existing == null)
        {
            _db.Leads.Add(new Lead
            {
                OrganizationId = orgId,
                FirstName = f.GetValueOrDefault("FirstName") ?? "Unknown",
                LastName = f.GetValueOrDefault("LastName") ?? "",
                Email = f.GetValueOrDefault("Email"), Phone = f.GetValueOrDefault("Phone"),
                CompanyName = f.GetValueOrDefault("CompanyName"), JobTitle = f.GetValueOrDefault("JobTitle"),
                Source = cfg.ExternalSystem, ExternalRefId = extKey, ExternalSystem = cfg.ExternalSystem
            });
            await _db.SaveChangesAsync(ct);
            return "created";
        }
        existing.Email = f.GetValueOrDefault("Email") ?? existing.Email;
        existing.Phone = f.GetValueOrDefault("Phone") ?? existing.Phone;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return "updated";
    }

    private static void ApplyCustomFields(Contact c, Dictionary<string, string?> fields)
    {
        var custom = fields.Where(kv => kv.Key.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key[7..], kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (custom.Count > 0)
            c.CustomFieldsJson = JsonSerializer.Serialize(custom);
    }

    private static string? Transform(string? value, string? transform)
    {
        if (value == null) return null;
        if (string.IsNullOrEmpty(transform)) return value.Trim();
        var t = transform.Trim();
        if (t.StartsWith("default:", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(value) ? t[8..] : value;
        return t.ToLowerInvariant() switch
        {
            "trim" => value.Trim(),
            "lowercase" => value.ToLowerInvariant(),
            "uppercase" => value.ToUpperInvariant(),
            "number" => decimal.TryParse(value, out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            var s when s.StartsWith("date:") => DateTimeOffset.TryParse(value, out var d)
                ? d.ToString(s[5..], System.Globalization.CultureInfo.InvariantCulture) : value,
            _ => value
        };
    }
}
