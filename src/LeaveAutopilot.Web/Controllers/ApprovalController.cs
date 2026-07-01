using LeaveAutopilot.Web.Models;
using LeaveAutopilot.Web.Models.Approval;
using LeaveAutopilot.Web.Models.Entities;
using LeaveAutopilot.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LeaveAutopilot.Web.Controllers;

/// <summary>
/// S5-1/S5-2/S5-3: the approval queue and approve/reject decisions. HR's manager-assignment
/// screen (S3-2) does not restrict the assignable manager to the Manager role, so anyone can
/// end up with reports — this controller carries no role restriction beyond the app-wide
/// authenticated-user fallback policy; HR additionally sees the manager-less fallback queue.
/// </summary>
[Authorize]
public class ApprovalController(
    IApprovalService approvalService,
    IBalanceService balanceService,
    UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var deciderId = GetUserId();
        var isHr = User.IsInRole(Roles.Hr);

        var model = new ApprovalQueueViewModel
        {
            IsHr = isHr,
            ManagerQueue = await BuildQueueItemsAsync(await approvalService.GetPendingRequestsForManagerAsync(deciderId)),
        };

        if (isHr)
        {
            model.HrFallbackQueue = await BuildQueueItemsAsync(await approvalService.GetPendingRequestsForHrFallbackAsync());
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var result = await approvalService.ApproveAsync(id, GetUserId(), User.IsInRole(Roles.Hr));
        return HandleDecision(result, "approved");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string? note)
    {
        var result = await approvalService.RejectAsync(id, GetUserId(), User.IsInRole(Roles.Hr), note);
        return HandleDecision(result, "rejected");
    }

    private IActionResult HandleDecision(ApprovalDecisionResult result, string verb)
    {
        switch (result.Outcome)
        {
            case ApprovalDecisionOutcome.NotFound:
                return NotFound();
            case ApprovalDecisionOutcome.NotEligible:
                // A resource-level authorization failure (not a role failure), so this is
                // checked here rather than via an [Authorize] attribute: Forbid() triggers
                // the same AccessDenied (403) response as a role-based failure would.
                return Forbid();
            case ApprovalDecisionOutcome.Approved:
            case ApprovalDecisionOutcome.Rejected:
                TempData["ApprovalSuccess"] = $"Request {verb}.";
                return RedirectToAction(nameof(Index));
            default:
                TempData["ApprovalError"] = result.ErrorMessage;
                return RedirectToAction(nameof(Index));
        }
    }

    private Guid GetUserId() => Guid.Parse(userManager.GetUserId(User)!);

    private async Task<List<ApprovalQueueItemViewModel>> BuildQueueItemsAsync(List<LeaveRequest> requests)
    {
        var items = new List<ApprovalQueueItemViewModel>();
        foreach (var request in requests)
        {
            decimal? remaining = request.LeaveType == LeaveType.Unpaid
                ? null
                : await balanceService.GetRemainingBalanceAsync(request.EmployeeId, request.LeaveType, request.StartDate.Year);

            items.Add(new ApprovalQueueItemViewModel
            {
                Id = request.Id,
                RequesterName = request.Employee.FullName,
                LeaveType = request.LeaveType,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                StartHalfDay = request.StartHalfDay,
                EndHalfDay = request.EndHalfDay,
                ChargeableDays = request.ChargeableDays,
                RemainingBalance = remaining,
                Reason = request.Reason,
                CreatedAt = request.CreatedAt,
            });
        }

        return items;
    }
}
