using LeaveAutopilot.Web.Models.Entities;

namespace LeaveAutopilot.Web.Models.Leave;

/// <summary>A balance-backed leave type's allocated/remaining days for the current year, shown on the employee's leave dashboard.</summary>
public class LeaveBalanceViewModel
{
    public LeaveType LeaveType { get; set; }

    public decimal AllocatedDays { get; set; }

    public decimal RemainingDays { get; set; }
}

/// <summary>One row in the employee's own request history/list.</summary>
public class LeaveRequestListItemViewModel
{
    public Guid Id { get; set; }

    public LeaveType LeaveType { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public bool StartHalfDay { get; set; }

    public bool EndHalfDay { get; set; }

    public decimal ChargeableDays { get; set; }

    public LeaveRequestState State { get; set; }

    public string? Reason { get; set; }

    /// <summary>Optional note the decider recorded on rejection (S5-2).</summary>
    public string? DecisionNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// The employee's own leave dashboard (S4-3's minimal slice of what Sprint 6's
/// balance/history views will expand on): current-year balances plus their own requests.
/// </summary>
public class LeaveIndexViewModel
{
    public List<LeaveBalanceViewModel> Balances { get; set; } = [];

    public List<LeaveRequestListItemViewModel> Requests { get; set; } = [];
}
