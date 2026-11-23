using System.Text.Json;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Crm.Web.Services.Integrations;
using Crm.Web.Services.OAuth;
using Crm.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

[Authorize]
public class IntegrationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly IntegrationHttpService _http;
    private readonly IntegrationSyncService _sync;
    private readonly ISecretProtector _secrets;
    private readonly OAuthFlowService _oauth;

    public IntegrationsController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit,
        IntegrationHttpService http, IntegrationSyncService sync, ISecretProtector secrets,
        OAuthFlowService oauth)
    {
        _db = db; _tenant = tenant; _audit = audit;
        _http = http; _sync = sync; _secrets = secrets; _oauth = oauth;
    }

    public static readonly (string Key, string Name, string Description)[] Templates =
    {
        ("generic-rest", "Generic REST API", "Any JSON REST endpoint"),
        ("erp-customers", "ERP customer sync", "Pull customers/contacts from an ERP API"),
        ("erp-products", "ERP product sync", "Pull products & prices"),
        ("erp-orders", "ERP order sync", "Pull sales orders"),
        ("erp-invoices", "ERP invoice sync", "Pull invoices"),
        ("website-contacts", "Website contact sync", "Sync leads submitted on your website"),
        ("ecommerce-orders", "E-commerce orders", "Shopify/WooCommerce-style order feed"),
        ("accounting", "Accounting software", "Invoices/customers from accounting API"),
        ("custom", "Custom CRM", "Connect another CRM")
    };

    [HttpGet]
    [Authorize(Policy = Permissions.IntegrationsView)]
    public async Task<IActionResult> Index()
    {
        var items = await _db.IntegrationConfigs.OrderByDescending(i => i.UpdatedAt).ToListAsync();
        var vm = items.Select(i => new IntegrationListItem
        {
            Id = i.Id, Name = i.Name, ExternalSystem = i.ExternalSystem, BaseUrl = i.BaseUrl,
            IsEnabled = i.IsEnabled, SyncStatus = i.SyncStatus, LastSyncAt = i.LastSyncAt,
            LastError = i.LastError, TargetEntity = i.TargetEntity, Direction = i.Direction
        }).ToList();
        ViewBag.Templates = Templates;
        return View(vm);
    }

    [HttpGet]
    [Authorize(Policy = Permissions.IntegrationsCreate)]
    public IActionResult Create(string? template = null)
    {
        var vm = new IntegrationFormVm();
        if (!string.IsNullOrEmpty(template))
        {
            vm.TemplateKey = template;
            var t = Templates.FirstOrDefault(x => x.Key == template);
            if (t.Key != null) vm.ExternalSystem = t.Name;
            if (template == "erp-customers") { vm.TargetEntity = "Contact"; vm.RecordsPath = "customers"; }
            if (template == "erp-products") { vm.TargetEntity = "Product"; }
            if (template == "website-contacts") { vm.TargetEntity = "Lead"; }
        }
        PopulateLookups(vm);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.IntegrationsCreate)]
    public async Task<IActionResult> Create(IntegrationFormVm vm, bool saveDisabled = false)
    {
        if (!ModelState.IsValid) { PopulateLookups(vm); return View(vm); }

        var cfg = new IntegrationConfig
        {
            OrganizationId = _tenant.OrganizationId!.Value,
            Name = vm.Name.Trim(), Description = vm.Description, TemplateKey = vm.TemplateKey,
            ExternalSystem = vm.ExternalSystem.Trim(), BaseUrl = vm.BaseUrl.Trim(),
            Endpoint = vm.Endpoint?.Trim() ?? "", HttpMethod = vm.HttpMethod,
            AuthType = vm.AuthType, TimeoutSeconds = vm.TimeoutSeconds,
            PageSize = vm.PageSize, PaginationMode = vm.PaginationMode,
            MaxRecordsPerSync = vm.MaxRecordsPerSync,
            Direction = vm.Direction, IsEnabled = !saveDisabled,
            SyncSchedule = vm.SyncSchedule, RecordsPath = vm.RecordsPath,
            TargetEntity = vm.TargetEntity, ExternalKeyField = vm.ExternalKeyField,
            MappingRulesJson = vm.MappingRulesJson, CustomHeadersJson = vm.CustomHeadersJson,
            QueryParamsJson = vm.QueryParamsJson, RequestBody = vm.RequestBody,
            SyncOptionsJson = vm.SyncOptionsJson,
            OAuthProvider = vm.OAuthProvider, OAuthScope = vm.OAuthScope,
            OAuthAuthorizeUrl = vm.OAuthAuthorizeUrl, OAuthTokenUrl = vm.OAuthTokenUrl
        };
        ApplySecrets(cfg, vm);
        _db.IntegrationConfigs.Add(cfg);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Integration.Created", "Integration", cfg.Id.ToString(), new { cfg.Name });
        TempData["Success"] = $"Integration '{cfg.Name}' saved{(saveDisabled ? " (disabled)" : "")}.";
        return RedirectToAction(nameof(Edit), new { id = cfg.Id });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.IntegrationsEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var cfg = await _db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (cfg == null) return NotFound();

        var vm = new IntegrationFormVm
        {
            Id = cfg.Id, Name = cfg.Name, Description = cfg.Description, TemplateKey = cfg.TemplateKey,
            ExternalSystem = cfg.ExternalSystem, BaseUrl = cfg.BaseUrl, Endpoint = cfg.Endpoint,
            HttpMethod = cfg.HttpMethod, AuthType = cfg.AuthType, TimeoutSeconds = cfg.TimeoutSeconds,
            PageSize = cfg.PageSize, PaginationMode = cfg.PaginationMode, MaxRecordsPerSync = cfg.MaxRecordsPerSync,
            Direction = cfg.Direction, IsEnabled = cfg.IsEnabled, SyncSchedule = cfg.SyncSchedule,
            RecordsPath = cfg.RecordsPath, TargetEntity = cfg.TargetEntity,
            ExternalKeyField = cfg.ExternalKeyField, MappingRulesJson = cfg.MappingRulesJson,
            CustomHeadersJson = cfg.CustomHeadersJson, QueryParamsJson = cfg.QueryParamsJson,
            RequestBody = cfg.RequestBody, SyncOptionsJson = cfg.SyncOptionsJson,
            CustomHeaderName = cfg.CustomHeaderName,
            // Secrets are never echoed back — show masked placeholders
            ApiKey = cfg.ApiKeyEncrypted != null ? "••••••••" : null,
            BearerToken = cfg.BearerTokenEncrypted != null || cfg.OAuthTokenEncrypted != null ? "••••••••" : null,
            Username = cfg.UsernameEncrypted != null ? "••••••••" : null,
            CustomHeaderValue = cfg.CustomHeaderValueEncrypted != null ? "••••••••" : null
        };
        ViewBag.SyncLogs = await _db.SyncLogs.Where(s => s.IntegrationId == id)
            .OrderByDescending(s => s.StartedAt).Take(20).ToListAsync();
        ViewBag.RecentErrors = await _db.SyncRecordErrors.Where(e => e.SyncLog!.IntegrationId == id)
            .OrderByDescending(e => e.Id).Take(20).ToListAsync();
        PopulateLookups(vm);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.IntegrationsEdit)]
    public async Task<IActionResult> Edit(Guid id, IntegrationFormVm vm)
    {
        var cfg = await _db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (cfg == null) return NotFound();
        if (!ModelState.IsValid) { PopulateLookups(vm); return View(vm); }

        cfg.Name = vm.Name.Trim(); cfg.Description = vm.Description; cfg.TemplateKey = vm.TemplateKey;
        cfg.ExternalSystem = vm.ExternalSystem.Trim(); cfg.BaseUrl = vm.BaseUrl.Trim();
        cfg.Endpoint = vm.Endpoint?.Trim() ?? ""; cfg.HttpMethod = vm.HttpMethod;
        cfg.AuthType = vm.AuthType; cfg.TimeoutSeconds = vm.TimeoutSeconds;
        cfg.PageSize = vm.PageSize; cfg.PaginationMode = vm.PaginationMode;
        cfg.MaxRecordsPerSync = vm.MaxRecordsPerSync; cfg.Direction = vm.Direction;
        cfg.IsEnabled = vm.IsEnabled; cfg.SyncSchedule = vm.SyncSchedule;
        cfg.RecordsPath = vm.RecordsPath; cfg.TargetEntity = vm.TargetEntity;
        cfg.ExternalKeyField = vm.ExternalKeyField; cfg.MappingRulesJson = vm.MappingRulesJson;
        cfg.CustomHeadersJson = vm.CustomHeadersJson; cfg.QueryParamsJson = vm.QueryParamsJson;
        cfg.RequestBody = vm.RequestBody; cfg.SyncOptionsJson = vm.SyncOptionsJson;
        cfg.CustomHeaderName = vm.CustomHeaderName;
        cfg.OAuthProvider = vm.OAuthProvider; cfg.OAuthScope = vm.OAuthScope;
        cfg.OAuthAuthorizeUrl = vm.OAuthAuthorizeUrl; cfg.OAuthTokenUrl = vm.OAuthTokenUrl;
        cfg.UpdatedAt = DateTimeOffset.UtcNow;
        ApplySecrets(cfg, vm);

        await _db.SaveChangesAsync();
        await _audit.LogAsync("Integration.Updated", "Integration", id.ToString());
        TempData["Success"] = "Integration saved.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>AJAX: send the configured request and return status/preview/structure.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.IntegrationsEdit)]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        var cfg = await _db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (cfg == null) return NotFound();

        var result = await _sync.TestConnectionAsync(cfg, ct);
        object payload = result.Json.HasValue
            ? new { result.StatusCode, result.DurationMs, result.Success, result.Error,
                    Preview = result.BodyPreview, Structure = JsonExplorer.Describe(result.Json.Value) }
            : new { result.StatusCode, result.DurationMs, result.Success, result.Error, Preview = result.BodyPreview };
        return Json(payload);
    }

    /// <summary>AJAX: test an unsaved configuration from the create form.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.IntegrationsCreate)]
    public async Task<IActionResult> TestDraft([FromBody] IntegrationFormVm vm, CancellationToken ct)
    {
        var cfg = new IntegrationConfig
        {
            OrganizationId = _tenant.OrganizationId ?? Guid.Empty,
            Name = vm.Name ?? "", ExternalSystem = vm.ExternalSystem ?? "",
            BaseUrl = vm.BaseUrl ?? "", Endpoint = vm.Endpoint ?? "",
            HttpMethod = vm.HttpMethod, AuthType = vm.AuthType,
            TimeoutSeconds = vm.TimeoutSeconds, CustomHeaderName = vm.CustomHeaderName,
            CustomHeadersJson = vm.CustomHeadersJson, QueryParamsJson = vm.QueryParamsJson,
            RequestBody = vm.RequestBody
        };
        // For draft test, temporarily protect the plaintext secrets just for this call
        var result = await _http.SendAsync(cfg,
            decryptedApiKey: null,
            decryptedBearer: vm.BearerToken == "••••••••" ? null : vm.BearerToken,
            decryptedUsername: vm.Username == "••••••••" ? null : vm.Username,
            decryptedPassword: vm.Password,
            decryptedCustomHeaderValue: vm.CustomHeaderValue,
            ct: ct);
        // apiKey/customHeader auth via headers
        if (vm.AuthType == IntegrationAuthType.ApiKey && !string.IsNullOrEmpty(vm.ApiKey))
        {
            // re-send with key header: emulate by adding to custom headers
            cfg.CustomHeadersJson = MergeHeader(cfg.CustomHeadersJson, "X-API-Key", vm.ApiKey);
            result = await _http.SendAsync(cfg, ct: ct);
        }
        object payload = result.Json.HasValue
            ? new { result.StatusCode, result.DurationMs, result.Success, result.Error,
                    Preview = result.BodyPreview, Structure = JsonExplorer.Describe(result.Json.Value) }
            : new { result.StatusCode, result.DurationMs, result.Success, result.Error, Preview = result.BodyPreview };
        return Json(payload);
    }

    private static string? MergeHeader(string? json, string name, string value)
    {
        var list = IntegrationHttpService.ParseJsonArray(json)
            .Select(kv => new { name = kv.Key, value = kv.Value }).ToList();
        list.Add(new { name, value });
        return JsonSerializer.Serialize(list);
    }

    /// <summary>AJAX: run sync now.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.IntegrationsSync)]
    public async Task<IActionResult> SyncNow(Guid id, CancellationToken ct)
    {
        var cfg = await _db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (cfg == null) return NotFound();
        if (!cfg.IsEnabled) return BadRequest("Integration is disabled.");

        var result = await _sync.RunSyncAsync(cfg, SyncTrigger.Manual, ct);
        return Json(new
        {
            ok = result.Errors.Count == 0 || result.Created + result.Updated > 0,
            result.Fetched, result.Created, result.Updated, result.Skipped, result.Failed,
            Errors = result.Errors.Take(10)
        });
    }

    /// <summary>AJAX: preview records that would be imported (first 20) without saving.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.IntegrationsEdit)]
    public async Task<IActionResult> PreviewRecords(Guid id, CancellationToken ct)
    {
        var cfg = await _db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (cfg == null) return NotFound();

        var response = await _http.SendAsync(cfg,
            _secrets.UnprotectOrNull(cfg.ApiKeyEncrypted),
            _secrets.UnprotectOrNull(cfg.BearerTokenEncrypted) ?? _secrets.UnprotectOrNull(cfg.OAuthTokenEncrypted),
            _secrets.UnprotectOrNull(cfg.UsernameEncrypted),
            _secrets.UnprotectOrNull(cfg.PasswordEncrypted),
            _secrets.UnprotectOrNull(cfg.CustomHeaderValueEncrypted), ct: ct);
        if (response.Json == null)
            return Json(new { error = response.Error ?? "No JSON response" });

        var records = JsonExplorer.GetRecords(response.Json.Value, cfg.RecordsPath).Take(20).ToList();
        var fields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
            CollectFields(r, "", fields, 2);

        return Json(new { count = records.Count, fields = fields.ToList(), records });
    }

    private static void CollectFields(JsonElement el, string prefix, SortedSet<string> fields, int depth)
    {
        if (depth < 0) return;
        if (el.ValueKind == JsonValueKind.Object)
            foreach (var p in el.EnumerateObject())
                CollectFields(p.Value, string.IsNullOrEmpty(prefix) ? p.Name : $"{prefix}.{p.Name}", fields, depth - 1);
        else fields.Add(prefix);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.IntegrationsEdit)]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var cfg = await _db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (cfg == null) return NotFound();
        cfg.IsEnabled = !cfg.IsEnabled;
        cfg.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.IntegrationsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var cfg = await _db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (cfg == null) return NotFound();
        _db.IntegrationConfigs.Remove(cfg);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Integration.Deleted", "Integration", id.ToString(), new { cfg.Name });
        TempData["Success"] = "Integration deleted.";
        return RedirectToAction(nameof(Index));
    }

    private void ApplySecrets(IntegrationConfig cfg, IntegrationFormVm vm)
    {
        string? NewSecret(string? incoming) =>
            string.IsNullOrEmpty(incoming) || incoming == "••••••••" ? null : incoming;

        var apiKey = NewSecret(vm.ApiKey);
        if (apiKey != null) cfg.ApiKeyEncrypted = _secrets.Protect(apiKey);
        var bearer = NewSecret(vm.BearerToken);
        if (bearer != null && (cfg.AuthType == IntegrationAuthType.BearerToken || cfg.AuthType == IntegrationAuthType.OAuth2))
            cfg.BearerTokenEncrypted = _secrets.Protect(bearer);
        var user = NewSecret(vm.Username);
        if (user != null) cfg.UsernameEncrypted = _secrets.Protect(user);
        var pass = NewSecret(vm.Password);
        if (pass != null) cfg.PasswordEncrypted = _secrets.Protect(pass);
        var chv = NewSecret(vm.CustomHeaderValue);
        if (chv != null) cfg.CustomHeaderValueEncrypted = _secrets.Protect(chv);
        var cid = NewSecret(vm.OAuthClientId);
        if (cid != null) cfg.OAuthClientIdEncrypted = _secrets.Protect(cid);
        var cs = NewSecret(vm.OAuthClientSecret);
        if (cs != null) cfg.OAuthClientSecretEncrypted = _secrets.Protect(cs);
        cfg.CustomHeaderName = vm.CustomHeaderName;
    }

    // ---------- OAuth2 consent flow ----------
    [HttpGet]
    [Authorize(Policy = Permissions.IntegrationsEdit)]
    public async Task<IActionResult> OAuthStart(Guid id)
    {
        var cfg = await _db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (cfg == null) return NotFound();
        var provider = OAuthProviders.Find(cfg.OAuthProvider ?? "custom");
        var clientId = _secrets.UnprotectOrNull(cfg.OAuthClientIdEncrypted);
        if (provider == null || string.IsNullOrEmpty(clientId))
        { TempData["Error"] = "Set OAuth provider, client ID and secret first."; return RedirectToAction(nameof(Edit), new { id }); }

        var redirectUri = Url.Action(nameof(OAuthCallback), "Integrations",
            new { id }, Request.Scheme, Request.Host.ToString());
        var state = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        HttpContext.Session.SetString($"oauth_state_{id}", state);
        var url = _oauth.BuildAuthorizeUrl(provider, clientId, redirectUri!, cfg.OAuthScope, state);
        return Redirect(url);
    }

    [HttpGet]
    public async Task<IActionResult> OAuthCallback(Guid id, string? code, string? state, string? error)
    {
        if (!User.Identity?.IsAuthenticated ?? true) return Challenge();
        var cfg = await _db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (cfg == null) return NotFound();
        if (!string.IsNullOrEmpty(error)) { TempData["Error"] = $"OAuth error: {error}"; return RedirectToAction(nameof(Edit), new { id }); }
        var savedState = HttpContext.Session.GetString($"oauth_state_{id}");
        if (string.IsNullOrEmpty(code) || state != savedState)
        { TempData["Error"] = "Invalid OAuth callback state."; return RedirectToAction(nameof(Edit), new { id }); }

        var provider = OAuthProviders.Find(cfg.OAuthProvider ?? "custom")!;
        var clientId = _secrets.UnprotectOrNull(cfg.OAuthClientIdEncrypted)!;
        var clientSecret = _secrets.UnprotectOrNull(cfg.OAuthClientSecretEncrypted) ?? "";
        var redirectUri = Url.Action(nameof(OAuthCallback), "Integrations",
            new { id }, Request.Scheme, Request.Host.ToString());

        var (access, refresh, err) = await _oauth.ExchangeCodeAsync(provider, clientId, clientSecret, code, redirectUri!);
        if (err != null) { TempData["Error"] = $"Token exchange failed: {err}"; return RedirectToAction(nameof(Edit), new { id }); }

        cfg.OAuthTokenEncrypted = _secrets.Protect(access!);
        if (refresh != null) cfg.OAuthRefreshTokenEncrypted = _secrets.Protect(refresh);
        cfg.OAuthExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        cfg.LastError = null; cfg.SyncStatus = SyncStatus.Success;
        cfg.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Integration.OAuthConnected", "Integration", id.ToString(), new { cfg.Name, provider.Key });
        TempData["Success"] = $"Connected to {provider.Name} successfully.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    private void PopulateLookups(IntegrationFormVm vm)
    {
        ViewBag.AuthTypes = Enum.GetValues<IntegrationAuthType>().Select(e => new SelectListItem(e.ToString(), e.ToString(), e == vm.AuthType));
        ViewBag.HttpMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }
            .Select(m => new SelectListItem(m, m, m == vm.HttpMethod));
        ViewBag.Directions = Enum.GetValues<SyncDirection>().Select(e => new SelectListItem(e.ToString(), e.ToString(), e == vm.Direction));
        ViewBag.Entities = new[] { "", "Contact", "Account", "Lead", "Product" }
            .Select(e => new SelectListItem(e == "" ? "—" : e, e, e == vm.TargetEntity));
        ViewBag.Templates = Templates;
    }

    public class IntegrationListItem
    {
        public Guid Id { get; set; } public string Name { get; set; } = "";
        public string ExternalSystem { get; set; } = ""; public string BaseUrl { get; set; } = "";
        public bool IsEnabled { get; set; } public SyncStatus SyncStatus { get; set; }
        public DateTimeOffset? LastSyncAt { get; set; } public string? LastError { get; set; }
        public string? TargetEntity { get; set; } public SyncDirection Direction { get; set; }
    }
}
