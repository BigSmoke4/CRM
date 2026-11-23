using Crm.Data;
using Crm.Data.Entities;
using Crm.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Controllers;

[Authorize]
public class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public UsersController(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles,
        ITenantContext tenant, IAuditService audit)
    { _users = users; _roles = roles; _tenant = tenant; _audit = audit; }

    [HttpGet, Authorize(Policy = Permissions.UsersView)]
    public async Task<IActionResult> Index()
    {
        var users = _users.Users.Where(u => u.OrganizationId == _tenant.OrganizationId).ToList();
        var roles = await _roles.Roles.Select(r => r.Name!).ToListAsync();
        var vm = new List<UserRow>();
        foreach (var u in users)
            vm.Add(new UserRow
            {
                Id = u.Id, Name = $"{u.FirstName} {u.LastName}".Trim(), Email = u.Email!,
                IsActive = u.IsActive, Roles = string.Join(", ", await _users.GetRolesAsync(u)),
                LastLogin = u.LastLoginAt
            });
        ViewBag.Roles = roles;
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.UsersManage)]
    public async Task<IActionResult> Invite(string email, string firstName, string lastName, string role, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        { TempData["Error"] = "Email and password required."; return RedirectToAction(nameof(Index)); }

        var user = new ApplicationUser
        {
            UserName = email, Email = email, FirstName = firstName, LastName = lastName,
            OrganizationId = _tenant.OrganizationId, EmailConfirmed = true, IsActive = true
        };
        var result = await _users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }
        if (!string.IsNullOrEmpty(role) && await _roles.RoleExistsAsync(role))
            await _users.AddToRoleAsync(user, role);
        await _audit.LogAsync("User.Invited", "User", user.Id, new { user.Email, role });
        TempData["Success"] = $"User {email} created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.UsersManage)]
    public async Task<IActionResult> SetRole(string userId, string role)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null || user.OrganizationId != _tenant.OrganizationId) return NotFound();
        var current = await _users.GetRolesAsync(user);
        await _users.RemoveFromRolesAsync(user, current);
        if (!string.IsNullOrEmpty(role)) await _users.AddToRoleAsync(user, role);
        await _audit.LogAsync("User.RoleChanged", "User", userId, new { role });
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Permissions.UsersManage)]
    public async Task<IActionResult> ToggleActive(string userId)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null || user.OrganizationId != _tenant.OrganizationId) return NotFound();
        user.IsActive = !user.IsActive;
        await _users.UpdateAsync(user);
        await _audit.LogAsync("User.Toggled", "User", userId, new { user.IsActive });
        return RedirectToAction(nameof(Index));
    }

    public class UserRow
    {
        public string Id { get; set; } = ""; public string Name { get; set; } = "";
        public string Email { get; set; } = ""; public bool IsActive { get; set; }
        public string Roles { get; set; } = ""; public DateTimeOffset? LastLogin { get; set; }
    }
}
