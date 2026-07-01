using LeaveAutopilot.Web.Models.Entities;

namespace LeaveAutopilot.Web.Services;

/// <summary>The specific outcome of a decision attempt via <see cref="IApprovalService"/>.</summary>
public enum ApprovalDecisionOutcome
{
    Approved,
    Rejected,
    NotFound,
    NotEligible,
    InvalidState,
    InsufficientBalance,
}

/// <summary>The outcome of an approve/reject decision: either the updated request, or a specific failure reason the caller can act on.</summary>
public sealed class ApprovalDecisionResult
{
    private ApprovalDecisionResult(ApprovalDecisionOutcome outcome, string? errorMessage, LeaveRequest? request)
    {
        Outcome = outcome;
        ErrorMessage = errorMessage;
        Request = request;
    }

    public ApprovalDecisionOutcome Outcome { get; }

    public string? ErrorMessage { get; }

    public LeaveRequest? Request { get; }

    public bool Success => Outcome is ApprovalDecisionOutcome.Approved or ApprovalDecisionOutcome.Rejected;

    public static ApprovalDecisionResult Approved(LeaveRequest request) => new(ApprovalDecisionOutcome.Approved, null, request);

    public static ApprovalDecisionResult Rejected(LeaveRequest request) => new(ApprovalDecisionOutcome.Rejected, null, request);

    public static ApprovalDecisionResult NotFound() =>
        new(ApprovalDecisionOutcome.NotFound, "Request not found.", null);

    public static ApprovalDecisionResult NotEligible() =>
        new(ApprovalDecisionOutcome.NotEligible, "You are not authorized to decide this request.", null);

    public static ApprovalDecisionResult InvalidState(string message) =>
        new(ApprovalDecisionOutcome.InvalidState, message, null);

    public static ApprovalDecisionResult InsufficientBalance(string message) =>
        new(ApprovalDecisionOutcome.InsufficientBalance, message, null);
}
