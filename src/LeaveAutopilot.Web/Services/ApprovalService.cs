using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeaveAutopilot.Web.Services;

/// <inheritdoc cref="IApprovalService"/>
public class ApprovalService(ApplicationDbContext dbContext, IBalanceService balanceService, TimeProvider timeProvider)
    : IApprovalService
{
    public async Task<List<LeaveRequest>> GetPendingRequestsForManagerAsync(
        Guid managerId, CancellationToken cancellationToken = default)
    {
        return await dbContext.LeaveRequests
            .Include(r => r.Employee)
            .Where(r => r.State == LeaveRequestState.Pending && r.Employee.ManagerId == managerId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<LeaveRequest>> GetPendingRequestsForHrFallbackAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.LeaveRequests
            .Include(r => r.Employee)
            .Where(r => r.State == LeaveRequestState.Pending && r.Employee.ManagerId == null)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<ApprovalDecisionResult> ApproveAsync(
        Guid requestId, Guid deciderId, bool deciderIsHr, CancellationToken cancellationToken = default)
        => DecideAsync(requestId, deciderId, deciderIsHr, approve: true, note: null, cancellationToken);

    public Task<ApprovalDecisionResult> RejectAsync(
        Guid requestId, Guid deciderId, bool deciderIsHr, string? note, CancellationToken cancellationToken = default)
        => DecideAsync(requestId, deciderId, deciderIsHr, approve: false, note, cancellationToken);

    private async Task<ApprovalDecisionResult> DecideAsync(
        Guid requestId, Guid deciderId, bool deciderIsHr, bool approve, string? note, CancellationToken cancellationToken)
    {
        var request = await dbContext.LeaveRequests
            .Include(r => r.Employee)
            .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request is null)
        {
            return ApprovalDecisionResult.NotFound();
        }

        // FR-15/FR-20 + Sprint plan assumption #1: decidable by the requester's assigned
        // manager, or by HR when (and only when) the requester has no assigned manager at
        // all. HR does not get a blanket override of every request.
        var isEligible = request.Employee.ManagerId == deciderId
            || (request.Employee.ManagerId is null && deciderIsHr);
        if (!isEligible)
        {
            return ApprovalDecisionResult.NotEligible();
        }

        // Only Pending requests are decidable; terminal states cannot be re-decided.
        if (request.State != LeaveRequestState.Pending)
        {
            return ApprovalDecisionResult.InvalidState(
                $"This request is already {request.State} and cannot be re-decided.");
        }

        // Balance mutation/validation happens inside a transaction, mirroring
        // LeaveRequestService.SubmitAsync (S4-3): the re-check and the state write must be
        // atomic so a concurrent change can't slip past a stale read.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (approve && request.LeaveType != LeaveType.Unpaid)
        {
            // Re-validate at approval time (S5-2/FR-9): this pending request is already
            // counted in "remaining" (Pending balances reserve), so a negative remaining
            // balance means the quota was reduced, or another request pushed the employee
            // over, since this request was submitted. Fail safely rather than approve into
            // an over-quota state.
            var remaining = await balanceService.GetRemainingBalanceAsync(
                request.EmployeeId, request.LeaveType, request.StartDate.Year, cancellationToken);

            if (remaining < 0m)
            {
                return ApprovalDecisionResult.InsufficientBalance(
                    $"Cannot approve: {request.Employee.FullName}'s {request.LeaveType} balance is no longer sufficient ({remaining:0.#} day(s) remaining).");
            }
        }

        request.State = approve ? LeaveRequestState.Approved : LeaveRequestState.Rejected;
        request.DecidedByEmployeeId = deciderId;
        request.DecidedAt = timeProvider.GetUtcNow();
        // A real browser submits an empty "note" <input> as "", not an omitted form key.
        // Normalize to null so Leave/Index.cshtml's `?? "—"` placeholder renders correctly.
        request.DecisionNote = approve ? null : NormalizeNote(note);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return approve ? ApprovalDecisionResult.Approved(request) : ApprovalDecisionResult.Rejected(request);
    }

    public async Task<bool> HasReportsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users.AnyAsync(u => u.ManagerId == employeeId, cancellationToken);
    }

    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note;
}
