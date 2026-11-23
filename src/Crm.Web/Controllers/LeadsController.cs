using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Crm.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

[Authorize]
public class LeadsController : Controller
{
    private const int PageSize = 25;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public LeadsController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db; _tenant = tenant; _audit = audit;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.LeadsView)]
    public async Task<IActionResult> Index(string? q, string? status, int page = 1)
    {
        var query = _db.Leads.Where(l => !l.IsDeleted && l.ConvertedContactId == null).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(l => l.FirstName.ToLower().Contains(term) || l.LastName.ToLower().Contains(term) ||
                (l.Email != null && l.Email.ToLower().Contains(term)) ||
                (l.CompanyName != null && l.CompanyName.ToLower().Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LeadStatus>(status, out var st))
            query = query.Where(l => l.Status == st);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(l => l.Score).ThenByDescending(l => l.CreatedAt)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(l => new LeadListItem
            {
                Id = l.Id, Name = l.FirstName + " " + l.LastName, Email = l.Email,
                CompanyName = l.CompanyName, Source = l.Source, Status = l.Status,
                Score = l.Score, AssignedTo = l.AssignedTo != null ? l.AssignedTo.FirstName + " " + l.AssignedTo.LastName : null,
                CreatedAt = l.CreatedAt
            }).ToListAsync();

        ViewBag.Q = q; ViewBag.Status = status; ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)PageSize); ViewBag.Total = total;
        ViewBag.Statuses = Enum.GetValues<LeadStatus>().Select(e => new SelectListItem(e.ToString(), e.ToString()));
        return View(items);
    }

    [HttpGet]
    [Authorize(Policy = Permissions.LeadsCreate)]
    public async Task<IActionResult> Create() { await PopulateUsers(); return View(new LeadFormVm()); }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.LeadsCreate)]
    public async Task<IActionResult> Create(LeadFormVm vm)
    {
        if (!ModelState.IsValid) { await PopulateUsers(); return View(vm); }
        var lead = MapToEntity(new Lead { OrganizationId = _tenant.OrganizationId!.Value }, vm);
        lead.Score = ComputeScore(lead);
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Lead.Created", "Lead", lead.Id.ToString());
        TempData["Success"] = "Lead created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.LeadsEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var l = await _db.Leads.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted && x.ConvertedContactId == null);
        if (l == null) return NotFound();
        await PopulateUsers();
        return View(MapToVm(l));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.LeadsEdit)]
    public async Task<IActionResult> Edit(Guid id, LeadFormVm vm)
    {
        var l = await _db.Leads.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (l == null) return NotFound();
        if (!ModelState.IsValid) { await PopulateUsers(); return View(vm); }
        MapToEntity(l, vm);
        l.Score = ComputeScore(l);
        l.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Lead.Updated", "Lead", id.ToString());
        TempData["Success"] = "Lead updated.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Convert a lead into a Contact (+ optional Account). Marks lead converted.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.LeadsEdit)]
    public async Task<IActionResult> Convert(Guid id, bool createAccount = false)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (lead == null) return NotFound();
        if (lead.ConvertedContactId != null)
        {
            TempData["Error"] = "Lead already converted.";
            return RedirectToAction(nameof(Index));
        }

        Guid? accountId = null;
        if (createAccount && !string.IsNullOrWhiteSpace(lead.CompanyName))
        {
            var existing = await _db.Accounts.FirstOrDefaultAsync(a => !a.IsDeleted && a.Name == lead.CompanyName);
            if (existing != null) accountId = existing.Id;
            else
            {
                var acc = new Account { OrganizationId = lead.OrganizationId, Name = lead.CompanyName, Website = lead.Website };
                _db.Accounts.Add(acc);
                await _db.SaveChangesAsync();
                accountId = acc.Id;
            }
        }

        var contact = new Contact
        {
            OrganizationId = lead.OrganizationId,
            FirstName = lead.FirstName, LastName = lead.LastName,
            Email = lead.Email, Phone = lead.Phone, JobTitle = lead.JobTitle,
            AccountId = accountId, Website = lead.Website, Country = lead.Country,
            Source = lead.Source ?? "Lead", CustomerType = CustomerType.Prospect,
            LeadStatus = LeadStatus.Qualified, AssignedToId = lead.AssignedToId,
            LeadScore = lead.Score, ExternalRefId = lead.ExternalRefId, ExternalSystem = lead.ExternalSystem
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();

        lead.ConvertedContactId = contact.Id;
        lead.ConvertedAt = DateTimeOffset.UtcNow;
        lead.Status = LeadStatus.Converted;
        lead.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Lead.Converted", "Lead", id.ToString(), new { ContactId = contact.Id });

        TempData["Success"] = "Lead converted to contact.";
        return RedirectToAction("Details", "Contacts", new { id = contact.Id });
    }

    /// <summary>Simple heuristic lead score 0-100 until configurable scoring rules ship.</summary>
    private static int ComputeScore(Lead l)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(l.Email)) score += 15;
        if (!string.IsNullOrWhiteSpace(l.Phone)) score += 10;
        if (!string.IsNullOrWhiteSpace(l.CompanyName)) score += 15;
        if (!string.IsNullOrWhiteSpace(l.JobTitle)) score += 15;
        if (!string.IsNullOrWhiteSpace(l.Website)) score += 10;
        if (!string.IsNullOrWhiteSpace(l.Country)) score += 5;
        if (!string.IsNullOrWhiteSpace(l.Source) && l.Source != "manual") score += 10;
        if (!string.IsNullOrWhiteSpace(l.Campaign)) score += 10;
        if (!string.IsNullOrWhiteSpace(l.UtmSource)) score += 10;
        return Math.Min(score, 100);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.LeadsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var l = await _db.Leads.FirstOrDefaultAsync(x => x.Id == id);
        if (l == null) return NotFound();
        l.IsDeleted = true; l.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Lead.Deleted", "Lead", id.ToString());
        TempData["Success"] = "Lead deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateUsers()
    {
        var users = await _db.Users.Where(u => u.OrganizationId == _tenant.OrganizationId && u.IsActive)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToListAsync();
        ViewBag.Users = new SelectList(users.Select(u => new
        {
            u.Id,
            Name = (u.FirstName + " " + u.LastName).Trim() is { Length: > 0 } n ? n : u.Email ?? u.Id
        }), "Id", "Name");
    }

    private static Lead MapToEntity(Lead l, LeadFormVm vm)
    {
        l.FirstName = vm.FirstName.Trim(); l.LastName = vm.LastName.Trim();
        l.Email = N(vm.Email); l.Phone = N(vm.Phone); l.CompanyName = N(vm.CompanyName);
        l.JobTitle = N(vm.JobTitle); l.Industry = N(vm.Industry); l.Country = N(vm.Country);
        l.Website = N(vm.Website); l.Source = N(vm.Source); l.Campaign = N(vm.Campaign);
        l.AssignedToId = vm.AssignedToId; l.Status = vm.Status; l.Notes = N(vm.Notes);
        return l;
    }

    private static LeadFormVm MapToVm(Lead l) => new()
    {
        Id = l.Id, FirstName = l.FirstName, LastName = l.LastName, Email = l.Email, Phone = l.Phone,
        CompanyName = l.CompanyName, JobTitle = l.JobTitle, Industry = l.Industry, Country = l.Country,
        Website = l.Website, Source = l.Source, Campaign = l.Campaign, AssignedToId = l.AssignedToId,
        Status = l.Status, Notes = l.Notes
    };

    private static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class LeadListItem
    {
        public Guid Id { get; set; } public string Name { get; set; } = "";
        public string? Email { get; set; } public string? CompanyName { get; set; }
        public string? Source { get; set; } public LeadStatus Status { get; set; }
        public int Score { get; set; } public string? AssignedTo { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
