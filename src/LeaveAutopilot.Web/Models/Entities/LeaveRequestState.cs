namespace LeaveAutopilot.Web.Models.Entities;

/// <summary>
/// The lifecycle state of a <see cref="LeaveRequest"/>.
/// Valid transitions: Pending -> Approved | Rejected | Cancelled; Approved -> Withdrawn (future-dated only).
/// Rejected, Cancelled and Withdrawn are terminal.
/// </summary>
public enum LeaveRequestState
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
    Withdrawn,
}
