using LeaveAutopilot.Web.Models.Entities;

namespace LeaveAutopilot.Web.Services;

/// <summary>
/// S5-1/S5-2/S5-3: the manager (and HR-fallback) approval queue and approve/reject
/// decisions. A request is decidable by its requester's assigned manager, or — when the
/// requester has no assigned manager — by HR (Sprint plan assumption #1). Only
/// <see cref="LeaveRequestState.Pending"/> requests can be decided; approval re-validates
/// the requester's remaining balance to guard against overspend from concurrent changes.
/// </summary>
public interface IApprovalService
{
    /// <summary>Pending requests from employees whose assigned manager is <paramref name="managerId"/>, oldest first.</summary>
    Task<List<LeaveRequest>> GetPendingRequestsForManagerAsync(Guid managerId, CancellationToken cancellationToken = default);

    /// <summary>Pending requests from employees with no assigned manager at all (HR fallback queue), oldest first.</summary>
    Task<List<LeaveRequest>> GetPendingRequestsForHrFallbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending request. Fails with <see cref="ApprovalDecisionOutcome.NotEligible"/>
    /// unless <paramref name="deciderId"/> is the requester's assigned manager, or the
    /// requester has no manager and <paramref name="deciderIsHr"/> is true.
    /// </summary>
    Task<ApprovalDecisionResult> ApproveAsync(
        Guid requestId, Guid deciderId, bool deciderIsHr, CancellationToken cancellationToken = default);

    /// <summary>Rejects a pending request, optionally recording a note. Same eligibility rules as <see cref="ApproveAsync"/>.</summary>
    Task<ApprovalDecisionResult> RejectAsync(
        Guid requestId, Guid deciderId, bool deciderIsHr, string? note, CancellationToken cancellationToken = default);
}
