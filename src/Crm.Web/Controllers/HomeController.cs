using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<HomeController> _logger;

    public HomeController(ApplicationDbContext db, ILogger<HomeController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = new DashboardVm
        {
            TotalContacts = await _db.Contacts.CountAsync(c => !c.IsDeleted),
            NewContactsThisMonth = await _db.Contacts.CountAsync(c => !c.IsDeleted && c.CreatedAt >= now.AddDays(-30)),
            TotalAccounts = await _db.Accounts.CountAsync(a => !a.IsDeleted),
            OpenLeads = await _db.Leads.CountAsync(l => !l.IsDeleted && (l.Status == LeadStatus.New || l.Status == LeadStatus.Contacted)),
            QualifiedLeads = await _db.Leads.CountAsync(l => !l.IsDeleted && l.Status == LeadStatus.Qualified),
            OpenDeals = await _db.Deals.CountAsync(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Open),
            PipelineValue = await _db.Deals.Where(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Open).SumAsync(d => (decimal?)d.Amount) ?? 0,
            WeightedForecast = await _db.Deals.Where(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Open)
                .SumAsync(d => (decimal?)(d.Amount * d.Probability / 100)) ?? 0,
            WonDealsThisMonth = await _db.Deals.CountAsync(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Won && d.ActualCloseDate >= now.AddDays(-30)),
            OpenTickets = await _db.Tickets.CountAsync(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
            OverdueTasks = await _db.Activities.CountAsync(a => a.Type == ActivityType.Task && !a.IsCompleted && a.DueAt < now),
            DueTasksToday = await _db.Activities.CountAsync(a => a.Type == ActivityType.Task && !a.IsCompleted && a.DueAt >= now && a.DueAt < now.AddDays(1)),
            RecentActivities = await _db.Activities
                .OrderByDescending(a => a.CreatedAt).Take(12).ToListAsync(),
            LeadsByStatus = await _db.Leads.Where(l => !l.IsDeleted)
                .GroupBy(l => l.Status).Select(g => new StatusCount { Name = g.Key.ToString(), Count = g.Count() }).ToListAsync(),
            DealsByStage = await _db.Deals.Where(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Open)
                .GroupBy(d => d.Stage!.Name).Select(g => new StageValue { Name = g.Key, Value = g.Sum(x => x.Amount), Count = g.Count() }).ToListAsync(),
            ContactsBySource = await _db.Contacts.Where(c => !c.IsDeleted && c.Source != null)
                .GroupBy(c => c.Source!).Select(g => new StatusCount { Name = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(8).ToListAsync()
        };
        return View(vm);
    }

    [AllowAnonymous]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Privacy() => View();
}

public class DashboardVm
{
    public int TotalContacts { get; set; }
    public int NewContactsThisMonth { get; set; }
    public int TotalAccounts { get; set; }
    public int OpenLeads { get; set; }
    public int QualifiedLeads { get; set; }
    public int OpenDeals { get; set; }
    public decimal PipelineValue { get; set; }
    public decimal WeightedForecast { get; set; }
    public int WonDealsThisMonth { get; set; }
    public int OpenTickets { get; set; }
    public int OverdueTasks { get; set; }
    public int DueTasksToday { get; set; }
    public List<Activity> RecentActivities { get; set; } = new();
    public List<StatusCount> LeadsByStatus { get; set; } = new();
    public List<StageValue> DealsByStage { get; set; } = new();
    public List<StatusCount> ContactsBySource { get; set; } = new();
}

public class StatusCount { public string Name { get; set; } = ""; public int Count { get; set; } }
public class StageValue { public string Name { get; set; } = ""; public decimal Value { get; set; } public int Count { get; set; } }
