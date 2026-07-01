using LeaveAutopilot.Web.Models.Entities;

namespace LeaveAutopilot.Web.Services;

/// <summary>
/// S4-2: authoritative remaining-balance calculation for a balance-backed leave type
/// (Annual, Medical). Remaining = allocated quota - (Approved + Pending) chargeable days
/// for that employee x type x year. Unpaid is uncapped and has no balance to compute.
/// </summary>
public interface IBalanceService
{
    /// <summary>
    /// The employee's remaining balance for <paramref name="leaveType"/> in
    /// <paramref name="year"/>. Throws <see cref="InvalidOperationException"/> for
    /// <see cref="LeaveType.Unpaid"/>, which has no quota and no balance.
    /// </summary>
    Task<decimal> GetRemainingBalanceAsync(
        Guid employeeId, LeaveType leaveType, int year, CancellationToken cancellationToken = default);

    /// <summary>The allocated quota for <paramref name="leaveType"/> in <paramref name="year"/> (0 if HR hasn't set one).</summary>
    Task<decimal> GetAllocatedDaysAsync(
        Guid employeeId, LeaveType leaveType, int year, CancellationToken cancellationToken = default);
}
