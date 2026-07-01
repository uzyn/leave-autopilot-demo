using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace LeaveAutopilot.Tests.Infrastructure;

/// <summary>
/// A minimal, entirely in-memory <see cref="IRoleStore{TRole}"/> whose <see cref="CreateAsync"/>
/// always fails. Used to deterministically exercise <c>DataSeeder.EnsureRolesAsync</c>'s
/// <see cref="InvalidOperationException"/> throw path, which only fires when an Identity
/// operation reports failure — something that never happens against a healthy real database.
/// </summary>
public sealed class FailingRoleStore : IRoleStore<IdentityRole<Guid>>
{
    public Task<IdentityResult> CreateAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = "SimulatedRoleCreateFailure",
            Description = "Simulated role creation failure.",
        }));

    public Task<IdentityResult> DeleteAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityResult.Success);

    public Task<IdentityRole<Guid>?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
        Task.FromResult<IdentityRole<Guid>?>(null);

    public Task<IdentityRole<Guid>?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) =>
        Task.FromResult<IdentityRole<Guid>?>(null);

    public Task<string?> GetNormalizedRoleNameAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
        Task.FromResult(role.NormalizedName);

    public Task<string> GetRoleIdAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Id.ToString());

    public Task<string?> GetRoleNameAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Name);

    public Task SetNormalizedRoleNameAsync(IdentityRole<Guid> role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetRoleNameAsync(IdentityRole<Guid> role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<IdentityResult> UpdateAsync(IdentityRole<Guid> role, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityResult.Success);

    public void Dispose()
    {
    }
}

/// <summary>
/// A real EF Core-backed user store (so <c>CreateAsync</c> still inserts into the actual test
/// database normally) except <see cref="UpdateAsync"/> always fails. <c>UserManager.AddToRoleAsync</c>
/// adds the role in memory and then calls the store's <c>UpdateAsync</c> to persist it, returning
/// whatever <see cref="IdentityResult"/> that produces — so this deterministically exercises
/// <c>DataSeeder.EnsureUserAsync</c>'s "failed to assign role" <see cref="InvalidOperationException"/>
/// throw path without needing a genuinely broken database.
/// </summary>
public sealed class RoleAssignmentFailingUserStore(ApplicationDbContext context)
    : UserStore<ApplicationUser, IdentityRole<Guid>, ApplicationDbContext, Guid>(context)
{
    public override Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = "SimulatedRoleAssignmentFailure",
            Description = "Simulated failure persisting the user's role assignment.",
        }));
    }
}
