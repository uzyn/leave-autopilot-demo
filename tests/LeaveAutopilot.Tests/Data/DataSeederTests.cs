using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Data.Seed;
using LeaveAutopilot.Web.Models;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveAutopilot.Tests.Data;

[Collection(DatabaseCollection.Name)]
public class DataSeederTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task SeedAsync_CreatesRolesAndFirstHrUser_OnEmptyDatabase()
    {
        const string email = "hr-seedtest1@leaveautopilot.local";
        const string password = "SeedTest123!";

        await using var services = IdentityServiceProviderFactory.Build(
            fixture.ConnectionString,
            new SeedOptions { HrEmail = email, HrPassword = password, HrFullName = "Test HR" });

        await DataSeeder.SeedAsync(services);

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
        {
            Assert.True(await roleManager.RoleExistsAsync(role), $"Role '{role}' should have been created.");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var hrUser = await userManager.FindByEmailAsync(email);

        Assert.NotNull(hrUser);
        Assert.True(hrUser!.IsActive);
        Assert.True(await userManager.IsInRoleAsync(hrUser, Roles.Hr));
        Assert.NotEqual(password, hrUser.PasswordHash);
        Assert.True(await userManager.CheckPasswordAsync(hrUser, password));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_WhenRunTwice()
    {
        const string email = "hr-seedtest2@leaveautopilot.local";

        await using var services = IdentityServiceProviderFactory.Build(
            fixture.ConnectionString,
            new SeedOptions { HrEmail = email, HrPassword = "SeedTest123!", HrFullName = "Test HR" });

        await DataSeeder.SeedAsync(services);
        await DataSeeder.SeedAsync(services);

        await using var db = fixture.CreateContext();
        var count = await db.Users.CountAsync(u => u.Email == email);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SeedAsync_WithSampleData_CreatesManagerEmployeesAndPolicies_Idempotently()
    {
        const string email = "hr-seedtest3@leaveautopilot.local";

        await using var services = IdentityServiceProviderFactory.Build(
            fixture.ConnectionString,
            new SeedOptions
            {
                HrEmail = email,
                HrPassword = "SeedTest123!",
                HrFullName = "Test HR",
                IncludeSampleData = true,
            });

        await DataSeeder.SeedAsync(services);
        await DataSeeder.SeedAsync(services); // run twice: must not duplicate

        await using var db = fixture.CreateContext();

        var manager = await db.Users.SingleAsync(u => u.Email == "manager@leaveautopilot.local");
        var alice = await db.Users.SingleAsync(u => u.Email == "alice@leaveautopilot.local");
        var bob = await db.Users.SingleAsync(u => u.Email == "bob@leaveautopilot.local");

        Assert.Equal(manager.Id, alice.ManagerId);
        Assert.Equal(manager.Id, bob.ManagerId);

        var currentYear = DateTime.UtcNow.Year;
        foreach (var employeeId in new[] { manager.Id, alice.Id, bob.Id })
        {
            var policyCount = await db.LeavePolicies.CountAsync(p => p.EmployeeId == employeeId && p.Year == currentYear);
            Assert.Equal(2, policyCount); // Annual + Medical, no duplicates from the second run
        }
    }

    // S2.5-4: DataSeeder.EnsureRolesAsync/EnsureUserAsync have InvalidOperationException throw
    // paths that only trigger when an underlying Identity operation reports failure — something
    // that never happens against a healthy database in the tests above. These tests substitute
    // fake stores (see FailingIdentityStores.cs) to deterministically exercise those paths.
    //
    // Note: EnsurePolicyAsync has no Identity call and no throw path in the current
    // implementation (it only queries/adds a LeavePolicy row via EF Core directly), so there is
    // nothing to cover there; the sprint note grouping it with the other two methods does not
    // match the code as written.

    [Fact]
    public async Task SeedAsync_ThrowsInvalidOperationException_WhenRoleCreationFails()
    {
        await using var services = IdentityServiceProviderFactory.Build(
            fixture.ConnectionString,
            new SeedOptions { HrEmail = "hr-seedtest-rolefail@leaveautopilot.local", HrPassword = "SeedTest123!", HrFullName = "Test HR" },
            configureServices: s => s.AddScoped<IRoleStore<IdentityRole<Guid>>>(_ => new FailingRoleStore()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DataSeeder.SeedAsync(services));
        Assert.Contains("Failed to create role", ex.Message);
    }

    [Fact]
    public async Task SeedAsync_ThrowsInvalidOperationException_WhenUserCreationFails_DueToWeakPassword()
    {
        await using var services = IdentityServiceProviderFactory.Build(
            fixture.ConnectionString,
            new SeedOptions { HrEmail = "hr-seedtest-userfail@leaveautopilot.local", HrPassword = "weak", HrFullName = "Test HR" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DataSeeder.SeedAsync(services));
        Assert.Contains("Failed to seed user", ex.Message);
    }

    [Fact]
    public async Task SeedAsync_ThrowsInvalidOperationException_WhenRoleAssignmentFails()
    {
        const string email = "hr-seedtest-roleassignfail@leaveautopilot.local";

        await using var services = IdentityServiceProviderFactory.Build(
            fixture.ConnectionString,
            new SeedOptions { HrEmail = email, HrPassword = "SeedTest123!", HrFullName = "Test HR" },
            configureServices: s => s.AddScoped<IUserStore<ApplicationUser>>(
                sp => new RoleAssignmentFailingUserStore(sp.GetRequiredService<ApplicationDbContext>())));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DataSeeder.SeedAsync(services));
        Assert.Contains("Failed to assign role", ex.Message);
    }
}
