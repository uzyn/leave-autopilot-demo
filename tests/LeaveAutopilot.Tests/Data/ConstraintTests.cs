using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeaveAutopilot.Tests.Data;

/// <summary>
/// Verifies the DB-level constraints declared in ApplicationDbContext (S1-2 AC): unique
/// employee email, unique policy per employee+type+year, non-negative allocated/chargeable
/// days, and that enums persist and round-trip correctly.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ConstraintTests(DatabaseFixture fixture)
{
    private static ApplicationUser NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FullName = "Test User",
    };

    [Fact]
    public async Task DuplicateEmail_IsRejected_AtDatabaseLevel()
    {
        var email = $"dup-{Guid.NewGuid():N}@leaveautopilot.local";

        await using (var db = fixture.CreateContext())
        {
            db.Users.Add(NewUser(email));
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.CreateContext();
        db2.Users.Add(NewUser(email));

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicatePolicy_ForSameEmployeeTypeAndYear_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"policy-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Annual, Year = 2026, AllocatedDays = 14 });
        await db.SaveChangesAsync();

        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Annual, Year = 2026, AllocatedDays = 10 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task NegativeAllocatedDays_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"neg-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Medical, Year = 2026, AllocatedDays = -1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task UnpaidLeavePolicy_IsRejected_BecauseUnpaidHasNoQuota()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"unpaid-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = user.Id, LeaveType = LeaveType.Unpaid, Year = 2026, AllocatedDays = 5 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LeaveRequest_EndDateBeforeStartDate_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser($"daterange-{Guid.NewGuid():N}@leaveautopilot.local");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = user.Id,
            LeaveType = LeaveType.Annual,
            StartDate = new DateOnly(2026, 3, 10),
            EndDate = new DateOnly(2026, 3, 9),
            ChargeableDays = 1,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LeaveRequest_EnumsRoundTrip_ThroughDatabase()
    {
        Guid requestId;
        await using (var db = fixture.CreateContext())
        {
            var user = NewUser($"enum-{Guid.NewGuid():N}@leaveautopilot.local");
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var request = new LeaveRequest
            {
                Id = Guid.NewGuid(),
                EmployeeId = user.Id,
                LeaveType = LeaveType.Medical,
                StartDate = new DateOnly(2026, 5, 1),
                EndDate = new DateOnly(2026, 5, 1),
                StartHalfDay = true,
                ChargeableDays = 0.5m,
                State = LeaveRequestState.Approved,
            };
            db.LeaveRequests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        // Reload from a fresh context/connection to prove the values persisted, not just an in-memory tracked value.
        await using var freshDb = fixture.CreateContext();
        var reloaded = await freshDb.LeaveRequests.SingleAsync(r => r.Id == requestId);

        Assert.Equal(LeaveType.Medical, reloaded.LeaveType);
        Assert.Equal(LeaveRequestState.Approved, reloaded.State);
        Assert.True(reloaded.StartHalfDay);
        Assert.Equal(0.5m, reloaded.ChargeableDays);
    }
}
