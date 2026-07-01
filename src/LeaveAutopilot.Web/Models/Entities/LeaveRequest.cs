namespace LeaveAutopilot.Web.Models.Entities;

/// <summary>
/// A leave request submitted by an employee. Chargeable days are computed server-side
/// (working days only, with half-day adjustments) at submission time.
/// </summary>
public class LeaveRequest
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public ApplicationUser Employee { get; set; } = null!;

    public LeaveType LeaveType { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public bool StartHalfDay { get; set; }

    public bool EndHalfDay { get; set; }

    /// <summary>Computed working-day count for this request (weekends excluded; half-days subtract 0.5).</summary>
    public decimal ChargeableDays { get; set; }

    public LeaveRequestState State { get; set; } = LeaveRequestState.Pending;

    public string? Reason { get; set; }

    /// <summary>Who decided (approved/rejected) this request — the assigned manager, or HR for manager-less fallback.</summary>
    public Guid? DecidedByEmployeeId { get; set; }

    public ApplicationUser? DecidedByEmployee { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Optional note recorded on rejection.</summary>
    public string? DecisionNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
