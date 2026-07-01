using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models.Entities;
using LeaveAutopilot.Web.Services;

namespace LeaveAutopilot.Tests.Services;

/// <summary>
/// S4-2 acceptance criteria: remaining balance = allocated quota - (Approved + Pending)
/// chargeable days for that employee x type x year; Pending reserves balance; Unpaid never
/// affects any balance; balance math is verified across a mix of states.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class BalanceServiceTests(DatabaseFixture fixture)
{
    private const int Year = 2026;

    private static ApplicationUser NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FullName = "Test User",
    };

    private static LeaveRequest NewRequest(
        Guid employeeId, LeaveType leaveType, LeaveRequestState state, decimal chargeableDays, DateOnly? startDate = null) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveType = leaveType,
            StartDate = startDate ?? new DateOnly(Year, 3, 2),
            EndDate = startDate ?? new DateOnly(Year, 3, 2),
            ChargeableDays = chargeableDays,
            State = state,
        };

    [Fact]
    public async Task RemainingBalance_WithNoRequests_EqualsAllocatedQuota()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"bal-none-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Annual, Year = Year, AllocatedDays = 14 });
        await db.SaveChangesAsync();

        var service = new BalanceService(db);
        var remaining = await service.GetRemainingBalanceAsync(user.Id, LeaveType.Annual, Year);

        Assert.Equal(14m, remaining);
    }

    [Fact]
    public async Task PendingRequests_ReserveBalance()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"bal-pending-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Annual, Year = Year, AllocatedDays = 14 });
        db.LeaveRequests.Add(NewRequest(user.Id, LeaveType.Annual, LeaveRequestState.Pending, 3m));
        await db.SaveChangesAsync();

        var service = new BalanceService(db);
        var remaining = await service.GetRemainingBalanceAsync(user.Id, LeaveType.Annual, Year);

        Assert.Equal(11m, remaining);
    }

    [Fact]
    public async Task ApprovedRequests_ReduceBalance_SameAsPending()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"bal-approved-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Annual, Year = Year, AllocatedDays = 14 });
        db.LeaveRequests.Add(NewRequest(user.Id, LeaveType.Annual, LeaveRequestState.Approved, 5m));
        await db.SaveChangesAsync();

        var service = new BalanceService(db);
        var remaining = await service.GetRemainingBalanceAsync(user.Id, LeaveType.Annual, Year);

        Assert.Equal(9m, remaining);
    }

    [Fact]
    public async Task MixOfApprovedAndPending_BothReduceBalance()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"bal-mix-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Medical, Year = Year, AllocatedDays = 14 });
        db.LeaveRequests.Add(NewRequest(user.Id, LeaveType.Medical, LeaveRequestState.Approved, 2.5m, new DateOnly(Year, 1, 5)));
        db.LeaveRequests.Add(NewRequest(user.Id, LeaveType.Medical, LeaveRequestState.Pending, 1.5m, new DateOnly(Year, 6, 1)));
        await db.SaveChangesAsync();

        var service = new BalanceService(db);
        var remaining = await service.GetRemainingBalanceAsync(user.Id, LeaveType.Medical, Year);

        Assert.Equal(10m, remaining);
    }

    [Theory]
    [InlineData(LeaveRequestState.Rejected)]
    [InlineData(LeaveRequestState.Cancelled)]
    [InlineData(LeaveRequestState.Withdrawn)]
    public async Task TerminalNonApprovedStates_DoNotAffectBalance(LeaveRequestState state)
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"bal-terminal-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Annual, Year = Year, AllocatedDays = 14 });
        db.LeaveRequests.Add(NewRequest(user.Id, LeaveType.Annual, state, 4m));
        await db.SaveChangesAsync();

        var service = new BalanceService(db);
        var remaining = await service.GetRemainingBalanceAsync(user.Id, LeaveType.Annual, Year);

        Assert.Equal(14m, remaining);
    }

    [Fact]
    public async Task RequestsInADifferentYear_DoNotAffectThisYearsBalance()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"bal-otheryear-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Annual, Year = Year, AllocatedDays = 14 });
        db.LeaveRequests.Add(NewRequest(user.Id, LeaveType.Annual, LeaveRequestState.Approved, 5m, new DateOnly(Year - 1, 12, 15)));
        await db.SaveChangesAsync();

        var service = new BalanceService(db);
        var remaining = await service.GetRemainingBalanceAsync(user.Id, LeaveType.Annual, Year);

        Assert.Equal(14m, remaining);
    }

    [Fact]
    public async Task NoQuotaSet_TreatsAllocatedAsZero()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"bal-noquota-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new BalanceService(db);
        var remaining = await service.GetRemainingBalanceAsync(user.Id, LeaveType.Medical, Year);

        Assert.Equal(0m, remaining);
    }

    [Fact]
    public async Task UnpaidLeaveType_ThrowsBecauseItHasNoBalance()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"bal-unpaid-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new BalanceService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetRemainingBalanceAsync(user.Id, LeaveType.Unpaid, Year));
    }

    [Fact]
    public async Task DifferentEmployees_BalancesAreIndependent()
    {
        await using var db = fixture.CreateContext();
        var userA = NewUser($"bal-a-{Guid.NewGuid():N}@leaveautopilot.local");
        var userB = NewUser($"bal-b-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.AddRange(userA, userB);
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = userA.Id, LeaveType = LeaveType.Annual, Year = Year, AllocatedDays = 14 });
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = userB.Id, LeaveType = LeaveType.Annual, Year = Year, AllocatedDays = 20 });
        db.LeaveRequests.Add(NewRequest(userA.Id, LeaveType.Annual, LeaveRequestState.Approved, 10m));
        await db.SaveChangesAsync();

        var service = new BalanceService(db);

        Assert.Equal(4m, await service.GetRemainingBalanceAsync(userA.Id, LeaveType.Annual, Year));
        Assert.Equal(20m, await service.GetRemainingBalanceAsync(userB.Id, LeaveType.Annual, Year));
    }
}
