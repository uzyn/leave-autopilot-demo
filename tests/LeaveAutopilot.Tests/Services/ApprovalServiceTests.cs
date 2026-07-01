using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Models.Entities;
using LeaveAutopilot.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace LeaveAutopilot.Tests.Services;

/// <summary>
/// S5-1/S5-2/S5-3 acceptance criteria at the service layer: the manager queue shows only
/// pending requests from that manager's own reports; the HR fallback queue shows only
/// pending requests from manager-less employees; approve confirms the reservation (no
/// balance change beyond what Pending already reserved) while reject releases it; approval
/// re-validates the balance and fails safely if it's no longer sufficient; only Pending
/// requests can be decided; and only the assigned manager (or HR, manager-less only) may
/// decide.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ApprovalServiceTests(DatabaseFixture fixture)
{
    private static readonly DateOnly Start = new(2026, 7, 6); // Monday
    private static readonly DateOnly End = new(2026, 7, 10); // Friday — 5 chargeable days

    private static ApplicationUser NewUser(string email, Guid? managerId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FullName = email,
        ManagerId = managerId,
    };

    private static LeaveRequest NewRequest(
        Guid employeeId,
        LeaveType leaveType = LeaveType.Annual,
        decimal chargeableDays = 5m,
        LeaveRequestState state = LeaveRequestState.Pending,
        DateOnly? start = null,
        DateOnly? end = null) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveType = leaveType,
            StartDate = start ?? Start,
            EndDate = end ?? End,
            ChargeableDays = chargeableDays,
            State = state,
        };

    private async Task<ApplicationDbContext> ArrangeDbAsync() => await Task.FromResult(fixture.CreateContext());

    private static ApprovalService NewService(ApplicationDbContext db, DateTimeOffset? now = null) =>
        new(db, new BalanceService(db), new FakeTimeProvider(now ?? new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero)));

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task ManagerQueue_ReturnsOnlyPendingRequestsFromOwnReports()
    {
        await using var db = await ArrangeDbAsync();
        var managerA = NewUser($"mgr-a-{Guid.NewGuid():N}@leaveautopilot.local");
        var managerB = NewUser($"mgr-b-{Guid.NewGuid():N}@leaveautopilot.local");
        var reportOfA = NewUser($"report-a-{Guid.NewGuid():N}@leaveautopilot.local", managerA.Id);
        var reportOfB = NewUser($"report-b-{Guid.NewGuid():N}@leaveautopilot.local", managerB.Id);
        db.Users.AddRange(managerA, managerB, reportOfA, reportOfB);

        var pendingForA = NewRequest(reportOfA.Id);
        var approvedForA = NewRequest(reportOfA.Id, state: LeaveRequestState.Approved);
        var pendingForB = NewRequest(reportOfB.Id);
        db.LeaveRequests.AddRange(pendingForA, approvedForA, pendingForB);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var queue = await service.GetPendingRequestsForManagerAsync(managerA.Id);

        var queueIds = queue.Select(r => r.Id).ToList();
        Assert.Contains(pendingForA.Id, queueIds);
        Assert.DoesNotContain(approvedForA.Id, queueIds); // not Pending
        Assert.DoesNotContain(pendingForB.Id, queueIds); // different manager's report
    }

    [Fact]
    public async Task HrFallbackQueue_ReturnsOnlyPendingRequestsFromManagerLessEmployees()
    {
        await using var db = await ArrangeDbAsync();
        var manager = NewUser($"mgr-{Guid.NewGuid():N}@leaveautopilot.local");
        var managedEmployee = NewUser($"managed-{Guid.NewGuid():N}@leaveautopilot.local", manager.Id);
        var managerLessEmployee = NewUser($"nomanager-{Guid.NewGuid():N}@leaveautopilot.local", managerId: null);
        db.Users.AddRange(manager, managedEmployee, managerLessEmployee);

        var managedPending = NewRequest(managedEmployee.Id);
        var managerLessPending = NewRequest(managerLessEmployee.Id);
        db.LeaveRequests.AddRange(managedPending, managerLessPending);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var queue = await service.GetPendingRequestsForHrFallbackAsync();

        var queueIds = queue.Select(r => r.Id).ToList();
        Assert.Contains(managerLessPending.Id, queueIds);
        Assert.DoesNotContain(managedPending.Id, queueIds); // has an assigned manager
    }

    [Fact]
    public async Task Approve_MovesToApproved_AndDoesNotChangeRemainingBalance()
    {
        await using var db = await ArrangeDbAsync();
        var manager = NewUser($"mgr-{Guid.NewGuid():N}@leaveautopilot.local");
        var employee = NewUser($"emp-{Guid.NewGuid():N}@leaveautopilot.local", manager.Id);
        db.Users.AddRange(manager, employee);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = employee.Id, LeaveType = LeaveType.Annual, Year = Start.Year, AllocatedDays = 14m });
        var request = NewRequest(employee.Id);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var balanceService = new BalanceService(db);
        var remainingBeforeDecision = await balanceService.GetRemainingBalanceAsync(employee.Id, LeaveType.Annual, Start.Year);

        var service = NewService(db);
        var result = await service.ApproveAsync(request.Id, manager.Id, deciderIsHr: false);

        Assert.True(result.Success);
        Assert.Equal(ApprovalDecisionOutcome.Approved, result.Outcome);
        Assert.Equal(LeaveRequestState.Approved, result.Request!.State);

        var remainingAfterDecision = await balanceService.GetRemainingBalanceAsync(employee.Id, LeaveType.Annual, Start.Year);
        Assert.Equal(remainingBeforeDecision, remainingAfterDecision); // already reserved by Pending; no further change
        Assert.Equal(9m, remainingAfterDecision);
    }

    [Fact]
    public async Task Approve_RecordsDeciderTimestampAndNoNote()
    {
        await using var db = await ArrangeDbAsync();
        var manager = NewUser($"mgr-{Guid.NewGuid():N}@leaveautopilot.local");
        var employee = NewUser($"emp-{Guid.NewGuid():N}@leaveautopilot.local", manager.Id);
        db.Users.AddRange(manager, employee);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = employee.Id, LeaveType = LeaveType.Annual, Year = Start.Year, AllocatedDays = 14m });
        var request = NewRequest(employee.Id);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var decidedAt = new DateTimeOffset(2026, 7, 2, 10, 30, 0, TimeSpan.Zero);
        var service = NewService(db, decidedAt);
        var result = await service.ApproveAsync(request.Id, manager.Id, deciderIsHr: false);

        Assert.True(result.Success);
        Assert.Equal(manager.Id, result.Request!.DecidedByEmployeeId);
        Assert.Equal(decidedAt, result.Request.DecidedAt);
        Assert.Null(result.Request.DecisionNote);
    }

    [Fact]
    public async Task Reject_MovesToRejected_RecordsNote_AndReleasesReservedBalance()
    {
        await using var db = await ArrangeDbAsync();
        var manager = NewUser($"mgr-{Guid.NewGuid():N}@leaveautopilot.local");
        var employee = NewUser($"emp-{Guid.NewGuid():N}@leaveautopilot.local", manager.Id);
        db.Users.AddRange(manager, employee);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = employee.Id, LeaveType = LeaveType.Annual, Year = Start.Year, AllocatedDays = 14m });
        var request = NewRequest(employee.Id);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.RejectAsync(request.Id, manager.Id, deciderIsHr: false, note: "Team is short-staffed that week.");

        Assert.True(result.Success);
        Assert.Equal(ApprovalDecisionOutcome.Rejected, result.Outcome);
        Assert.Equal(LeaveRequestState.Rejected, result.Request!.State);
        Assert.Equal("Team is short-staffed that week.", result.Request.DecisionNote);
        Assert.Equal(manager.Id, result.Request.DecidedByEmployeeId);

        var balanceService = new BalanceService(db);
        var remaining = await balanceService.GetRemainingBalanceAsync(employee.Id, LeaveType.Annual, Start.Year);
        Assert.Equal(14m, remaining); // fully released
    }

    [Fact]
    public async Task Approve_WhenBalanceNoLongerSufficient_FailsSafely_AndLeavesRequestPending()
    {
        await using var db = await ArrangeDbAsync();
        var manager = NewUser($"mgr-{Guid.NewGuid():N}@leaveautopilot.local");
        var employee = NewUser($"emp-{Guid.NewGuid():N}@leaveautopilot.local", manager.Id);
        db.Users.AddRange(manager, employee);
        var policy = new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = employee.Id, LeaveType = LeaveType.Annual, Year = Start.Year, AllocatedDays = 14m };
        db.LeavePolicies.Add(policy);
        var request = NewRequest(employee.Id, chargeableDays: 5m);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        // Simulate a concurrent HR quota reduction after submission that leaves the
        // already-Pending request over the new (lower) quota.
        policy.AllocatedDays = 2m;
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ApproveAsync(request.Id, manager.Id, deciderIsHr: false);

        Assert.False(result.Success);
        Assert.Equal(ApprovalDecisionOutcome.InsufficientBalance, result.Outcome);
        Assert.Contains("no longer sufficient", result.ErrorMessage);

        var reloaded = await db.LeaveRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(LeaveRequestState.Pending, reloaded.State); // unchanged
        Assert.Null(reloaded.DecidedByEmployeeId);
    }

    [Fact]
    public async Task Approve_UnpaidRequest_SkipsBalanceCheck()
    {
        await using var db = await ArrangeDbAsync();
        var manager = NewUser($"mgr-{Guid.NewGuid():N}@leaveautopilot.local");
        var employee = NewUser($"emp-{Guid.NewGuid():N}@leaveautopilot.local", manager.Id);
        db.Users.AddRange(manager, employee); // no quotas set at all
        var request = NewRequest(employee.Id, leaveType: LeaveType.Unpaid);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ApproveAsync(request.Id, manager.Id, deciderIsHr: false);

        Assert.True(result.Success);
        Assert.Equal(LeaveRequestState.Approved, result.Request!.State);
    }

    [Theory]
    [InlineData(LeaveRequestState.Approved)]
    [InlineData(LeaveRequestState.Rejected)]
    [InlineData(LeaveRequestState.Cancelled)]
    [InlineData(LeaveRequestState.Withdrawn)]
    public async Task Decide_OnANonPendingRequest_IsRejectedAsInvalidState(LeaveRequestState currentState)
    {
        await using var db = await ArrangeDbAsync();
        var manager = NewUser($"mgr-{Guid.NewGuid():N}@leaveautopilot.local");
        var employee = NewUser($"emp-{Guid.NewGuid():N}@leaveautopilot.local", manager.Id);
        db.Users.AddRange(manager, employee);
        var request = NewRequest(employee.Id, state: currentState);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var approveResult = await service.ApproveAsync(request.Id, manager.Id, deciderIsHr: false);
        Assert.Equal(ApprovalDecisionOutcome.InvalidState, approveResult.Outcome);

        var rejectResult = await service.RejectAsync(request.Id, manager.Id, deciderIsHr: false, note: null);
        Assert.Equal(ApprovalDecisionOutcome.InvalidState, rejectResult.Outcome);
    }

    [Fact]
    public async Task Decide_ByAManagerNotAssignedToTheRequester_IsNotEligible()
    {
        await using var db = await ArrangeDbAsync();
        var assignedManager = NewUser($"mgr-assigned-{Guid.NewGuid():N}@leaveautopilot.local");
        var otherManager = NewUser($"mgr-other-{Guid.NewGuid():N}@leaveautopilot.local");
        var employee = NewUser($"emp-{Guid.NewGuid():N}@leaveautopilot.local", assignedManager.Id);
        db.Users.AddRange(assignedManager, otherManager, employee);
        var request = NewRequest(employee.Id);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ApproveAsync(request.Id, otherManager.Id, deciderIsHr: false);

        Assert.Equal(ApprovalDecisionOutcome.NotEligible, result.Outcome);
        var reloaded = await db.LeaveRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(LeaveRequestState.Pending, reloaded.State); // unchanged
    }

    [Fact]
    public async Task Decide_ByHr_WhenRequesterHasAnAssignedManager_IsNotEligible()
    {
        await using var db = await ArrangeDbAsync();
        var manager = NewUser($"mgr-{Guid.NewGuid():N}@leaveautopilot.local");
        var hr = NewUser($"hr-{Guid.NewGuid():N}@leaveautopilot.local");
        var employee = NewUser($"emp-{Guid.NewGuid():N}@leaveautopilot.local", manager.Id);
        db.Users.AddRange(manager, hr, employee);
        var request = NewRequest(employee.Id);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ApproveAsync(request.Id, hr.Id, deciderIsHr: true);

        Assert.Equal(ApprovalDecisionOutcome.NotEligible, result.Outcome);
    }

    [Fact]
    public async Task Decide_ByHr_WhenRequesterHasNoManager_IsEligible()
    {
        await using var db = await ArrangeDbAsync();
        var hr = NewUser($"hr-{Guid.NewGuid():N}@leaveautopilot.local");
        var employee = NewUser($"emp-{Guid.NewGuid():N}@leaveautopilot.local", managerId: null);
        db.Users.AddRange(hr, employee);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = employee.Id, LeaveType = LeaveType.Annual, Year = Start.Year, AllocatedDays = 14m });
        var request = NewRequest(employee.Id);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ApproveAsync(request.Id, hr.Id, deciderIsHr: true);

        Assert.True(result.Success);
        Assert.Equal(hr.Id, result.Request!.DecidedByEmployeeId);
    }

    [Fact]
    public async Task Decide_OnAnUnknownRequest_ReturnsNotFound()
    {
        await using var db = await ArrangeDbAsync();
        var manager = NewUser($"mgr-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(manager);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var result = await service.ApproveAsync(Guid.NewGuid(), manager.Id, deciderIsHr: false);

        Assert.Equal(ApprovalDecisionOutcome.NotFound, result.Outcome);
    }
}
