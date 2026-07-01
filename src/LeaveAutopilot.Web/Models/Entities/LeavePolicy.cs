namespace LeaveAutopilot.Web.Models.Entities;

/// <summary>
/// An employee's annual quota (allocated days) for a balance-backed leave type in a given
/// calendar year. There is no quota for Unpaid leave — HR only sets Annual and Medical.
/// </summary>
public class LeavePolicy
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public ApplicationUser Employee { get; set; } = null!;

    public LeaveType LeaveType { get; set; }

    /// <summary>Calendar year the allocation applies to.</summary>
    public int Year { get; set; }

    /// <summary>Total days allocated for the year; must be non-negative. Half-day increments allowed.</summary>
    public decimal AllocatedDays { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
