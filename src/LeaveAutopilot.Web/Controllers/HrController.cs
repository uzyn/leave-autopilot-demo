using LeaveAutopilot.Web.Models;
using LeaveAutopilot.Web.Models.Entities;
using LeaveAutopilot.Web.Models.Hr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LeaveAutopilot.Web.Controllers;

/// <summary>
/// HR-only administration actions. Starts with HR-assisted password reset (S2-3); Sprint 3
/// grows this into full employee/manager/quota administration.
/// </summary>
[Authorize(Roles = Roles.Hr)]
public class HrController(UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet]
    public async Task<IActionResult> ResetPassword()
    {
        return View(await BuildViewModelAsync(new ResetPasswordViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildViewModelAsync(model));
        }

        // ModelState.IsValid (checked above) guarantees UserId is non-null here: [Required]
        // on the nullable Guid? rejects a missing/empty selection before we reach this line.
        var userId = model.UserId!.Value;
        var user = await userManager.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Select an active user.");
            return View(await BuildViewModelAsync(model));
        }

        // Use Identity's reset-token flow (rather than Remove+AddPassword) so the security
        // stamp is regenerated: any existing session for this user is invalidated, and the
        // old password immediately stops working.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, model.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(await BuildViewModelAsync(model));
        }

        TempData["ResetPasswordSuccess"] = $"Password reset for {user.Email}.";
        return RedirectToAction(nameof(ResetPassword));
    }

    private async Task<ResetPasswordViewModel> BuildViewModelAsync(ResetPasswordViewModel model)
    {
        model.ActiveUsers = await userManager.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem($"{u.FullName} ({u.Email})", u.Id.ToString()))
            .ToListAsync();

        return model;
    }
}
