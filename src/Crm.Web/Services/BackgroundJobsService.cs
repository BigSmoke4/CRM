using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Controllers;
using Crm.Web.Services.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Services;

/// <summary>
/// Hosted service that runs scheduled integration syncs and workflow triggers.
/// Runs every 60s; tenant context is unauthenticated so query filters see all orgs —
/// each job explicitly sets OrganizationId on created records.
/// </summary>
public class BackgroundJobsService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<BackgroundJobsService> _log;

    public BackgroundJobsService(IServiceProvider sp, ILogger<BackgroundJobsService> log)
    { _sp = sp; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueJobsAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Background job iteration failed"); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task RunDueJobsAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sync = scope.ServiceProvider.GetRequiredService<IntegrationSyncService>();
        var now = DateTimeOffset.UtcNow;

        // ---- Scheduled integration syncs ----
        var integrations = await db.IntegrationConfigs
            .Where(i => i.IsEnabled && i.Direction != SyncDirection.Outbound)
            .ToListAsync(ct);

        foreach (var cfg in integrations)
        {
            if (!IsDue(cfg.SyncSchedule, cfg.LastSyncAt, now)) continue;
            cfg.NextSyncAt = NextRun(cfg.SyncSchedule, now);
            _log.LogInformation("Running scheduled sync for integration {Name} (org {Org})", cfg.Name, cfg.OrganizationId);
            try { await sync.RunSyncAsync(cfg, SyncTrigger.Scheduled, ct); }
            catch (Exception ex) { _log.LogError(ex, "Scheduled sync failed for {Name}", cfg.Name); }
        }
        await db.SaveChangesAsync(ct);

        // ---- Scheduled workflows (TriggerType=Scheduled with cron-ish config) ----
        var workflows = await db.WorkflowDefinitions
            .Where(w => w.IsActive && w.TriggerType == WorkflowTriggerType.Scheduled).ToListAsync(ct);
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        foreach (var w in workflows)
        {
            var lastRun = await db.WorkflowRuns.Where(r => r.WorkflowId == w.Id)
                .OrderByDescending(r => r.StartedAt).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefaultAsync(ct);
            if (!IsDue(w.TriggerConfigJson, lastRun, now)) continue;
            _log.LogInformation("Running scheduled workflow {Name}", w.Name);
            try { await WorkflowEngine.RunAsync(db, tenant, w, new { scheduled = true }); }
            catch (Exception ex) { _log.LogError(ex, "Scheduled workflow {Name} failed", w.Name); }
        }
    }

    /// <summary>Supports: "every:30m", "every:2h", "hourly", "daily", "0 * * * *"-lite cron (minute hour dom mon dow ignored → treated hourly at minute).</summary>
    internal static bool IsDue(string? schedule, DateTimeOffset? lastRun, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(schedule)) return false;
        var s = schedule.Trim().ToLowerInvariant();

        TimeSpan interval = s switch
        {
            "hourly" => TimeSpan.FromHours(1),
            "daily" => TimeSpan.FromDays(1),
            _ when s.StartsWith("every:") => ParseEvery(s[6..]),
            _ => TimeSpan.Zero // cron-ish → approximate as hourly
        };
        if (interval == TimeSpan.Zero) interval = TimeSpan.FromHours(1);

        var last = lastRun ?? DateTimeOffset.MinValue;
        return now - last >= interval;
    }

    internal static DateTimeOffset? NextRun(string? schedule, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(schedule)) return null;
        var s = schedule.Trim().ToLowerInvariant();
        var interval = s switch
        {
            "hourly" => TimeSpan.FromHours(1),
            "daily" => TimeSpan.FromDays(1),
            _ when s.StartsWith("every:") => ParseEvery(s[6..]),
            _ => TimeSpan.FromHours(1)
        };
        return now + interval;
    }

    private static TimeSpan ParseEvery(string spec)
    {
        spec = spec.Trim();
        var unit = spec[^1];
        var num = double.TryParse(spec[..^1], out var n) ? n : 1;
        return unit switch
        {
            'm' => TimeSpan.FromMinutes(num),
            'h' => TimeSpan.FromHours(num),
            'd' => TimeSpan.FromDays(num),
            _ => TimeSpan.FromMinutes(num)
        };
    }
}
