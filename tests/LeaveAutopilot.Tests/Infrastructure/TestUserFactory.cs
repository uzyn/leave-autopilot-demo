using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveAutopilot.Tests.Infrastructure;

/// <summary>
/// Creates ad-hoc Identity users (with roles) for authentication/authorization tests, going
/// through the same UserManager/RoleManager pipeline as production code (DataSeeder) so
/// password hashing and role assignment behave identically to the real app.
/// </summary>
public static class TestUserFactory
{
    public static async Task<ApplicationUser> CreateUserAsync(
        IServiceProvider rootServices,
        string email,
        string password,
        string role,
        bool isActive = true,
        Guid? managerId = null,
        string? fullName = null)
    {
        using var scope = rootServices.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName ?? email,
            IsActive = isActive,
            ManagerId = managerId,
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create test user '{email}': {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign role '{role}' to test user '{email}': {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
        }

        return user;
    }
}
