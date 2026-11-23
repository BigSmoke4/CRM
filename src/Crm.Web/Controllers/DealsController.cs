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
public class DealsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public DealsController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db; _tenant = tenant; _audit = audit;
    }

    /// <summary>Kanban board: pipelines -> stages -> deals.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.DealsView)]
    public async Task<IActionResult> Index(Guid? pipelineId = null)
    {
        var pipelines = await _db.Pipelines.Include(p => p.Stages)
            .Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
        if (pipelines.Count == 0) return View(new KanbanVm());

        var selected = pipelineId.HasValue && pipelines.Any(p => p.Id == pipelineId.Value)
            ? pipelineId.Value : pipelines.First(p => p.IsDefault).Id;

        var stages = await _db.DealStages.Where(s => s.PipelineId == selected && s.IsActive)
            .OrderBy(s => s.SortOrder).ToListAsync();
        var deals = await _db.Deals
            .Include(d => d.Account).Include(d => d.Contact).Include(d => d.Owner)
            .Where(d => d.PipelineId == selected && !d.IsDeleted)
            .OrderByDescending(d => d.Amount).ToListAsync();

        var vm = new KanbanVm
        {
            Pipelines = pipelines.Select(p => new SelectListItem(p.Name, p.Id.ToString(), p.Id == selected)).ToList(),
            SelectedPipelineId = selected,
            Stages = stages.Select(s => new KanbanStage
            {
                Id = s.Id, Name = s.Name, Type = s.Type, Probability = s.Probability,
                Deals = deals.Where(d => d.StageId == s.Id).Select(ToCard).ToList()
            }).ToList(),
            Totals = deals.GroupBy(d => d.StageId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount))
        };
        return View(vm);
    }

    /// <summary>AJAX: move a deal to another stage (drag & drop on the kanban).</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.DealsEdit)]
    public async Task<IActionResult> MoveStage(Guid dealId, Guid stageId)
    {
        var deal = await _db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && !d.IsDeleted);
        if (deal == null) return NotFound();
        var stage = await _db.DealStages.FirstOrDefaultAsync(s => s.Id == stageId);
        if (stage == null) return BadRequest("Invalid stage.");
        if (stage.PipelineId != deal.PipelineId) return BadRequest("Stage belongs to a different pipeline.");

        var oldStage = await _db.DealStages.Where(s => s.Id == deal.StageId).Select(s => s.Name).FirstOrDefaultAsync();
        deal.StageId = stageId;
        deal.Probability = stage.Type == DealStageType.Open ? stage.Probability : deal.Probability;
        if (stage.Type == DealStageType.Won || stage.Type == DealStageType.Lost)
        {
            deal.ActualCloseDate = DateTimeOffset.UtcNow;
            if (stage.Type == DealStageType.Lost && string.IsNullOrEmpty(deal.LostReason))
                deal.LostReason = "Not specified";
        }
        deal.UpdatedAt = DateTimeOffset.UtcNow;

        _db.Activities.Add(new Activity
        {
            OrganizationId = deal.OrganizationId, Type = ActivityType.Deal,
            Subject = $"Deal moved: {oldStage} → {stage.Name}", DealId = deal.Id,
            AccountId = deal.AccountId, ContactId = deal.ContactId, OwnerId = _tenant.UserId
        });
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.DealsCreate)]
    public async Task<IActionResult> Create()
    {
        await PopulateLookups();
        return View(new DealFormVm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.DealsCreate)]
    public async Task<IActionResult> Create(DealFormVm vm)
    {
        if (!ModelState.IsValid) { await PopulateLookups(); return View(vm); }
        var stage = await _db.DealStages.FirstOrDefaultAsync(s => s.Id == vm.StageId);
        var deal = new Deal
        {
            OrganizationId = _tenant.OrganizationId!.Value,
            Name = vm.Name.Trim(), Description = vm.Description,
            PipelineId = vm.PipelineId, StageId = vm.StageId,
            Amount = vm.Amount, Currency = vm.Currency,
            Probability = vm.ProbabilityOverride ?? stage?.Probability ?? 10,
            ExpectedCloseDate = vm.ExpectedCloseDate,
            AccountId = vm.AccountId, ContactId = vm.ContactId,
            OwnerId = vm.OwnerId ?? _tenant.UserId,
            Competitor = vm.Competitor, LostReason = vm.LostReason,
            Tags = vm.Tags, Notes = vm.Notes
        };
        _db.Deals.Add(deal);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Deal.Created", "Deal", deal.Id.ToString(), new { deal.Name, deal.Amount });
        TempData["Success"] = "Deal created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.DealsEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var d = await _db.Deals.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (d == null) return NotFound();
        await PopulateLookups(d.PipelineId);
        return View(MapToVm(d));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.DealsEdit)]
    public async Task<IActionResult> Edit(Guid id, DealFormVm vm)
    {
        var d = await _db.Deals.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (d == null) return NotFound();
        if (!ModelState.IsValid) { await PopulateLookups(vm.PipelineId); return View(vm); }

        var stage = await _db.DealStages.FirstOrDefaultAsync(s => s.Id == vm.StageId);
        d.Name = vm.Name.Trim(); d.Description = vm.Description;
        if (d.PipelineId != vm.PipelineId) { d.PipelineId = vm.PipelineId; }
        d.StageId = vm.StageId;
        d.Amount = vm.Amount; d.Currency = vm.Currency;
        d.Probability = vm.ProbabilityOverride ?? stage?.Probability ?? d.Probability;
        d.ExpectedCloseDate = vm.ExpectedCloseDate;
        d.AccountId = vm.AccountId; d.ContactId = vm.ContactId;
        d.OwnerId = vm.OwnerId; d.Competitor = vm.Competitor; d.LostReason = vm.LostReason;
        d.Tags = vm.Tags; d.Notes = vm.Notes;
        d.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Deal.Updated", "Deal", id.ToString());
        TempData["Success"] = "Deal updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.DealsView)]
    public async Task<IActionResult> Details(Guid id)
    {
        var d = await _db.Deals
            .Include(x => x.Account).Include(x => x.Contact).Include(x => x.Owner).Include(x => x.Stage)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (d == null) return NotFound();
        var activities = await _db.Activities.Where(a => a.DealId == id)
            .OrderByDescending(a => a.CreatedAt).Take(50).ToListAsync();
        ViewBag.Activities = activities;
        ViewBag.CanEdit = User.HasClaim("permission", Permissions.DealsEdit);
        return View(d);
    }

    /// <summary>Forecast / analytics view.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.DealsForecast)]
    public async Task<IActionResult> Forecast()
    {
        var open = await _db.Deals.Include(d => d.Stage).Include(d => d.Owner)
            .Where(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Open).ToListAsync();
        var closed = await _db.Deals.Include(d => d.Stage)
            .Where(d => !d.IsDeleted && d.Stage != null && d.Stage.Type != DealStageType.Open).ToListAsync();

        var vm = new ForecastVm
        {
            PipelineValue = open.Sum(d => d.Amount),
            WeightedValue = open.Sum(d => d.Amount * d.Probability / 100),
            OpenCount = open.Count,
            WonCount = closed.Count(d => d.Stage!.Type == DealStageType.Won),
            LostCount = closed.Count(d => d.Stage!.Type == DealStageType.Lost),
            WinRate = closed.Count == 0 ? 0 : Math.Round(closed.Count(d => d.Stage!.Type == DealStageType.Won) * 100.0 / closed.Count, 1),
            ByStage = open.GroupBy(d => d.Stage!.Name).Select(g => new StageForecast
            {
                Stage = g.Key, Count = g.Count(), Value = g.Sum(x => x.Amount),
                Weighted = g.Sum(x => x.Amount * x.Probability / 100)
            }).OrderByDescending(x => x.Value).ToList(),
            ByOwner = open.GroupBy(d => d.OwnerId).Select(g => new OwnerForecast
            {
                Owner = g.Select(x => x.Owner != null ? x.Owner.FirstName + " " + x.Owner.LastName : "Unassigned").FirstOrDefault() ?? "Unassigned",
                Count = g.Count(), Value = g.Sum(x => x.Amount)
            }).OrderByDescending(x => x.Value).ToList(),
            ByMonth = open.Where(d => d.ExpectedCloseDate != null)
                .GroupBy(d => d.ExpectedCloseDate!.Value.ToString("yyyy-MM"))
                .Select(g => new MonthForecast { Month = g.Key, Value = g.Sum(x => x.Amount), Weighted = g.Sum(x => x.Amount * x.Probability / 100) })
                .OrderBy(x => x.Month).ToList()
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.DealsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var d = await _db.Deals.FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound();
        d.IsDeleted = true; d.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Deal.Deleted", "Deal", id.ToString());
        TempData["Success"] = "Deal deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateLookups(Guid? pipelineId = null)
    {
        var pipelines = await _db.Pipelines.Include(p => p.Stages).Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
        ViewBag.Pipelines = new SelectList(pipelines, "Id", "Name", pipelineId ?? pipelines.FirstOrDefault(p => p.IsDefault)?.Id);
        var pid = pipelineId ?? pipelines.FirstOrDefault(p => p.IsDefault)?.Id;
        var stages = pipelines.FirstOrDefault(p => p.Id == pid)?.Stages.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ToList()
            ?? pipelines.SelectMany(p => p.Stages).Where(s => s.IsActive).OrderBy(s => s.SortOrder).ToList();
        ViewBag.Stages = new SelectList(stages, "Id", "Name");
        ViewBag.Accounts = new SelectList(await _db.Accounts.Where(a => !a.IsDeleted).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");
        ViewBag.Contacts = new SelectList(await _db.Contacts.Where(c => !c.IsDeleted).OrderBy(c => c.LastName).Select(c => new { c.Id, Name = c.FirstName + " " + c.LastName }).ToListAsync(), "Id", "Name");
        var users = await _db.Users.Where(u => u.OrganizationId == _tenant.OrganizationId && u.IsActive)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToListAsync();
        ViewBag.Users = new SelectList(users.Select(u => new
        {
            u.Id,
            Name = (u.FirstName + " " + u.LastName).Trim() is { Length: > 0 } n ? n : u.Email ?? u.Id
        }), "Id", "Name");
    }

    private static DealCardVm ToCard(Deal d) => new()
    {
        Id = d.Id, Name = d.Name, Amount = d.Amount, Currency = d.Currency,
        Company = d.Account?.Name ?? d.Contact?.FirstName + " " + d.Contact?.LastName,
        Owner = d.Owner != null ? d.Owner.FirstName + " " + d.Owner.LastName : null,
        ExpectedClose = d.ExpectedCloseDate
    };

    private static DealFormVm MapToVm(Deal d) => new()
    {
        Id = d.Id, Name = d.Name, Description = d.Description, PipelineId = d.PipelineId,
        StageId = d.StageId, Amount = d.Amount, Currency = d.Currency,
        ProbabilityOverride = d.Probability == d.Stage?.Probability ? null : d.Probability,
        ExpectedCloseDate = d.ExpectedCloseDate, AccountId = d.AccountId, ContactId = d.ContactId,
        OwnerId = d.OwnerId, Competitor = d.Competitor, LostReason = d.LostReason,
        Tags = d.Tags, Notes = d.Notes
    };

    public class KanbanVm
    {
        public List<SelectListItem> Pipelines { get; set; } = new();
        public Guid SelectedPipelineId { get; set; }
        public List<KanbanStage> Stages { get; set; } = new();
        public Dictionary<Guid, decimal> Totals { get; set; } = new();
    }
    public class KanbanStage { public Guid Id { get; set; } public string Name { get; set; } = ""; public DealStageType Type { get; set; } public int Probability { get; set; } public List<DealCardVm> Deals { get; set; } = new(); }
    public class DealCardVm { public Guid Id { get; set; } public string Name { get; set; } = ""; public decimal Amount { get; set; } public string Currency { get; set; } = ""; public string? Company { get; set; } public string? Owner { get; set; } public DateTimeOffset? ExpectedClose { get; set; } }

    public class ForecastVm
    {
        public decimal PipelineValue { get; set; } public decimal WeightedValue { get; set; }
        public int OpenCount { get; set; } public int WonCount { get; set; } public int LostCount { get; set; }
        public double WinRate { get; set; }
        public List<StageForecast> ByStage { get; set; } = new();
        public List<OwnerForecast> ByOwner { get; set; } = new();
        public List<MonthForecast> ByMonth { get; set; } = new();
    }
    public class StageForecast { public string Stage { get; set; } = ""; public int Count { get; set; } public decimal Value { get; set; } public decimal Weighted { get; set; } }
    public class OwnerForecast { public string Owner { get; set; } = ""; public int Count { get; set; } public decimal Value { get; set; } }
    public class MonthForecast { public string Month { get; set; } = ""; public decimal Value { get; set; } public decimal Weighted { get; set; } }
}
