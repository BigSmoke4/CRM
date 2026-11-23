using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>Organization settings: profile, SMTP email, security, custom fields.</summary>
[Authorize]
public class SettingsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly Crm.Web.Services.DemoDataSeeder _seeder;

    public SettingsController(ApplicationDbContext db, ITenantContext tenant, IAuditService audit, Crm.Web.Services.DemoDataSeeder seeder)
    { _db = db; _tenant = tenant; _audit = audit; _seeder = seeder; }

    [HttpGet, Authorize(Policy = Permissions.SettingsView)]
    public async Task<IActionResult> Index()
    {
        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == _tenant.OrganizationId);
        if (org == null) return NotFound();
        ViewBag.CustomFields = await _db.CustomFieldDefinitions
            .Where(f => f.IsActive).OrderBy(f => f.EntityType).ThenBy(f => f.SortOrder).ToListAsync();
        ViewBag.AuditLogs = await _db.AuditLogs.OrderByDescending(a => a.CreatedAt).Take(50).ToListAsync();
        ViewBag.Pipelines = await _db.Pipelines.Include(p => p.Stages).ToListAsync();
        return View(org);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.SettingsManage)]
    public async Task<IActionResult> SaveOrganization(Organization o)
    {
        var e = await _db.Organizations.FirstOrDefaultAsync(x => x.Id == o.Id);
        if (e == null) return NotFound();
        e.Name = o.Name; e.TimeZone = o.TimeZone; e.Currency = o.Currency; e.PrimaryColor = o.PrimaryColor;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Settings.Organization", "Organization", e.Id.ToString());
        TempData["Success"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.SettingsManage)]
    public async Task<IActionResult> CustomFieldSave(CustomFieldDefinition f)
    {
        if (f.Id == Guid.Empty)
        {
            f.Id = Guid.NewGuid(); f.OrganizationId = _tenant.OrganizationId!.Value;
            _db.CustomFieldDefinitions.Add(f);
        }
        else
        {
            var e = await _db.CustomFieldDefinitions.FirstOrDefaultAsync(x => x.Id == f.Id);
            if (e == null) return NotFound();
            e.Name = f.Name; e.Label = f.Label; e.FieldType = f.FieldType;
            e.OptionsJson = f.OptionsJson; e.IsRequired = f.IsRequired; e.IsActive = f.IsActive; e.SortOrder = f.SortOrder;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Custom field saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.SettingsManage)]
    public async Task<IActionResult> CustomFieldDelete(Guid id)
    {
        var f = await _db.CustomFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (f != null) { _db.CustomFieldDefinitions.Remove(f); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.SettingsManage)]
    public async Task<IActionResult> SeedDemo()
    {
        var seeded = await _seeder.SeedAsync(_tenant.OrganizationId!.Value, _tenant.UserId);
        TempData[seeded > 0 ? "Success" : "Error"] = seeded > 0
            ? "Demo data loaded across all modules."
            : "Demo data already exists for this organization.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.SettingsManage)]
    public async Task<IActionResult> StageAdd(Guid pipelineId, string name, int probability)
    {
        var p = await _db.Pipelines.Include(x => x.Stages).FirstOrDefaultAsync(x => x.Id == pipelineId);
        if (p != null)
        {
            p.Stages.Add(new DealStage
            {
                OrganizationId = p.OrganizationId, PipelineId = p.Id,
                Name = name, Probability = probability,
                SortOrder = p.Stages.Count + 1
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
