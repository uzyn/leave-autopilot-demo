using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LeaveAutopilot.Web.Models.Hr;

/// <summary>Form model for HR resetting another user's password (S2-3).</summary>
public class ResetPasswordViewModel
{
    // Nullable so an empty "-- select a user --" submission binds to null and is caught by
    // [Required] with the intended message, instead of failing at model-binding time with
    // MVC's generic type-conversion error for a non-nullable Guid.
    [Required(ErrorMessage = "Select a user.")]
    [Display(Name = "User")]
    public Guid? UserId { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The new password and confirmation do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Populated by the controller for the user picker; not bound from the form.</summary>
    [BindNever]
    public List<SelectListItem> ActiveUsers { get; set; } = [];
}
