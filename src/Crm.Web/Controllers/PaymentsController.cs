using System.ComponentModel.DataAnnotations;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Crm.Web.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>Payment provider settings + invoice checkout + Stripe webhook.</summary>
[Authorize]
public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _secrets;
    private readonly StripePaymentService _stripe;
    private readonly IAuditService _audit;

    public PaymentsController(ApplicationDbContext db, ITenantContext tenant, ISecretProtector secrets,
        StripePaymentService stripe, IAuditService audit)
    { _db = db; _tenant = tenant; _secrets = secrets; _stripe = stripe; _audit = audit; }

    [HttpGet, Authorize(Policy = Permissions.SettingsView)]
    public async Task<IActionResult> Settings()
    {
        var cfg = await _db.PaymentProviderConfigs.FirstOrDefaultAsync(p => p.Provider == "Stripe");
        ViewBag.Config = cfg;
        return View(new PaymentSettingsVm());
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.SettingsManage)]
    public async Task<IActionResult> SaveSettings(PaymentSettingsVm vm)
    {
        var cfg = await _db.PaymentProviderConfigs.FirstOrDefaultAsync(p => p.Provider == "Stripe")
            ?? new PaymentProviderConfig { OrganizationId = _tenant.OrganizationId!.Value, Provider = "Stripe" };
        cfg.IsEnabled = vm.IsEnabled;
        cfg.PublishableKey = vm.PublishableKey;
        if (!string.IsNullOrEmpty(vm.SecretKey) && vm.SecretKey != "••••••••")
            cfg.SecretKeyEncrypted = _secrets.Protect(vm.SecretKey);
        if (!string.IsNullOrEmpty(vm.WebhookSecret) && vm.WebhookSecret != "••••••••")
            cfg.WebhookSecretEncrypted = _secrets.Protect(vm.WebhookSecret);
        if (cfg.OrganizationId == Guid.Empty) { cfg.Id = Guid.NewGuid(); _db.PaymentProviderConfigs.Add(cfg); }
        cfg.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Payment.SettingsSaved", "PaymentProvider", cfg.Id.ToString());
        TempData["Success"] = "Payment settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    /// <summary>Create Stripe Checkout for an invoice and redirect the user to Stripe.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.InvoicesEdit)]
    public async Task<IActionResult> PayInvoice(Guid id)
    {
        var inv = await _db.Invoices.Include(i => i.Contact).FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return NotFound();
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var (url, err) = await _stripe.CreateInvoiceCheckoutAsync(
            _tenant.OrganizationId!.Value, inv,
            $"{baseUrl}/Commerce/Invoices", $"{baseUrl}/Commerce/Invoices");
        if (err != null) { TempData["Error"] = err; return RedirectToAction("Invoices", "Commerce"); }
        return Redirect(url!);
    }

    /// <summary>Stripe webhook endpoint (no auth — verified by signature).</summary>
    [HttpPost, AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Webhook()
    {
        var payload = await new StreamReader(Request.Body).ReadToEndAsync();
        var sig = Request.Headers["Stripe-Signature"].ToString();
        // find webhook secret across orgs
        var configs = await _db.PaymentProviderConfigs.Where(p => p.IsEnabled).ToListAsync();
        foreach (var cfg in configs)
        {
            var secret = _secrets.UnprotectOrNull(cfg.WebhookSecretEncrypted);
            if (!string.IsNullOrEmpty(secret) && _stripe.VerifyWebhook(payload, sig, secret))
            {
                await _stripe.HandleWebhookAsync(payload);
                return Ok();
            }
        }
        return Unauthorized();
    }
}

public class PaymentSettingsVm
{
    public bool IsEnabled { get; set; }
    [StringLength(200)] public string? PublishableKey { get; set; }
    [StringLength(200)] public string? SecretKey { get; set; }
    [StringLength(200)] public string? WebhookSecret { get; set; }
}
