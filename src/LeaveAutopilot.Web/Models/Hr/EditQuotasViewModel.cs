using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LeaveAutopilot.Web.Models.Hr;

/// <summary>
/// Form model for HR setting an employee's Annual/Medical allocated days for a calendar
/// year (S3-3). Unpaid leave has no quota and is not represented here.
/// </summary>
public class EditQuotasViewModel
{
    [BindNever]
    public Guid EmployeeId { get; set; }

    [BindNever]
    public string EmployeeName { get; set; } = string.Empty;

    [BindNever]
    public int Year { get; set; }

    [Required]
    [Range(0, 366, ErrorMessage = "Annual days must be zero or greater.")]
    [Display(Name = "Annual (days)")]
    public decimal AnnualAllocatedDays { get; set; }

    [Required]
    [Range(0, 366, ErrorMessage = "Medical days must be zero or greater.")]
    [Display(Name = "Medical (days)")]
    public decimal MedicalAllocatedDays { get; set; }
}
