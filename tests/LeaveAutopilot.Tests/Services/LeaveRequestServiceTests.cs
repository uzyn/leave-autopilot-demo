using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models.Entities;
using LeaveAutopilot.Web.Services;

namespace LeaveAutopilot.Tests.Services;

/// <summary>
/// S4-3 acceptance criteria: a valid request is submitted as Pending and reserves the
/// correct chargeable days; submission is rejected with clear messages for end-before-
/// start, past dates, cross-year spans, and insufficient balance; Unpaid requests submit
/// regardless of balance and reserve no balance.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class LeaveRequestServiceTests(DatabaseFixture fixture)
{
    private static readonly DateOnly Today = new(2026, 7, 1);

    private static ApplicationUser NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FullName = "Test User",
    };

    private async Task<(LeaveAutopilot.Web.Data.ApplicationDbContext Db, ApplicationUser User, LeaveRequestService Service)> ArrangeAsync(
        decimal? annualQuota = null, decimal? medicalQuota = null)
    {
        var db = fixture.CreateContext();
        var user = NewUser($"submit-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);

        if (annualQuota.HasValue)
        {
            db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Annual, Year = Today.Year, AllocatedDays = annualQuota.Value });
        }

        if (medicalQuota.HasValue)
        {
            db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Medical, Year = Today.Year, AllocatedDays = medicalQuota.Value });
        }

        await db.SaveChangesAsync();

        var service = new LeaveRequestService(db, new BalanceService(db));
        return (db, user, service);
    }

    [Fact]
    public async Task ValidRequest_SpanningAWeekend_IsPendingWithCorrectDaysAndReservedBalance()
    {
        var (db, user, service) = await ArrangeAsync(annualQuota: 14m);

        // Thu 2026-07-09 -> Mon 2026-07-13: 3 chargeable days (weekend excluded).
        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 7, 9), new DateOnly(2026, 7, 13),
            startHalfDay: false, endHalfDay: false, reason: "Trip", today: Today);

        Assert.True(result.Success);
        Assert.NotNull(result.Request);
        Assert.Equal(LeaveRequestState.Pending, result.Request!.State);
        Assert.Equal(3m, result.Request.ChargeableDays);

        var remaining = await new BalanceService(db).GetRemainingBalanceAsync(user.Id, LeaveType.Annual, Today.Year);
        Assert.Equal(11m, remaining);
    }

    [Fact]
    public async Task ValidRequest_WithHalfDays_ComputesCorrectChargeableDays()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 14m);

        // Mon 2026-07-06 (half) -> Wed 2026-07-08 (half): 2.0 chargeable days.
        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 8),
            startHalfDay: true, endHalfDay: true, reason: null, today: Today);

        Assert.True(result.Success);
        Assert.Equal(2m, result.Request!.ChargeableDays);
    }

    [Fact]
    public async Task EndDateBeforeStartDate_IsRejectedWithClearMessage()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 14m);

        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 8),
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);

        Assert.False(result.Success);
        Assert.Equal("End date must be on or after the start date.", result.ErrorMessage);
    }

    [Fact]
    public async Task PastStartDate_IsRejectedWithClearMessage()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 14m);

        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 2),
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);

        Assert.False(result.Success);
        Assert.Equal("Start date cannot be in the past.", result.ErrorMessage);
    }

    [Fact]
    public async Task CrossCalendarYearSpan_IsRejectedWithClearMessage()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 14m);

        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 2),
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);

        Assert.False(result.Success);
        Assert.Equal("Leave requests cannot span across calendar years.", result.ErrorMessage);
    }

    [Fact]
    public async Task InsufficientBalance_IsRejectedWithClearMessage()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 2m);

        // Mon-Fri = 5 chargeable days, but only 2 remain.
        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 10),
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);

        Assert.False(result.Success);
        Assert.Contains("Insufficient", result.ErrorMessage);
    }

    [Fact]
    public async Task RequestExactlyMatchingRemainingBalance_Succeeds()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 5m);

        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 10),
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);

        Assert.True(result.Success);
        Assert.Equal(5m, result.Request!.ChargeableDays);
    }

    [Fact]
    public async Task PendingRequest_ReservesBalance_SoASecondOverlappingRequestIsRejected()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 5m);

        var first = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 10),
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);
        Assert.True(first.Success);

        var second = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 13),
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);

        Assert.False(second.Success);
        Assert.Contains("Insufficient", second.ErrorMessage);
    }

    [Fact]
    public async Task UnpaidRequest_SubmitsRegardlessOfBalance_AndReservesNoBalance()
    {
        var (db, user, service) = await ArrangeAsync(); // No Annual/Medical quotas at all.

        var result = await service.SubmitAsync(
            user.Id, LeaveType.Unpaid, new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 10),
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);

        Assert.True(result.Success);
        Assert.Equal(5m, result.Request!.ChargeableDays);

        // Confirm it doesn't count against Annual/Medical balances (it's a different type,
        // and Unpaid itself has no balance to query).
        var annualRemaining = await new BalanceService(db).GetRemainingBalanceAsync(user.Id, LeaveType.Annual, Today.Year);
        Assert.Equal(0m, annualRemaining);
    }

    [Fact]
    public async Task WeekendOnlyRange_IsRejectedAsNoWorkingDays()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 14m);

        // Sat 2026-07-11 -> Sun 2026-07-12.
        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 12),
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);

        Assert.False(result.Success);
        Assert.Equal("The selected dates contain no working days.", result.ErrorMessage);
    }

    [Fact]
    public async Task BothHalfDayFlagsOnASingleDayRequest_IsRejected()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 14m);

        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 6),
            startHalfDay: true, endHalfDay: true, reason: null, today: Today);

        Assert.False(result.Success);
        Assert.Equal("Select only one half-day option for a single-day request.", result.ErrorMessage);
    }

    [Fact]
    public async Task TodayAsStartDate_IsAllowed_NotTreatedAsPast()
    {
        var (_, user, service) = await ArrangeAsync(annualQuota: 14m);

        var result = await service.SubmitAsync(
            user.Id, LeaveType.Annual, Today, Today,
            startHalfDay: false, endHalfDay: false, reason: null, today: Today);

        // 2026-07-01 is a Wednesday.
        Assert.True(result.Success);
        Assert.Equal(1m, result.Request!.ChargeableDays);
    }
}
