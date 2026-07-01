using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Models.Entities;

namespace LeaveAutopilot.Web.Services;

/// <inheritdoc cref="ILeaveRequestService"/>
public class LeaveRequestService(ApplicationDbContext dbContext, IBalanceService balanceService) : ILeaveRequestService
{
    public async Task<LeaveRequestSubmissionResult> SubmitAsync(
        Guid employeeId,
        LeaveType leaveType,
        DateOnly startDate,
        DateOnly endDate,
        bool startHalfDay,
        bool endHalfDay,
        string? reason,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        // FR-13: end date must be on or after the start date.
        if (endDate < startDate)
        {
            return LeaveRequestSubmissionResult.Failed("End date must be on or after the start date.");
        }

        // FR-13: dates must not be in the past. Only the start date needs checking —
        // end >= start (checked above) means the end date can't be in the past either.
        if (startDate < today)
        {
            return LeaveRequestSubmissionResult.Failed("Start date cannot be in the past.");
        }

        // Sprint plan assumption #2: calendar-year quotas mean a request spanning
        // 31 Dec -> 1 Jan is disallowed in v1 rather than split across two years.
        if (startDate.Year != endDate.Year)
        {
            return LeaveRequestSubmissionResult.Failed("Leave requests cannot span across calendar years.");
        }

        // A single-day request only needs one half-day flag; both flags true for the same
        // date is an ambiguous "half of a half day" input rather than a meaningful state.
        if (startDate == endDate && startHalfDay && endHalfDay)
        {
            return LeaveRequestSubmissionResult.Failed(
                "Select only one half-day option for a single-day request.");
        }

        var chargeableDays = WorkingDayCalculator.CalculateChargeableDays(startDate, endDate, startHalfDay, endHalfDay);
        if (chargeableDays <= 0m)
        {
            return LeaveRequestSubmissionResult.Failed("The selected dates contain no working days.");
        }

        // All balance-affecting work happens within one transaction: the remaining-balance
        // read and the reservation insert must be atomic so a concurrent submission can't
        // observe a stale balance between the two (S4-2).
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (leaveType != LeaveType.Unpaid)
        {
            var remaining = await balanceService.GetRemainingBalanceAsync(employeeId, leaveType, startDate.Year, cancellationToken);
            if (chargeableDays > remaining)
            {
                return LeaveRequestSubmissionResult.Failed(
                    $"Insufficient {leaveType} balance: {remaining:0.#} day(s) remaining, {chargeableDays:0.#} requested.");
            }
        }

        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveType = leaveType,
            StartDate = startDate,
            EndDate = endDate,
            StartHalfDay = startHalfDay,
            EndHalfDay = endHalfDay,
            ChargeableDays = chargeableDays,
            State = LeaveRequestState.Pending,
            Reason = reason,
        };

        dbContext.LeaveRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return LeaveRequestSubmissionResult.Succeeded(request);
    }
}
