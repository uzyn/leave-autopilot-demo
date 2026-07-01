using LeaveAutopilot.Web.Data;
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
/// HR-only administration: password reset (S2-3), and — added in Sprint 3 — employee
/// account management (S3-1), manager assignment (S3-2), and annual quota management
/// (S3-3). Every action is HR-only, enforced server-side via the class-level [Authorize].
/// </summary>
[Authorize(Roles = Roles.Hr)]
public class HrController(UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> ResetPassword()
    {
        return View(await BuildResetPasswordViewModelAsync(new ResetPasswordViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildResetPasswordViewModelAsync(model));
        }

        // ModelState.IsValid (checked above) guarantees UserId is non-null here: [Required]
        // on the nullable Guid? rejects a missing/empty selection before we reach this line.
        var userId = model.UserId!.Value;
        var user = await userManager.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Select an active user.");
            return View(await BuildResetPasswordViewModelAsync(model));
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

            return View(await BuildResetPasswordViewModelAsync(model));
        }

        TempData["ResetPasswordSuccess"] = $"Password reset for {user.Email}.";
        return RedirectToAction(nameof(ResetPassword));
    }

    // --- S3-1 / S3-2: employee account management & manager assignment ---

    [HttpGet]
    public async Task<IActionResult> Employees()
    {
        var users = await userManager.Users
            .OrderBy(u => u.FullName)
            .ToListAsync();

        var managerNamesById = users.ToDictionary(u => u.Id, u => u.FullName);

        var items = new List<EmployeeListItemViewModel>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            items.Add(new EmployeeListItemViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                Role = roles.FirstOrDefault() ?? string.Empty,
                ManagerName = user.ManagerId.HasValue && managerNamesById.TryGetValue(user.ManagerId.Value, out var name)
                    ? name
                    : null,
                IsActive = user.IsActive,
            });
        }

        return View(items);
    }

    [HttpGet]
    public IActionResult CreateEmployee()
    {
        return View(BuildCreateEmployeeViewModel(new CreateEmployeeViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEmployee(CreateEmployeeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(BuildCreateEmployeeViewModel(model));
        }

        // Never trust the client-side <select> to constrain Role: validate against the known
        // role set before any Identity mutation runs, so a forged/unexpected value is rejected
        // up front instead of creating a user we then can't cleanly assign a role to.
        if (!Roles.All.Contains(model.Role))
        {
            ModelState.AddModelError(nameof(model.Role), "Select a valid role.");
            return View(BuildCreateEmployeeViewModel(model));
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            FullName = model.FullName,
            IsActive = true,
        };

        var createResult = await userManager.CreateAsync(user, model.InitialPassword);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(BuildCreateEmployeeViewModel(model));
        }

        var roleResult = await userManager.AddToRoleAsync(user, model.Role);
        if (!roleResult.Succeeded)
        {
            // Compensate: the role is now known-valid (checked above), so this can only fail
            // for an unexpected Identity-level reason. Roll back the just-created user rather
            // than leaving a roleless account occupying the email with no recovery path.
            await userManager.DeleteAsync(user);

            foreach (var error in roleResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(BuildCreateEmployeeViewModel(model));
        }

        TempData["EmployeesSuccess"] = $"Created employee {user.FullName} ({user.Email}).";
        return RedirectToAction(nameof(Employees));
    }

    [HttpGet]
    public async Task<IActionResult> EditEmployee(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        var model = new EditEmployeeViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Role = roles.FirstOrDefault() ?? string.Empty,
            ManagerId = user.ManagerId,
            IsActive = user.IsActive,
        };

        return View(await BuildEditEmployeeViewModelAsync(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEmployee(Guid id, EditEmployeeViewModel model)
    {
        model.Id = id;

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        model.IsActive = user.IsActive;

        // S3-2: an employee cannot be assigned as their own manager.
        if (model.ManagerId == id)
        {
            ModelState.AddModelError(nameof(model.ManagerId), "An employee cannot be their own manager.");
        }
        else if (model.ManagerId.HasValue)
        {
            var manager = await userManager.FindByIdAsync(model.ManagerId.Value.ToString());
            if (manager is null || !manager.IsActive)
            {
                ModelState.AddModelError(nameof(model.ManagerId), "Select an active manager.");
            }
        }

        // Never trust the client-side <select> to constrain Role: validate against the known
        // role set before any Identity role mutation runs, so a forged/unexpected value is
        // rejected up front rather than risking an existing employee being stripped of their
        // role (see the add-before-remove ordering below for the remaining failure mode).
        if (!Roles.All.Contains(model.Role))
        {
            ModelState.AddModelError(nameof(model.Role), "Select a valid role.");
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildEditEmployeeViewModelAsync(model));
        }

        var existingByEmail = await userManager.FindByEmailAsync(model.Email);
        if (existingByEmail is not null && existingByEmail.Id != user.Id)
        {
            ModelState.AddModelError(nameof(model.Email), "This email is already in use.");
            return View(await BuildEditEmployeeViewModelAsync(model));
        }

        user.FullName = model.FullName;
        user.ManagerId = model.ManagerId;

        if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailResult = await userManager.SetEmailAsync(user, model.Email);
            if (!emailResult.Succeeded)
            {
                foreach (var error in emailResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(await BuildEditEmployeeViewModelAsync(model));
            }

            var userNameResult = await userManager.SetUserNameAsync(user, model.Email);
            if (!userNameResult.Succeeded)
            {
                foreach (var error in userNameResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(await BuildEditEmployeeViewModelAsync(model));
            }
        }

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(await BuildEditEmployeeViewModelAsync(model));
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (!currentRoles.Contains(model.Role))
        {
            // Add the new (already-validated) role before removing the old one(s): if the add
            // fails for some unexpected Identity-level reason, the employee keeps their
            // previous role instead of being left with none.
            var addRoleResult = await userManager.AddToRoleAsync(user, model.Role);
            if (!addRoleResult.Succeeded)
            {
                foreach (var error in addRoleResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(await BuildEditEmployeeViewModelAsync(model));
            }

            if (currentRoles.Count > 0)
            {
                await userManager.RemoveFromRolesAsync(user, currentRoles);
            }
        }

        TempData["EmployeesSuccess"] = $"Updated employee {user.FullName} ({user.Email}).";
        return RedirectToAction(nameof(Employees));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateEmployee(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = false;
        await userManager.UpdateAsync(user);

        // Invalidate any existing session immediately rather than waiting for Identity's
        // periodic security-stamp check to notice the account was deactivated.
        await userManager.UpdateSecurityStampAsync(user);

        TempData["EmployeesSuccess"] = $"Deactivated {user.FullName} ({user.Email}).";
        return RedirectToAction(nameof(Employees));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateEmployee(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = true;
        await userManager.UpdateAsync(user);

        TempData["EmployeesSuccess"] = $"Reactivated {user.FullName} ({user.Email}).";
        return RedirectToAction(nameof(Employees));
    }

    // --- S3-3: annual quota management ---

    [HttpGet]
    public async Task<IActionResult> EditQuotas(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var year = DateTime.UtcNow.Year;
        var model = new EditQuotasViewModel
        {
            EmployeeId = user.Id,
            EmployeeName = user.FullName,
            Year = year,
            AnnualAllocatedDays = await GetAllocatedDaysAsync(user.Id, LeaveType.Annual, year),
            MedicalAllocatedDays = await GetAllocatedDaysAsync(user.Id, LeaveType.Medical, year),
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuotas(Guid id, EditQuotasViewModel model)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var year = DateTime.UtcNow.Year;
        model.EmployeeId = user.Id;
        model.EmployeeName = user.FullName;
        model.Year = year;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // ModelState.IsValid (checked above) guarantees both values are non-null here:
        // [Required] on the nullable decimals rejects a missing/blank submission first.
        await SetAllocatedDaysAsync(user.Id, LeaveType.Annual, year, model.AnnualAllocatedDays!.Value);
        await SetAllocatedDaysAsync(user.Id, LeaveType.Medical, year, model.MedicalAllocatedDays!.Value);
        await dbContext.SaveChangesAsync();

        TempData["EmployeesSuccess"] = $"Updated {year} quotas for {user.FullName}.";
        return RedirectToAction(nameof(EditQuotas), new { id });
    }

    private async Task<decimal> GetAllocatedDaysAsync(Guid employeeId, LeaveType leaveType, int year)
    {
        var policy = await dbContext.LeavePolicies
            .SingleOrDefaultAsync(p => p.EmployeeId == employeeId && p.LeaveType == leaveType && p.Year == year);

        return policy?.AllocatedDays ?? 0m;
    }

    private async Task SetAllocatedDaysAsync(Guid employeeId, LeaveType leaveType, int year, decimal allocatedDays)
    {
        var policy = await dbContext.LeavePolicies
            .SingleOrDefaultAsync(p => p.EmployeeId == employeeId && p.LeaveType == leaveType && p.Year == year);

        if (policy is null)
        {
            dbContext.LeavePolicies.Add(new LeavePolicy
            {
                Id = Guid.NewGuid(),
                EmployeeId = employeeId,
                LeaveType = leaveType,
                Year = year,
                AllocatedDays = allocatedDays,
            });
            return;
        }

        policy.AllocatedDays = allocatedDays;
        policy.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<ResetPasswordViewModel> BuildResetPasswordViewModelAsync(ResetPasswordViewModel model)
    {
        model.ActiveUsers = await userManager.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem($"{u.FullName} ({u.Email})", u.Id.ToString()))
            .ToListAsync();

        return model;
    }

    private static CreateEmployeeViewModel BuildCreateEmployeeViewModel(CreateEmployeeViewModel model)
    {
        model.RoleOptions = BuildRoleOptions(model.Role);
        return model;
    }

    private async Task<EditEmployeeViewModel> BuildEditEmployeeViewModelAsync(EditEmployeeViewModel model)
    {
        model.RoleOptions = BuildRoleOptions(model.Role);

        // Assignable managers: active users, excluding the employee being edited (S3-2:
        // self-assignment and deactivated users are both disallowed).
        model.ManagerOptions = await userManager.Users
            .Where(u => u.IsActive && u.Id != model.Id)
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem($"{u.FullName} ({u.Email})", u.Id.ToString()))
            .ToListAsync();

        return model;
    }

    private static List<SelectListItem> BuildRoleOptions(string? selectedRole)
    {
        return Roles.All
            .Select(role => new SelectListItem(role, role, role == selectedRole))
            .ToList();
    }
}
