using LeaveAutopilot.Web.Models.Entities;

namespace LeaveAutopilot.Web.Services;

/// <summary>The outcome of <see cref="ILeaveRequestService.SubmitAsync"/>: either the created request, or a clear rejection reason.</summary>
public sealed class LeaveRequestSubmissionResult
{
    private LeaveRequestSubmissionResult(bool success, string? errorMessage, LeaveRequest? request)
    {
        Success = success;
        ErrorMessage = errorMessage;
        Request = request;
    }

    public bool Success { get; }

    public string? ErrorMessage { get; }

    public LeaveRequest? Request { get; }

    public static LeaveRequestSubmissionResult Succeeded(LeaveRequest request) => new(true, null, request);

    public static LeaveRequestSubmissionResult Failed(string errorMessage) => new(false, errorMessage, null);
}
