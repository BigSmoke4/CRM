using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Crm.Data.Entities;
using Crm.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Crm.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(vm);
        }
        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated. Contact your administrator.");
            return View(vm);
        }

        var result = await _signInManager.PasswordSignInAsync(user, vm.Password, vm.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
            return LocalRedirect(Url.IsLocalUrl(vm.ReturnUrl) ? vm.ReturnUrl! : "/");
        }
        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account locked due to too many failed attempts. Try again later.");
            return View(vm);
        }
        if (result.RequiresTwoFactor)
            return RedirectToAction("LoginWith2fa", new { ReturnUrl = vm.ReturnUrl, RememberMe = vm.RememberMe });

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return View(vm);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult LoginWith2fa(bool rememberMe, string? returnUrl = null)
        => View(new { ReturnUrl = returnUrl, RememberMe = rememberMe });

    [HttpPost, ValidateAntiForgeryToken]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> LoginWith2fa(string twoFactorCode, bool rememberMe, string? returnUrl = null)
    {
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null) return RedirectToAction("Login");

        var code = twoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, rememberMe, rememberClient: false);
        if (result.Succeeded)
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");

        ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
        return View(new { ReturnUrl = returnUrl, RememberMe = rememberMe });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        return View(user);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromForm] string currentPassword, [FromForm] string newPassword)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View("Profile", user);
        }
        await _signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Password changed.";
        return RedirectToAction("Profile");
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Enable2fa()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _userManager.SetTwoFactorEnabledAsync(user, true);
        TempData["Success"] = "Two-factor authentication enabled.";
        return RedirectToAction("Profile");
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Disable2fa()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await _userManager.SetTwoFactorEnabledAsync(user, false);
        TempData["Success"] = "Two-factor authentication disabled.";
        return RedirectToAction("Profile");
    }
}
