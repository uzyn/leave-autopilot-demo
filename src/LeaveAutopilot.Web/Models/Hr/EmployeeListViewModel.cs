namespace LeaveAutopilot.Web.Models.Hr;

/// <summary>Row shown on the HR employee list (S3-1/S3-2).</summary>
public class EmployeeListItemViewModel
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string? ManagerName { get; set; }

    public bool IsActive { get; set; }
}
