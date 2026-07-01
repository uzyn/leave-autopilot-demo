using System.ComponentModel.DataAnnotations;

namespace LeaveAutopilot.Web.Models.Account;

/// <summary>Form model for the email/password login screen (S2-1).</summary>
public class LoginViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
