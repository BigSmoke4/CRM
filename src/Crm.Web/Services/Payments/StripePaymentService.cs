using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Services.Payments;

/// <summary>
/// Real Stripe integration: creates Checkout Sessions for invoices/orders and
/// verifies webhook signatures. Keys are org-configured and encrypted at rest.
/// </summary>
public class StripePaymentService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly ISecretProtector _secrets;

    public StripePaymentService(ApplicationDbContext db, IHttpClientFactory http, ISecretProtector secrets)
    { _db = db; _http = http; _secrets = secrets; }

    public async Task<PaymentProviderConfig?> GetConfigAsync(Guid orgId, CancellationToken ct = default)
        => await _db.PaymentProviderConfigs.FirstOrDefaultAsync(p => p.OrganizationId == orgId && p.IsEnabled, ct);

    /// <summary>Create a Stripe Checkout Session for an invoice. Returns checkout URL.</summary>
    public async Task<(string? CheckoutUrl, string? Error)> CreateInvoiceCheckoutAsync(Guid orgId, Invoice invoice, string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        var cfg = await GetConfigAsync(orgId, ct);
        if (cfg == null) return (null, "Stripe is not configured or disabled.");
        var key = _secrets.UnprotectOrNull(cfg.SecretKeyEncrypted);
        if (string.IsNullOrEmpty(key)) return (null, "Stripe secret key missing.");

        var amountCents = (long)Math.Round(invoice.Total * 100, MidpointRounding.AwayFromZero);
        var form = new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["client_reference_id"] = invoice.Id.ToString(),
            ["line_items[0][quantity]"] = "1",
            ["line_items[0][price_data][currency]"] = invoice.Currency.ToLowerInvariant(),
            ["line_items[0][price_data][unit_amount]"] = amountCents.ToString(),
            ["line_items[0][price_data][product_data][name]"] = $"Invoice {invoice.InvoiceNumber}",
            ["metadata[invoice_id]"] = invoice.Id.ToString(),
            ["metadata[org_id]"] = orgId.ToString(),
            ["payment_intent_data[metadata][invoice_id]"] = invoice.Id.ToString()
        };
        // customer email if known
        if (invoice.Contact?.Email != null) form["customer_email"] = invoice.Contact.Email;

        var client = _http.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/checkout/sessions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new FormUrlEncodedContent(form);
        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            return (null, $"Stripe error {(int)resp.StatusCode}: {body[..Math.Min(300, body.Length)]}");
        using var doc = JsonDocument.Parse(body);
        var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
        var sessionId = doc.RootElement.TryGetProperty("id", out var sid) ? sid.GetString() : null;
        if (sessionId != null) invoice.Notes = (invoice.Notes ?? "") + $"\n[stripe_session:{sessionId}]";
        return (url, null);
    }

    /// <summary>Verify Stripe webhook signature (Stripe-Signature header, HMAC-SHA256).</summary>
    public bool VerifyWebhook(string payload, string signatureHeader, string webhookSecret)
    {
        try
        {
            // format: t=timestamp,v1=sig[,v1=sig2]
            var parts = signatureHeader.Split(',');
            var t = parts.FirstOrDefault(p => p.StartsWith("t="))?[2..];
            var sigs = parts.Where(p => p.StartsWith("v1=")).Select(p => p[3..]).ToList();
            if (t == null || !sigs.Any()) return false;
            var signed = $"{t}.{payload}";
            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
            return sigs.Any(s => string.Equals(s, hash, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>Handle checkout.session.completed → mark invoice paid.</summary>
    public async Task HandleWebhookAsync(string payload, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (root.TryGetProperty("type", out var type) && type.GetString() == "checkout.session.completed"
            && root.TryGetProperty("data", out var data) && data.TryGetProperty("object", out var obj))
        {
            var invoiceIdStr = obj.TryGetProperty("metadata", out var md) && md.TryGetProperty("invoice_id", out var iid)
                ? iid.GetString() : obj.TryGetProperty("client_reference_id", out var cri) ? cri.GetString() : null;
            if (Guid.TryParse(invoiceIdStr, out var invoiceId))
            {
                var inv = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
                if (inv != null && inv.Status != InvoiceStatus.Paid)
                {
                    inv.Status = InvoiceStatus.Paid;
                    inv.AmountPaid = inv.Total;
                    inv.PaidAt = DateTimeOffset.UtcNow;
                    inv.UpdatedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(ct);
                }
            }
        }
    }
}
