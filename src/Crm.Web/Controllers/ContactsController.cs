using System.Text;
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
public class ContactsController : Controller
{
    private const int PageSize = 25;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public ContactsController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db; _tenant = tenant; _audit = audit;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ContactsView)]
    public async Task<IActionResult> Index(string? q, string? tag, string? status, int page = 1)
    {
        var query = _db.Contacts.Where(c => !c.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(c =>
                c.FirstName.ToLower().Contains(term) || c.LastName.ToLower().Contains(term) ||
                (c.Email != null && c.Email.ToLower().Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)) ||
                (c.JobTitle != null && c.JobTitle.ToLower().Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(tag))
            query = query.Where(c => c.Tags != null && c.Tags.Contains(tag));
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<CustomerType>(status, out var ct))
            query = query.Where(c => c.CustomerType == ct);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(c => new ContactListItem
            {
                Id = c.Id, Name = c.FirstName + " " + c.LastName, Email = c.Email,
                Phone = c.Phone, JobTitle = c.JobTitle, Company = c.Account != null ? c.Account.Name : null,
                CustomerType = c.CustomerType, LeadScore = c.LeadScore, Tags = c.Tags,
                AssignedTo = c.AssignedTo != null ? c.AssignedTo.FirstName + " " + c.AssignedTo.LastName : null,
                UpdatedAt = c.UpdatedAt
            }).ToListAsync();

        ViewBag.Q = q; ViewBag.Tag = tag; ViewBag.Status = status;
        ViewBag.Page = page; ViewBag.TotalPages = (int)Math.Ceiling(total / (double)PageSize);
        ViewBag.Total = total;
        return View(items);
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ContactsCreate)]
    public async Task<IActionResult> Create(Guid? accountId = null)
    {
        var vm = new ContactFormVm { AccountId = accountId };
        await PopulateLookups(vm);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ContactsCreate)]
    public async Task<IActionResult> Create(ContactFormVm vm)
    {
        if (!ModelState.IsValid) { await PopulateLookups(vm); return View(vm); }

        var contact = MapToEntity(new Contact { OrganizationId = _tenant.OrganizationId!.Value }, vm);
        contact.CreatedById = _tenant.UserId;
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();

        _db.Activities.Add(new Activity
        {
            OrganizationId = contact.OrganizationId, Type = ActivityType.System,
            Subject = "Contact created", ContactId = contact.Id, OwnerId = _tenant.UserId
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Contact.Created", "Contact", contact.Id.ToString(), new { contact.Email });

        TempData["Success"] = "Contact created.";
        return RedirectToAction(nameof(Details), new { id = contact.Id });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ContactsEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var c = await _db.Contacts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (c == null) return NotFound();
        var vm = MapToVm(c);
        await PopulateLookups(vm);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ContactsEdit)]
    public async Task<IActionResult> Edit(Guid id, ContactFormVm vm)
    {
        var c = await _db.Contacts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (c == null) return NotFound();
        if (!ModelState.IsValid) { await PopulateLookups(vm); return View(vm); }

        MapToEntity(c, vm);
        c.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Contact.Updated", "Contact", id.ToString());

        TempData["Success"] = "Contact updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ContactsView)]
    public async Task<IActionResult> Details(Guid id)
    {
        var c = await _db.Contacts
            .Include(x => x.Account)
            .Include(x => x.AssignedTo)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (c == null) return NotFound();

        var activities = await _db.Activities
            .Where(a => a.ContactId == id || a.AccountId == c.AccountId)
            .OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync();

        var notes = await _db.ContactNotes.Where(n => n.ContactId == id)
            .OrderByDescending(n => n.CreatedAt).ToListAsync();

        ViewBag.Activities = activities;
        ViewBag.Notes = notes;
        ViewBag.CanEdit = User.HasClaim("permission", Permissions.ContactsEdit);
        return View(c);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ContactsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var c = await _db.Contacts.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        c.IsDeleted = true;
        c.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Contact.Deleted", "Contact", id.ToString());
        TempData["Success"] = "Contact deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ContactsExport)]
    public async Task<IActionResult> Export(string? q, string? tag)
    {
        var query = _db.Contacts.Where(c => !c.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(c => c.FirstName.ToLower().Contains(term) || c.LastName.ToLower().Contains(term) ||
                (c.Email != null && c.Email.ToLower().Contains(term)));
        }
        var items = await query.OrderBy(c => c.LastName).Take(10000).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("FirstName,LastName,Email,Phone,Mobile,JobTitle,Company,City,Country,Source,CustomerType,LeadStatus,LeadScore,Tags,CreatedAt");
        foreach (var c in items)
        {
            var company = c.AccountId != null ? await _db.Accounts.Where(a => a.Id == c.AccountId).Select(a => a.Name).FirstOrDefaultAsync() : null;
            sb.AppendLine(string.Join(",", Csv(c.FirstName), Csv(c.LastName), Csv(c.Email), Csv(c.Phone), Csv(c.Mobile),
                Csv(c.JobTitle), Csv(company), Csv(c.City), Csv(c.Country), Csv(c.Source),
                c.CustomerType, c.LeadStatus, c.LeadScore, Csv(c.Tags), c.CreatedAt.ToString("o")));
        }
        await _audit.LogAsync("Contact.Exported", "Contact", null, new { Count = items.Count });
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"contacts-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpPost]
    [Authorize(Policy = Permissions.ContactsImport)]
    public IActionResult Import() => View(); // CSV import UI; POST handled by ImportController

    private async Task PopulateLookups(ContactFormVm vm)
    {
        ViewBag.Accounts = new SelectList(await _db.Accounts.Where(a => !a.IsDeleted).OrderBy(a => a.Name).ToListAsync(), "Id", "Name", vm.AccountId);
        var users = await _db.Users.Where(u => u.OrganizationId == _tenant.OrganizationId && u.IsActive)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToListAsync();
        ViewBag.Users = new SelectList(users.Select(u => new
        {
            u.Id,
            Name = (u.FirstName + " " + u.LastName).Trim() is { Length: > 0 } n ? n : u.Email ?? u.Id
        }), "Id", "Name", vm.AssignedToId);
        ViewBag.CustomerTypes = Enum.GetValues<CustomerType>().Select(e => new SelectListItem(e.ToString(), e.ToString()));
        ViewBag.LeadStatuses = Enum.GetValues<LeadStatus>().Select(e => new SelectListItem(e.ToString(), e.ToString()));
    }

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static Contact MapToEntity(Contact c, ContactFormVm vm)
    {
        c.FirstName = vm.FirstName.Trim(); c.LastName = vm.LastName.Trim();
        c.Email = NullIfEmpty(vm.Email); c.Phone = NullIfEmpty(vm.Phone); c.Mobile = NullIfEmpty(vm.Mobile);
        c.JobTitle = NullIfEmpty(vm.JobTitle); c.AccountId = vm.AccountId;
        c.AddressLine1 = NullIfEmpty(vm.AddressLine1); c.City = NullIfEmpty(vm.City);
        c.State = NullIfEmpty(vm.State); c.PostalCode = NullIfEmpty(vm.PostalCode); c.Country = NullIfEmpty(vm.Country);
        c.Website = NullIfEmpty(vm.Website); c.LinkedInUrl = NullIfEmpty(vm.LinkedInUrl);
        c.FacebookUrl = NullIfEmpty(vm.FacebookUrl); c.WhatsAppNumber = NullIfEmpty(vm.WhatsAppNumber);
        c.Source = NullIfEmpty(vm.Source); c.CustomerType = vm.CustomerType; c.LeadStatus = vm.LeadStatus;
        c.LeadScore = vm.LeadScore; c.AssignedToId = vm.AssignedToId; c.Tags = NullIfEmpty(vm.Tags);
        c.Notes = NullIfEmpty(vm.Notes); c.EmailOptIn = vm.EmailOptIn; c.SmsOptIn = vm.SmsOptIn;
        c.ExternalRefId = NullIfEmpty(vm.ExternalRefId);
        return c;
    }

    private static ContactFormVm MapToVm(Contact c) => new()
    {
        Id = c.Id, FirstName = c.FirstName, LastName = c.LastName, Email = c.Email, Phone = c.Phone,
        Mobile = c.Mobile, JobTitle = c.JobTitle, AccountId = c.AccountId, AddressLine1 = c.AddressLine1,
        City = c.City, State = c.State, PostalCode = c.PostalCode, Country = c.Country, Website = c.Website,
        LinkedInUrl = c.LinkedInUrl, FacebookUrl = c.FacebookUrl, WhatsAppNumber = c.WhatsAppNumber,
        Source = c.Source, CustomerType = c.CustomerType, LeadStatus = c.LeadStatus, LeadScore = c.LeadScore,
        AssignedToId = c.AssignedToId, Tags = c.Tags, Notes = c.Notes, EmailOptIn = c.EmailOptIn,
        SmsOptIn = c.SmsOptIn, ExternalRefId = c.ExternalRefId
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class ContactListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? JobTitle { get; set; }
        public string? Company { get; set; }
        public CustomerType CustomerType { get; set; }
        public int LeadScore { get; set; }
        public string? Tags { get; set; }
        public string? AssignedTo { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
