namespace LeaveAutopilot.Web.Data.Seed;

/// <summary>
/// Configuration for the startup seed routine, bound from the "Seed" configuration section
/// (appsettings, environment variables, or user-secrets). No credentials are committed —
/// the defaults below are for local development only and must be overridden for any shared
/// or production environment.
/// </summary>
public class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Email of the first HR account created on first run.</summary>
    public string HrEmail { get; set; } = "hr@leaveautopilot.local";

    /// <summary>Initial password for the seeded HR account. Override via configuration/environment for any non-local environment.</summary>
    public string HrPassword { get; set; } = "ChangeMe123!";

    public string HrFullName { get; set; } = "HR Administrator";

    /// <summary>When true, also seeds a sample manager, employees and quotas for local demoing/testing.</summary>
    public bool IncludeSampleData { get; set; } = false;
}
