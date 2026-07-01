using System.Data;
using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LeaveAutopilot.Web.Services;

/// <inheritdoc cref="IBalanceService"/>
public class BalanceService(ApplicationDbContext dbContext) : IBalanceService
{
    public async Task<decimal> GetAllocatedDaysAsync(
        Guid employeeId, LeaveType leaveType, int year, CancellationToken cancellationToken = default)
    {
        EnsureBalanceBacked(leaveType);

        return await dbContext.LeavePolicies
            .Where(p => p.EmployeeId == employeeId && p.LeaveType == leaveType && p.Year == year)
            .Select(p => (decimal?)p.AllocatedDays)
            .SingleOrDefaultAsync(cancellationToken) ?? 0m;
    }

    public async Task<decimal> GetRemainingBalanceAsync(
        Guid employeeId, LeaveType leaveType, int year, CancellationToken cancellationToken = default)
    {
        EnsureBalanceBacked(leaveType);

        // The allocated-quota read and the reserved/deducted-sum read must observe one
        // consistent snapshot, not two independently-committed states (a stale read here
        // could let a submission slip past a balance check that's already out of date).
        // If the caller already has an ambient transaction open (e.g.
        // LeaveRequestService.SubmitAsync wrapping this call plus its own insert), reuse
        // it rather than nesting — EF Core/Npgsql doesn't support nested transactions.
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;

        if (ownsTransaction)
        {
            transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        }

        try
        {
            var allocated = await GetAllocatedDaysAsync(employeeId, leaveType, year, cancellationToken);

            var yearStart = new DateOnly(year, 1, 1);
            var yearEnd = new DateOnly(year, 12, 31);

            var reservedOrDeducted = await dbContext.LeaveRequests
                .Where(r => r.EmployeeId == employeeId
                    && r.LeaveType == leaveType
                    && r.StartDate >= yearStart && r.StartDate <= yearEnd
                    && (r.State == LeaveRequestState.Pending || r.State == LeaveRequestState.Approved))
                .SumAsync(r => (decimal?)r.ChargeableDays, cancellationToken) ?? 0m;

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return allocated - reservedOrDeducted;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static void EnsureBalanceBacked(LeaveType leaveType)
    {
        if (leaveType == LeaveType.Unpaid)
        {
            throw new InvalidOperationException("Unpaid leave is uncapped and has no balance to compute.");
        }
    }
}
