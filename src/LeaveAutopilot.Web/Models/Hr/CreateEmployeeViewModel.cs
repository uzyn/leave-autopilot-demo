using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LeaveAutopilot.Web.Models.Hr;

/// <summary>Form model for HR creating a new employee account (S3-1).</summary>
public class CreateEmployeeViewModel
{
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

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Initial password")]
    public string InitialPassword { get; set; } = string.Empty;

    /// <summary>Populated by the controller for the role picker; not bound from the form.</summary>
    [BindNever]
    public List<SelectListItem> RoleOptions { get; set; } = [];
}
