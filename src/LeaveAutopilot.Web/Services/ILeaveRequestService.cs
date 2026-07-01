using LeaveAutopilot.Web.Models.Entities;

namespace LeaveAutopilot.Web.Services;

/// <summary>
/// S4-3: validates and submits a new leave request. On success the request is persisted
/// as <see cref="LeaveRequestState.Pending"/> and, for balance-backed types, reserves the
/// computed chargeable days against the employee's remaining balance.
/// </summary>
public interface ILeaveRequestService
{
    Task<LeaveRequestSubmissionResult> SubmitAsync(
        Guid employeeId,
        LeaveType leaveType,
        DateOnly startDate,
        DateOnly endDate,
        bool startHalfDay,
        bool endHalfDay,
        string? reason,
        DateOnly today,
        CancellationToken cancellationToken = default);
}
