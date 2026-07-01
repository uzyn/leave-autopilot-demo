namespace LeaveAutopilot.Web.Models;

/// <summary>The three roles supported by the system (PRD FR-2). Backed by ASP.NET Core Identity roles.</summary>
public static class Roles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string Hr = "HR";

    public static readonly string[] All = [Employee, Manager, Hr];
}
