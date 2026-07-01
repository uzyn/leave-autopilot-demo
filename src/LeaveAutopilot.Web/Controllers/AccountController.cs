using LeaveAutopilot.Web.Models.Account;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LeaveAutopilot.Web.Controllers;

/// <summary>
/// Email/password sign-in and sign-out (S2-1). Every action here is intentionally
/// anonymous-accessible — this is the on-ramp/off-ramp for authentication itself, so it
/// can't require the caller to already be signed in.
/// </summary>
[AllowAnonymous]
public class AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
    : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        // An already-authenticated user hitting the login form is confusing more than
        // useful — send them on to the home page instead of showing the form again.
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocalOrHome(returnUrl);
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Deactivated users are rejected before a password check even runs, so a disabled
        // account can never authenticate — regardless of whether the password is correct.
        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is not null && user.IsActive)
        {
            var result = await signInManager.PasswordSignInAsync(user, model.Password, isPersistent: false, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                return RedirectToLocalOrHome(returnUrl);
            }
        }

        // Deliberately generic message: don't reveal whether the email exists, whether the
        // password was wrong, or whether the account is deactivated.
        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private IActionResult RedirectToLocalOrHome(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }
}
