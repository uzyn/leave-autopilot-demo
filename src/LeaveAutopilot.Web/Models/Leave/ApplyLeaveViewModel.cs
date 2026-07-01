using System.ComponentModel.DataAnnotations;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LeaveAutopilot.Web.Models.Leave;

/// <summary>
/// Form model for an employee submitting a leave request (S4-3). Fields are nullable so a
/// blank/missing selection binds to null and is caught by [Required] with the intended
/// message, rather than failing at model-binding time with MVC's generic type-conversion
/// error (same pattern as EditQuotasViewModel, S2.5-1).
/// </summary>
public class ApplyLeaveViewModel
{
    [Required(ErrorMessage = "Select a leave type.")]
    [Display(Name = "Leave type")]
    public LeaveType? LeaveType { get; set; }

    [Required(ErrorMessage = "Start date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Start date")]
    public DateOnly? StartDate { get; set; }

    [Required(ErrorMessage = "End date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "End date")]
    public DateOnly? EndDate { get; set; }

    [Display(Name = "Half day (start date)")]
    public bool StartHalfDay { get; set; }

    [Display(Name = "Half day (end date)")]
    public bool EndHalfDay { get; set; }

    [StringLength(1000, ErrorMessage = "Reason must be 1000 characters or fewer.")]
    public string? Reason { get; set; }

    [BindNever]
    public List<SelectListItem> LeaveTypeOptions { get; set; } = [];
}
