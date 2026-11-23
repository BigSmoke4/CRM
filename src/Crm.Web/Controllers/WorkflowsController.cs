using System.Text.Json;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>Visual workflow automation: triggers -> conditions -> actions, executed by a background runner.</summary>
[Authorize]
public class WorkflowsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public WorkflowsController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet, Authorize(Policy = Permissions.WorkflowsView)]
    public async Task<IActionResult> Index()
        => View(await _db.WorkflowDefinitions.OrderByDescending(w => w.UpdatedAt).ToListAsync());

    [HttpGet, Authorize(Policy = Permissions.WorkflowsCreate)]
    public IActionResult Create() => View(new WorkflowDefinition { OrganizationId = _tenant.OrganizationId ?? Guid.Empty });

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.WorkflowsCreate)]
    public async Task<IActionResult> Create(WorkflowDefinition w)
    {
        w.Id = Guid.NewGuid(); w.OrganizationId = _tenant.OrganizationId!.Value;
        w.CreatedAt = DateTimeOffset.UtcNow; w.UpdatedAt = DateTimeOffset.UtcNow;
        _db.WorkflowDefinitions.Add(w); await _db.SaveChangesAsync();
        await _audit.LogAsync("Workflow.Created", "Workflow", w.Id.ToString());
        TempData["Success"] = "Workflow created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, Authorize(Policy = Permissions.WorkflowsEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var w = await _db.WorkflowDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (w == null) return NotFound();
        ViewBag.Runs = await _db.WorkflowRuns.Where(r => r.WorkflowId == id)
            .OrderByDescending(r => r.StartedAt).Take(50).ToListAsync();
        return View(w);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.WorkflowsEdit)]
    public async Task<IActionResult> Edit(Guid id, WorkflowDefinition w)
    {
        var e = await _db.WorkflowDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        e.Name = w.Name; e.Description = w.Description; e.IsActive = w.IsActive;
        e.TriggerType = w.TriggerType; e.TriggerEntity = w.TriggerEntity;
        e.TriggerConfigJson = w.TriggerConfigJson; e.DefinitionJson = w.DefinitionJson;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Workflow saved.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Manual test run of a workflow with sample payload.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.WorkflowsRun)]
    public async Task<IActionResult> Run(Guid id)
    {
        var w = await _db.WorkflowDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (w == null) return NotFound();
        var run = await WorkflowEngine.RunAsync(_db, _tenant, w, new { manual = true, userId = _tenant.UserId });
        TempData[run.Status == WorkflowRunStatus.Failed ? "Error" : "Success"] =
            $"Run {run.Status}: {run.CurrentStep}";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.WorkflowsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var w = await _db.WorkflowDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (w == null) return NotFound();
        _db.WorkflowDefinitions.Remove(w); await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Executes workflow definitions stored as JSON step graphs.</summary>
public static class WorkflowEngine
{
    public static async Task<WorkflowRun> RunAsync(ApplicationDbContext db, ITenantContext tenant,
        WorkflowDefinition w, object? payload)
    {
        var run = new WorkflowRun
        {
            OrganizationId = w.OrganizationId, WorkflowId = w.Id,
            Status = WorkflowRunStatus.Running, TriggerPayloadJson = JsonSerializer.Serialize(payload),
            StartedAt = DateTimeOffset.UtcNow
        };
        db.WorkflowRuns.Add(run);
        await db.SaveChangesAsync();

        try
        {
            var def = JsonDocument.Parse(w.DefinitionJson).RootElement;
            var steps = def.TryGetProperty("steps", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().ToList() : new List<JsonElement>();
            var log = new List<string>();

            foreach (var step in steps)
            {
                var type = step.TryGetProperty("type", out var t) ? t.GetString() : null;
                var action = step.TryGetProperty("action", out var a) ? a.GetString() : null;
                run.CurrentStep = $"{type}:{action}";
                log.Add($"step {type}:{action}");

                switch (action)
                {
                    case "create_task":
                        var subject = GetStr(step, "subject") ?? "Workflow task";
                        db.Activities.Add(new Activity
                        {
                            OrganizationId = w.OrganizationId, Type = ActivityType.Task,
                            Subject = subject, OwnerId = GetStr(step, "assigneeId") ?? tenant.UserId,
                            DueAt = DateTimeOffset.UtcNow.AddDays(GetNum(step, "dueDays", 1))
                        });
                        break;
                    case "send_notification":
                        db.Notifications.Add(new Notification
                        {
                            OrganizationId = w.OrganizationId, Title = GetStr(step, "title") ?? "Workflow notification",
                            Body = GetStr(step, "body") ?? "", Type = "info"
                        });
                        break;
                    case "update_lead_status":
                        var statusStr = GetStr(step, "status");
                        if (Enum.TryParse<LeadStatus>(statusStr, out var ls) && payload is JsonElement pe)
                        {
                            var leadIdStr = pe.TryGetProperty("leadId", out var lid) ? lid.GetString() : null;
                            if (Guid.TryParse(leadIdStr, out var lid2))
                            {
                                var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == lid2);
                                if (lead != null) { lead.Status = ls; lead.UpdatedAt = DateTimeOffset.UtcNow; }
                            }
                        }
                        break;
                    case "send_webhook":
                        var url = GetStr(step, "url");
                        if (!string.IsNullOrEmpty(url))
                        {
                            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                            var body = JsonSerializer.Serialize(new { workflow = w.Name, payload });
                            await client.PostAsync(url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
                            log.Add("webhook sent");
                        }
                        break;
                    case "delay":
                        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(GetNum(step, "seconds", 0), 0, 300)));
                        break;
                }
            }
            run.Status = WorkflowRunStatus.Completed;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.LogJson = JsonSerializer.Serialize(log);
        }
        catch (Exception ex)
        {
            run.Status = WorkflowRunStatus.Failed;
            run.Error = ex.Message;
            run.FinishedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync();
        return run;
    }

    private static string? GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) ? p.GetString() : null;
    private static double GetNum(JsonElement el, string name, double def) =>
        el.TryGetProperty(name, out var p) && p.TryGetDouble(out var d) ? d : def;
}
