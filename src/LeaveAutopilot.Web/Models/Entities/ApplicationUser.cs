using Microsoft.AspNetCore.Identity;

namespace LeaveAutopilot.Web.Models.Entities;

/// <summary>
/// A user of the system. Every user is also an employee: they have their own leave
/// balances and may submit their own requests, which route to their assigned manager.
/// Roles (Employee/Manager/HR) are modelled via ASP.NET Core Identity roles rather than
/// a redundant column here, so authorization can rely on Identity's role store directly.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Display name, e.g. "Jane Tan".</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>The single assigned manager who approves this employee's requests. Null means no manager (HR fallback approves).</summary>
    public Guid? ManagerId { get; set; }

    public ApplicationUser? Manager { get; set; }

    /// <summary>Deactivated users cannot log in and are not selectable as a manager.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
