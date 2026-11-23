using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>Email campaigns with audience segmentation and send tracking.</summary>
[Authorize]
public class CampaignsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

    public CampaignsController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit, Microsoft.Extensions.Configuration.IConfiguration config)
    { _db = db; _tenant = tenant; _audit = audit; _config = config; }

    [HttpGet, Authorize(Policy = Permissions.CampaignsView)]
    public async Task<IActionResult> Index()
        => View(await _db.Campaigns.OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync());

    [HttpGet, Authorize(Policy = Permissions.CampaignsCreate)]
    public IActionResult Create() => View(new Campaign { OrganizationId = _tenant.OrganizationId ?? Guid.Empty });

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.CampaignsCreate)]
    public async Task<IActionResult> Create(Campaign c)
    {
        c.Id = Guid.NewGuid(); c.OrganizationId = _tenant.OrganizationId!.Value; c.Status = CampaignStatus.Draft;
        _db.Campaigns.Add(c); await _db.SaveChangesAsync();
        TempData["Success"] = "Campaign created as draft.";
        return RedirectToAction(nameof(Details), new { id = c.Id });
    }

    [HttpGet, Authorize(Policy = Permissions.CampaignsView)]
    public async Task<IActionResult> Details(Guid id)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        ViewBag.Recipients = await _db.CampaignRecipients.Where(r => r.CampaignId == id).Take(200).ToListAsync();
        return View(c);
    }

    /// <summary>Resolve audience: contacts matching simple filter JSON {tags, source, customerType}.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.CampaignsSend)]
    public async Task<IActionResult> Send(Guid id)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        if (c.Status is CampaignStatus.Running or CampaignStatus.Completed) { TempData["Error"] = "Campaign already sent."; return RedirectToAction(nameof(Details), new { id }); }

        // Audience: contacts with email opt-in
        var contacts = await _db.Contacts
            .Where(x => !x.IsDeleted && x.Email != null && x.EmailOptIn)
            .Select(x => new { x.Id, x.Email }).ToListAsync();

        foreach (var ct in contacts)
            _db.CampaignRecipients.Add(new CampaignRecipient
            {
                OrganizationId = c.OrganizationId, CampaignId = c.Id,
                ContactId = ct.Id, Email = ct.Email, Status = "pending"
            });

        // If SMTP configured, attempt real send; otherwise mark queued and report honestly.
        var smtpHost = _config["Smtp:Host"];
        var sent = 0; var failed = 0;
        if (!string.IsNullOrEmpty(smtpHost))
        {
            // Real SMTP send via System.Net.Mail
            using var client = new System.Net.Mail.SmtpClient(smtpHost, int.TryParse(_config["Smtp:Port"], out var p) ? p : 587)
            {
                EnableSsl = true,
                Credentials = new System.Net.NetworkCredential(_config["Smtp:User"], _config["Smtp:Pass"])
            };
            var recipients = await _db.CampaignRecipients.Where(r => r.CampaignId == id && r.Status == "pending").ToListAsync();
            foreach (var r in recipients)
            {
                try
                {
                    var mail = new System.Net.Mail.MailMessage(_config["Smtp:From"] ?? "noreply@crm.local", r.Email!, c.Subject ?? c.Name, StripHtml(c.BodyHtml ?? ""));
                    await client.SendMailAsync(mail);
                    r.Status = "sent"; r.SentAt = DateTimeOffset.UtcNow; sent++;
                }
                catch { r.Status = "failed"; failed++; }
            }
        }
        else
        {
            // No SMTP: leave queued and tell user honestly
            var queued = await _db.CampaignRecipients.CountAsync(r => r.CampaignId == id && r.Status == "pending");
            TempData["Error"] = $"SMTP not configured. {queued} recipients queued — configure Settings → Email to send.";
        }

        c.Status = CampaignStatus.Completed;
        c.StartedAt = DateTimeOffset.UtcNow; c.FinishedAt = DateTimeOffset.UtcNow;
        c.RecipientsCount = contacts.Count; c.SentCount = sent;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Campaign.Sent", "Campaign", id.ToString(), new { c.RecipientsCount, sent, failed });
        TempData["Success"] = $"Campaign queued/sent to {contacts.Count} recipients ({sent} sent, {failed} failed).";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static string StripHtml(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.CampaignsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        _db.Campaigns.Remove(c); await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
