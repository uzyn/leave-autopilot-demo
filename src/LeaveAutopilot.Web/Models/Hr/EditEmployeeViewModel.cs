using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LeaveAutopilot.Web.Models.Hr;

/// <summary>
/// Form model for HR editing an employee's name/email/role (S3-1) and single assigned
/// manager (S3-2). Combined into one screen since both are edited by HR on the same
/// employee record.
/// </summary>
public class EditEmployeeViewModel
{
    [BindNever]
    public Guid Id { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a role.")]
    [Display(Name = "Role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>Null means no manager assigned (allowed — HR fallback approves, Sprint 5).</summary>
    [Display(Name = "Manager")]
    public Guid? ManagerId { get; set; }

    [BindNever]
    public bool IsActive { get; set; }

    /// <summary>Populated by the controller for the role picker; not bound from the form.</summary>
    [BindNever]
    public List<SelectListItem> RoleOptions { get; set; } = [];

    /// <summary>Populated by the controller for the manager picker (active users, excluding self); not bound from the form.</summary>
    [BindNever]
    public List<SelectListItem> ManagerOptions { get; set; } = [];
}
