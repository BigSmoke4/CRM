using System.ComponentModel.DataAnnotations;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

/// <summary>First-run wizard: creates the organization and its admin user. Disabled once any org exists.</summary>
[AllowAnonymous]
public class SetupController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<SetupController> _logger;

    public SetupController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<SetupController> logger)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (await _db.Organizations.AnyAsync())
            return RedirectToAction("Login", "Account");
        return View(new SetupVm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Index(SetupVm vm)
    {
        if (await _db.Organizations.AnyAsync())
            return RedirectToAction("Login", "Account");

        if (!ModelState.IsValid) return View(vm);

        var strategy = _db.Database.CreateExecutionStrategy();
        var user = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var created = await CreateWorkspaceAsync(vm);
            await tx.CommitAsync();
            return created;
        });

        await _signInManager.SignInAsync(user, isPersistent: false);
        _logger.LogInformation("Setup completed: org {Org} created with admin {Email}", user.Organization!.Name, user.Email);
        return RedirectToAction("Index", "Home");
    }

    private async Task<ApplicationUser> CreateWorkspaceAsync(SetupVm vm)
    {
        var org = new Organization
        {
            Name = vm.OrganizationName.Trim(),
            Slug = Slugify(vm.OrganizationName),
            Currency = "USD",
            TimeZone = "UTC"
        };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        var user = new ApplicationUser
        {
            UserName = vm.AdminEmail.Trim(),
            Email = vm.AdminEmail.Trim(),
            FirstName = vm.AdminFirstName.Trim(),
            LastName = vm.AdminLastName.Trim(),
            OrganizationId = org.Id,
            EmailConfirmed = true,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, vm.AdminPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        var adminRole = await _roleManager.FindByNameAsync(RolePermissions.OrgAdmin);
        if (adminRole != null)
            await _userManager.AddToRoleAsync(user, adminRole.Name!);

        var pipeline = new Pipeline { OrganizationId = org.Id, Name = "Sales Pipeline", IsDefault = true };
        pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Qualification", Probability = 10, SortOrder = 1 });
        pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Discovery", Probability = 25, SortOrder = 2 });
        pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Proposal", Probability = 50, SortOrder = 3 });
        pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Negotiation", Probability = 75, SortOrder = 4 });
        pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Closed Won", Type = DealStageType.Won, Probability = 100, SortOrder = 5 });
        pipeline.Stages.Add(new DealStage { OrganizationId = org.Id, Name = "Closed Lost", Type = DealStageType.Lost, Probability = 0, SortOrder = 6 });
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync();

        user.Organization = org;
        return user;
    }

    private static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
