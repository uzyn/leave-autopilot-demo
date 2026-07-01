using LeaveAutopilot.Web.Models;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeaveAutopilot.Web.Data.Seed;

/// <summary>
/// Idempotent startup seed routine (S1-3): ensures the three roles exist, creates the
/// first HR account on first run, and — when enabled — a small set of sample data for
/// local development/demoing. Safe to run on every startup; never overwrites or deletes
/// existing data.
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var options = services.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DataSeeder));

        await EnsureRolesAsync(roleManager, logger);

        var hrUser = await EnsureUserAsync(
            userManager,
            options.HrEmail,
            options.HrFullName,
            options.HrPassword,
            Roles.Hr,
            managerId: null,
            logger);

        if (options.IncludeSampleData)
        {
            await SeedSampleDataAsync(userManager, dbContext, hrUser, logger, cancellationToken);
        }
    }

    private static async Task EnsureRolesAsync(RoleManager<IdentityRole<Guid>> roleManager, ILogger logger)
    {
        foreach (var roleName in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create role '{roleName}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }

            logger.LogInformation("Seed: created role {RoleName}", roleName);
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        string password,
        string role,
        Guid? managerId,
        ILogger logger)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            ManagerId = managerId,
            IsActive = true,
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed user '{email}': {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign role '{role}' to seeded user '{email}': {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
        }

        logger.LogInformation("Seed: created {Role} user {Email}", role, email);
        return user;
    }

    private static async Task SeedSampleDataAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        ApplicationUser hrUser,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        const string samplePassword = "Password123!";

        var manager = await EnsureUserAsync(
            userManager, "manager@leaveautopilot.local", "Morgan Manager", samplePassword, Roles.Manager, managerId: hrUser.Id, logger);

        var employeeOne = await EnsureUserAsync(
            userManager, "alice@leaveautopilot.local", "Alice Employee", samplePassword, Roles.Employee, managerId: manager.Id, logger);

        var employeeTwo = await EnsureUserAsync(
            userManager, "bob@leaveautopilot.local", "Bob Employee", samplePassword, Roles.Employee, managerId: manager.Id, logger);

        var currentYear = DateTime.UtcNow.Year;
        foreach (var employee in new[] { manager, employeeOne, employeeTwo })
        {
            await EnsurePolicyAsync(dbContext, employee.Id, LeaveType.Annual, currentYear, 14m, logger);
            await EnsurePolicyAsync(dbContext, employee.Id, LeaveType.Medical, currentYear, 14m, logger);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsurePolicyAsync(
        ApplicationDbContext dbContext, Guid employeeId, LeaveType leaveType, int year, decimal allocatedDays, ILogger logger)
    {
        var exists = await dbContext.LeavePolicies
            .AnyAsync(p => p.EmployeeId == employeeId && p.LeaveType == leaveType && p.Year == year);

        if (exists)
        {
            return;
        }

        dbContext.LeavePolicies.Add(new LeavePolicy
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveType = leaveType,
            Year = year,
            AllocatedDays = allocatedDays,
        });

        logger.LogInformation(
            "Seed: created {LeaveType} policy for employee {EmployeeId}, year {Year}", leaveType, employeeId, year);
    }
}
