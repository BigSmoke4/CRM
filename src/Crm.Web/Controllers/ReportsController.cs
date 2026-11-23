using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>Reports: tabular exports (CSV) and on-screen summaries.</summary>
[Authorize]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _db;

    public ReportsController(ApplicationDbContext db) => _db = db;

    [HttpGet, Authorize(Policy = Permissions.ReportsView)]
    public async Task<IActionResult> Index()
    {
        var now = DateTimeOffset.UtcNow;
        ViewBag.SalesReport = new
        {
            DealsWon = await _db.Deals.CountAsync(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Won),
            RevenueWon = await _db.Deals.Where(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Won).SumAsync(d => (decimal?)d.Amount) ?? 0,
            DealsLost = await _db.Deals.CountAsync(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Lost),
            PipelineValue = await _db.Deals.Where(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Open).SumAsync(d => (decimal?)d.Amount) ?? 0,
            AvgDealSize = await _db.Deals.Where(d => !d.IsDeleted && d.Stage != null && d.Stage.Type == DealStageType.Won).AverageAsync(d => (decimal?)d.Amount) ?? 0
        };
        ViewBag.SupportReport = new
        {
            OpenTickets = await _db.Tickets.CountAsync(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
            ResolvedThisMonth = await _db.Tickets.CountAsync(t => t.ResolvedAt >= now.AddDays(-30)),
            AvgSatisfaction = await _db.Tickets.Where(t => t.SatisfactionScore > 0).AverageAsync(t => (double?)t.SatisfactionScore) ?? 0
        };
        ViewBag.MarketingReport = new
        {
            Contacts = await _db.Contacts.CountAsync(c => !c.IsDeleted),
            Leads = await _db.Leads.CountAsync(l => !l.IsDeleted && l.ConvertedContactId == null),
            ConversionRate = 0.0
        };
        var leads = await _db.Leads.CountAsync(l => !l.IsDeleted);
        var converted = await _db.Leads.CountAsync(l => !l.IsDeleted && l.ConvertedContactId != null);
        ViewBag.MarketingReport = new { ViewBag.MarketingReport.Contacts, ViewBag.MarketingReport.Leads,
            ConversionRate = leads == 0 ? 0 : Math.Round(converted * 100.0 / leads, 1) };
        return View();
    }

    [HttpGet, Authorize(Policy = Permissions.ReportsExport)]
    public async Task<IActionResult> Export(string entity)
    {
        var sb = new System.Text.StringBuilder();
        switch (entity?.ToLowerInvariant())
        {
            case "contacts":
                sb.AppendLine("FirstName,LastName,Email,Phone,JobTitle,Source,CustomerType,CreatedAt");
                foreach (var c in await _db.Contacts.Where(x => !x.IsDeleted).Take(10000).ToListAsync())
                    sb.AppendLine($"{c.FirstName},{c.LastName},{c.Email},{c.Phone},{c.JobTitle},{c.Source},{c.CustomerType},{c.CreatedAt:yyyy-MM-dd}");
                break;
            case "deals":
                sb.AppendLine("Name,Amount,Currency,Stage,Probability,ExpectedClose,CreatedAt");
                foreach (var d in await _db.Deals.Include(x => x.Stage).Where(x => !x.IsDeleted).Take(10000).ToListAsync())
                    sb.AppendLine($"\"{d.Name}\",{d.Amount},{d.Currency},{d.Stage?.Name},{d.Probability},{d.ExpectedCloseDate:yyyy-MM-dd},{d.CreatedAt:yyyy-MM-dd}");
                break;
            case "tickets":
                sb.AppendLine("TicketNumber,Subject,Status,Priority,CreatedAt,ResolvedAt");
                foreach (var t in await _db.Tickets.Take(10000).ToListAsync())
                    sb.AppendLine($"{t.TicketNumber},\"{t.Subject}\",{t.Status},{t.Priority},{t.CreatedAt:yyyy-MM-dd},{t.ResolvedAt:yyyy-MM-dd}");
                break;
            case "activities":
                sb.AppendLine("Type,Subject,DueAt,Completed,CreatedAt");
                foreach (var a in await _db.Activities.OrderByDescending(x => x.CreatedAt).Take(10000).ToListAsync())
                    sb.AppendLine($"{a.Type},\"{a.Subject}\",{a.DueAt:yyyy-MM-dd},{a.IsCompleted},{a.CreatedAt:yyyy-MM-dd}");
                break;
            default:
                return BadRequest("Unknown entity.");
        }
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"{entity}-report.csv");
    }
}
