using System.ComponentModel.DataAnnotations;
using Crm.Data;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
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
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly WorkspaceProvisioner _provisioner;
    private readonly ILogger<SetupController> _logger;

    public SetupController(
        ApplicationDbContext db,
        SignInManager<ApplicationUser> signInManager,
        WorkspaceProvisioner provisioner,
        ILogger<SetupController> logger)
    {
        _db = db;
        _signInManager = signInManager;
        _provisioner = provisioner;
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

        var user = await _provisioner.CreateAsync(vm.OrganizationName, vm.AdminFirstName,
            vm.AdminLastName, vm.AdminEmail, vm.AdminPassword);

        await _signInManager.SignInAsync(user, isPersistent: false);
        _logger.LogInformation("Setup completed: org {Org} created with admin {Email}", user.Organization!.Name, user.Email);
        return RedirectToAction("Index", "Home");
    }
}
