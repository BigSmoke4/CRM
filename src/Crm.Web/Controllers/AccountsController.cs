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
public class AccountsController : Controller
{
    private const int PageSize = 25;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public AccountsController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db; _tenant = tenant; _audit = audit;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.AccountsView)]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var query = _db.Accounts.Where(a => !a.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(a => a.Name.ToLower().Contains(term) ||
                (a.Industry != null && a.Industry.ToLower().Contains(term)) ||
                (a.City != null && a.City.ToLower().Contains(term)));
        }
        var total = await query.CountAsync();
        var items = await query.OrderBy(a => a.Name)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(a => new AccountListItem
            {
                Id = a.Id, Name = a.Name, Industry = a.Industry, Website = a.Website,
                City = a.City, Country = a.Country, ContactCount = a.Contacts.Count(c => !c.IsDeleted),
                LifetimeValue = a.LifetimeValue, AssignedTo = a.AssignedTo != null ? a.AssignedTo.FirstName + " " + a.AssignedTo.LastName : null
            }).ToListAsync();
        ViewBag.Q = q; ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)PageSize); ViewBag.Total = total;
        return View(items);
    }

    [HttpGet]
    [Authorize(Policy = Permissions.AccountsCreate)]
    public async Task<IActionResult> Create()
    {
        await PopulateUsers();
        return View(new AccountFormVm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.AccountsCreate)]
    public async Task<IActionResult> Create(AccountFormVm vm)
    {
        if (!ModelState.IsValid) { await PopulateUsers(); return View(vm); }
        var a = MapToEntity(new Account { OrganizationId = _tenant.OrganizationId!.Value }, vm);
        _db.Accounts.Add(a);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Account.Created", "Account", a.Id.ToString(), new { a.Name });
        TempData["Success"] = "Account created.";
        return RedirectToAction(nameof(Details), new { id = a.Id });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.AccountsEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var a = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound();
        await PopulateUsers();
        return View(MapToVm(a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.AccountsEdit)]
    public async Task<IActionResult> Edit(Guid id, AccountFormVm vm)
    {
        var a = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound();
        if (!ModelState.IsValid) { await PopulateUsers(); return View(vm); }
        MapToEntity(a, vm);
        a.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Account.Updated", "Account", id.ToString());
        TempData["Success"] = "Account updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.AccountsView)]
    public async Task<IActionResult> Details(Guid id)
    {
        var a = await _db.Accounts.Include(x => x.AssignedTo)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound();

        var contacts = await _db.Contacts.Where(c => c.AccountId == id && !c.IsDeleted)
            .OrderBy(c => c.LastName).ToListAsync();
        var deals = await _db.Deals.Where(d => d.AccountId == id && !d.IsDeleted)
            .Include(d => d.Stage).OrderByDescending(d => d.UpdatedAt).Take(20).ToListAsync();
        var activities = await _db.Activities.Where(x => x.AccountId == id)
            .OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync();
        var invoices = await _db.Invoices.Where(i => i.AccountId == id)
            .OrderByDescending(i => i.CreatedAt).Take(20).ToListAsync();
        var orders = await _db.Orders.Where(o => o.AccountId == id)
            .OrderByDescending(o => o.CreatedAt).Take(20).ToListAsync();

        ViewBag.Contacts = contacts; ViewBag.Deals = deals; ViewBag.Activities = activities;
        ViewBag.Invoices = invoices; ViewBag.Orders = orders;
        return View(a);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.AccountsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var a = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        a.IsDeleted = true; a.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Account.Deleted", "Account", id.ToString());
        TempData["Success"] = "Account deleted.";
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

    private static Account MapToEntity(Account a, AccountFormVm vm)
    {
        a.Name = vm.Name.Trim(); a.Industry = N(vm.Industry); a.Website = N(vm.Website);
        a.CompanySize = N(vm.CompanySize); a.AnnualRevenue = vm.AnnualRevenue;
        a.AddressLine1 = N(vm.AddressLine1); a.City = N(vm.City); a.State = N(vm.State);
        a.PostalCode = N(vm.PostalCode); a.Country = N(vm.Country);
        a.Phone = N(vm.Phone); a.Email = N(vm.Email); a.AssignedToId = vm.AssignedToId;
        a.Tags = N(vm.Tags); a.Notes = N(vm.Notes);
        return a;
    }

    private static AccountFormVm MapToVm(Account a) => new()
    {
        Id = a.Id, Name = a.Name, Industry = a.Industry, Website = a.Website, CompanySize = a.CompanySize,
        AnnualRevenue = a.AnnualRevenue, AddressLine1 = a.AddressLine1, City = a.City, State = a.State,
        PostalCode = a.PostalCode, Country = a.Country, Phone = a.Phone, Email = a.Email,
        AssignedToId = a.AssignedToId, Tags = a.Tags, Notes = a.Notes
    };

    private static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class AccountListItem
    {
        public Guid Id { get; set; } public string Name { get; set; } = "";
        public string? Industry { get; set; } public string? Website { get; set; }
        public string? City { get; set; } public string? Country { get; set; }
        public int ContactCount { get; set; } public decimal LifetimeValue { get; set; }
        public string? AssignedTo { get; set; }
    }
}
