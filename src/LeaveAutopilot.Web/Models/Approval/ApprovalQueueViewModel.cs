using LeaveAutopilot.Web.Models.Entities;

namespace LeaveAutopilot.Web.Models.Approval;

/// <summary>One pending request in an approval queue, with the context a decider needs (FR-19).</summary>
public class ApprovalQueueItemViewModel
{
    public Guid Id { get; set; }

    public string RequesterName { get; set; } = string.Empty;

    public LeaveType LeaveType { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public bool StartHalfDay { get; set; }

    public bool EndHalfDay { get; set; }

    public decimal ChargeableDays { get; set; }

    /// <summary>The requester's current remaining balance for this leave type; null for Unpaid (uncapped, no balance).</summary>
    public decimal? RemainingBalance { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// S5-1/S5-3: the decider's approval queue — their own reports' pending requests, plus (HR
/// only) the manager-less fallback queue.
/// </summary>
public class ApprovalQueueViewModel
{
    public bool IsHr { get; set; }

    public List<ApprovalQueueItemViewModel> ManagerQueue { get; set; } = [];

    public List<ApprovalQueueItemViewModel> HrFallbackQueue { get; set; } = [];
}
