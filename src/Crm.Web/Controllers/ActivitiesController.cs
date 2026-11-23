using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Crm.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>Tasks, calls, meetings, notes and other timeline activities.</summary>
[Authorize]
public class ActivitiesController : Controller
{
    private const int PageSize = 25;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public ActivitiesController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db; _tenant = tenant; _audit = audit;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ActivitiesView)]
    public async Task<IActionResult> Index(string? type, string? q, bool? overdue, int page = 1)
    {
        var query = _db.Activities.AsQueryable();
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<ActivityType>(type, out var t))
            query = query.Where(a => a.Type == t);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(a => a.Subject.ToLower().Contains(term) ||
                (a.Body != null && a.Body.ToLower().Contains(term)));
        }
        if (overdue == true)
            query = query.Where(a => a.Type == ActivityType.Task && !a.IsCompleted && a.DueAt < DateTimeOffset.UtcNow);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(a => a.DueAt ?? a.CreatedAt)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(a => new ActivityListItem
            {
                Id = a.Id, Type = a.Type, Subject = a.Subject, DueAt = a.DueAt,
                IsCompleted = a.IsCompleted, CreatedAt = a.CreatedAt,
                ContactName = a.ContactId != null ? _db.Contacts.Where(c => c.Id == a.ContactId).Select(c => c.FirstName + " " + c.LastName).FirstOrDefault() : null,
                Owner = a.OwnerId != null ? _db.Users.Where(u => u.Id == a.OwnerId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() : null
            }).ToListAsync();

        ViewBag.Type = type; ViewBag.Q = q; ViewBag.Overdue = overdue; ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)PageSize); ViewBag.Total = total;
        ViewBag.Types = Enum.GetValues<ActivityType>().Select(e => new SelectListItem(e.ToString(), e.ToString()));
        return View(items);
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ActivitiesCreate)]
    public async Task<IActionResult> Create(Guid? contactId = null, Guid? accountId = null, Guid? leadId = null, Guid? dealId = null)
    {
        var vm = new ActivityFormVm { ContactId = contactId, AccountId = accountId, LeadId = leadId, DealId = dealId };
        await PopulateLookups();
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ActivitiesCreate)]
    public async Task<IActionResult> Create(ActivityFormVm vm)
    {
        if (!ModelState.IsValid) { await PopulateLookups(); return View(vm); }
        var a = new Activity
        {
            OrganizationId = _tenant.OrganizationId!.Value,
            Type = vm.Type, Subject = vm.Subject.Trim(), Body = vm.Body,
            ContactId = vm.ContactId, AccountId = vm.AccountId, LeadId = vm.LeadId, DealId = vm.DealId,
            DueAt = vm.DueAt, DurationMinutes = vm.DurationMinutes,
            OwnerId = vm.OwnerId ?? _tenant.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Activities.Add(a);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Activity.Created", "Activity", a.Id.ToString(), new { a.Type, a.Subject });
        TempData["Success"] = "Activity created.";
        return RedirectAfterSave(a);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ActivitiesEdit)]
    public async Task<IActionResult> Complete(Guid id)
    {
        var a = await _db.Activities.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        a.IsCompleted = !a.IsCompleted;
        a.CompletedAt = a.IsCompleted ? DateTimeOffset.UtcNow : null;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ActivitiesDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var a = await _db.Activities.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        _db.Activities.Remove(a);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Activity.Deleted", "Activity", id.ToString());
        TempData["Success"] = "Activity deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.ActivitiesCreate)]
    public async Task<IActionResult> QuickTask(string subject, Guid? contactId = null)
    {
        if (string.IsNullOrWhiteSpace(subject)) return BadRequest();
        _db.Activities.Add(new Activity
        {
            OrganizationId = _tenant.OrganizationId!.Value, Type = ActivityType.Task,
            Subject = subject.Trim(), ContactId = contactId, OwnerId = _tenant.UserId
        });
        await _db.SaveChangesAsync();
        return Ok();
    }

    private IActionResult RedirectAfterSave(Activity a)
    {
        if (a.ContactId.HasValue) return RedirectToAction("Details", "Contacts", new { id = a.ContactId });
        if (a.AccountId.HasValue) return RedirectToAction("Details", "Accounts", new { id = a.AccountId });
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateLookups()
    {
        ViewBag.Types = Enum.GetValues<ActivityType>().Select(e => new SelectListItem(e.ToString(), e.ToString(), e == ActivityType.Task));
        ViewBag.Contacts = new SelectList(await _db.Contacts.Where(c => !c.IsDeleted).OrderBy(c => c.LastName).Select(c => new { c.Id, Name = c.FirstName + " " + c.LastName }).ToListAsync(), "Id", "Name");
        ViewBag.Accounts = new SelectList(await _db.Accounts.Where(a => !a.IsDeleted).OrderBy(a => a.Name).Select(a => new { a.Id, a.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Leads = new SelectList(await _db.Leads.Where(l => !l.IsDeleted && l.ConvertedContactId == null).OrderBy(l => l.LastName).Select(l => new { l.Id, Name = l.FirstName + " " + l.LastName }).ToListAsync(), "Id", "Name");
        ViewBag.Deals = new SelectList(await _db.Deals.Where(d => !d.IsDeleted).OrderBy(d => d.Name).Select(d => new { d.Id, d.Name }).ToListAsync(), "Id", "Name");
        var users = await _db.Users.Where(u => u.OrganizationId == _tenant.OrganizationId && u.IsActive)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToListAsync();
        ViewBag.Users = new SelectList(users.Select(u => new
        {
            u.Id,
            Name = (u.FirstName + " " + u.LastName).Trim() is { Length: > 0 } n ? n : u.Email ?? u.Id
        }), "Id", "Name");
    }

    public class ActivityListItem
    {
        public Guid Id { get; set; } public ActivityType Type { get; set; }
        public string Subject { get; set; } = ""; public DateTimeOffset? DueAt { get; set; }
        public bool IsCompleted { get; set; } public DateTimeOffset CreatedAt { get; set; }
        public string? ContactName { get; set; } public string? Owner { get; set; }
    }
}
