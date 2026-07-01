using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Models.Entities;
using LeaveAutopilot.Web.Models.Leave;
using LeaveAutopilot.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LeaveAutopilot.Web.Controllers;

/// <summary>
/// S4-3: leave request submission and each employee's own leave dashboard. Every user
/// (Employee, Manager, or HR) is also an employee (FR-3) and may submit their own
/// requests, so this controller carries no role restriction beyond the app-wide
/// authenticated-user fallback policy.
/// </summary>
[Authorize]
public class LeaveController(
    ApplicationDbContext dbContext,
    IBalanceService balanceService,
    ILeaveRequestService leaveRequestService,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var employeeId = GetEmployeeId();
        return View(await BuildIndexViewModelAsync(employeeId));
    }

    [HttpGet]
    public IActionResult Apply()
    {
        return View(BuildApplyViewModel(new ApplyLeaveViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(ApplyLeaveViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(BuildApplyViewModel(model));
        }

        var employeeId = GetEmployeeId();
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // ModelState.IsValid (checked above) guarantees these are non-null: [Required] on
        // the nullable LeaveType?/DateOnly? fields rejects a missing selection first.
        var result = await leaveRequestService.SubmitAsync(
            employeeId,
            model.LeaveType!.Value,
            model.StartDate!.Value,
            model.EndDate!.Value,
            model.StartHalfDay,
            model.EndHalfDay,
            model.Reason,
            today);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage!);
            return View(BuildApplyViewModel(model));
        }

        var request = result.Request!;
        TempData["LeaveSuccess"] =
            $"Submitted {request.LeaveType} request for {request.ChargeableDays:0.#} day(s) ({request.StartDate:d} - {request.EndDate:d}) — now Pending.";
        return RedirectToAction(nameof(Index));
    }

    private Guid GetEmployeeId() => Guid.Parse(userManager.GetUserId(User)!);

    private async Task<LeaveIndexViewModel> BuildIndexViewModelAsync(Guid employeeId)
    {
        var year = DateTime.UtcNow.Year;

        var balances = new List<LeaveBalanceViewModel>();
        foreach (var leaveType in new[] { LeaveType.Annual, LeaveType.Medical })
        {
            balances.Add(new LeaveBalanceViewModel
            {
                LeaveType = leaveType,
                AllocatedDays = await balanceService.GetAllocatedDaysAsync(employeeId, leaveType, year),
                RemainingDays = await balanceService.GetRemainingBalanceAsync(employeeId, leaveType, year),
            });
        }

        var requests = await dbContext.LeaveRequests
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new LeaveRequestListItemViewModel
            {
                Id = r.Id,
                LeaveType = r.LeaveType,
                StartDate = r.StartDate,
                EndDate = r.EndDate,
                StartHalfDay = r.StartHalfDay,
                EndHalfDay = r.EndHalfDay,
                ChargeableDays = r.ChargeableDays,
                State = r.State,
                Reason = r.Reason,
                CreatedAt = r.CreatedAt,
            })
            .ToListAsync();

        return new LeaveIndexViewModel { Balances = balances, Requests = requests };
    }

    private static ApplyLeaveViewModel BuildApplyViewModel(ApplyLeaveViewModel model)
    {
        model.LeaveTypeOptions = Enum.GetValues<LeaveType>()
            .Select(type => new SelectListItem(type.ToString(), type.ToString(), type == model.LeaveType))
            .ToList();

        return model;
    }
}
