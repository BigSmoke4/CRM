using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Crm.Web.ViewModels;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

[Authorize]
public class TicketsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public TicketsController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet]
    [Authorize(Policy = Permissions.TicketsView)]
    public async Task<IActionResult> Index(string? status, string? priority, string? q, int page = 1)
    {
        var query = _db.Tickets.Include(t => t.Contact).Include(t => t.Assignee).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, out var st))
            query = query.Where(t => t.Status == st);
        if (!string.IsNullOrWhiteSpace(priority) && Enum.TryParse<TicketPriority>(priority, out var pr))
            query = query.Where(t => t.Priority == pr);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(t => t.Subject.ToLower().Contains(term) || t.TicketNumber.Contains(term) ||
                (t.Contact != null && (t.Contact.FirstName + " " + t.Contact.LastName).ToLower().Contains(term)));
        }
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * 25).Take(25).ToListAsync();
        ViewBag.Status = status; ViewBag.Priority = priority; ViewBag.Q = q; ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / 25.0); ViewBag.Total = total;
        ViewBag.Statuses = Enum.GetValues<TicketStatus>().Select(e => new SelectListItem(e.ToString(), e.ToString()));
        ViewBag.Priorities = Enum.GetValues<TicketPriority>().Select(e => new SelectListItem(e.ToString(), e.ToString()));
        return View(items);
    }

    [HttpGet]
    [Authorize(Policy = Permissions.TicketsCreate)]
    public async Task<IActionResult> Create()
    {
        await PopulateLookups();
        return View(new TicketFormVm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.TicketsCreate)]
    public async Task<IActionResult> Create(TicketFormVm vm)
    {
        if (!ModelState.IsValid) { await PopulateLookups(); return View(vm); }
        var t = new Ticket
        {
            OrganizationId = _tenant.OrganizationId!.Value,
            TicketNumber = await NextTicketNumber(),
            Subject = vm.Subject.Trim(), Description = vm.Description,
            ContactId = vm.ContactId, AccountId = vm.AccountId,
            Priority = vm.Priority, Category = vm.Category,
            AssigneeId = vm.AssigneeId ?? _tenant.UserId,
            Source = "portal", SlaDueAt = DateTimeOffset.UtcNow.AddHours(vm.Priority == TicketPriority.Urgent ? 4 : vm.Priority == TicketPriority.High ? 24 : 72)
        };
        _db.Tickets.Add(t);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Ticket.Created", "Ticket", t.Id.ToString(), new { t.TicketNumber });
        TempData["Success"] = $"Ticket {t.TicketNumber} created.";
        return RedirectToAction(nameof(Details), new { id = t.Id });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.TicketsView)]
    public async Task<IActionResult> Details(Guid id)
    {
        var t = await _db.Tickets.Include(x => x.Contact).Include(x => x.Assignee)
            .Include(x => x.Replies).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        await PopulateLookups();
        ViewBag.ReplyVm = new TicketReplyVm { TicketId = id };
        return View(t);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.TicketsEdit)]
    public async Task<IActionResult> Reply(TicketReplyVm vm)
    {
        var t = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == vm.TicketId);
        if (t == null) return NotFound();
        _db.TicketReplies.Add(new TicketReply
        {
            OrganizationId = t.OrganizationId, TicketId = t.Id, Body = vm.Body,
            IsInternalNote = vm.IsInternalNote, AuthorId = _tenant.UserId
        });
        if (!vm.IsInternalNote && t.Status == TicketStatus.New) t.Status = TicketStatus.Open;
        if (!t.FirstResponseAt.HasValue && !vm.IsInternalNote) t.FirstResponseAt = DateTimeOffset.UtcNow;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = t.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.TicketsEdit)]
    public async Task<IActionResult> UpdateStatus(Guid id, TicketStatus status)
    {
        var t = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        t.Status = status;
        if (status is TicketStatus.Resolved or TicketStatus.Closed) { t.ResolvedAt ??= DateTimeOffset.UtcNow; }
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.TicketsAssign)]
    public async Task<IActionResult> Assign(Guid id, string? assigneeId)
    {
        var t = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        t.AssigneeId = assigneeId;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.TicketsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var t = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        _db.Tickets.Remove(t);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Ticket deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string> NextTicketNumber()
    {
        var count = await _db.Tickets.CountAsync() + 1;
        return $"T-{DateTime.UtcNow:yyyy}-{count:D5}";
    }

    private async Task PopulateLookups()
    {
        ViewBag.Contacts = new SelectList(await _db.Contacts.Where(c => !c.IsDeleted).OrderBy(c => c.LastName).Select(c => new { c.Id, Name = c.FirstName + " " + c.LastName }).ToListAsync(), "Id", "Name");
        ViewBag.Users = new SelectList(await _db.Users.Where(u => u.OrganizationId == _tenant.OrganizationId && u.IsActive).Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName }).ToListAsync(), "Id", "Name");
        ViewBag.Priorities = Enum.GetValues<TicketPriority>().Select(e => new SelectListItem(e.ToString(), e.ToString(), e == TicketPriority.Medium));
    }
}
