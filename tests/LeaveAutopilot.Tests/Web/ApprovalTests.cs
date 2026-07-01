using System.Net;
using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Models;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// S5-1/S5-2/S5-3 acceptance criteria (controller/UI layer): a manager sees only pending
/// requests from their own reports and cannot act on requests from employees not assigned to
/// them; approve/reject transition balances correctly and persist decision metadata; only
/// Pending requests are decidable; manager-less requests surface to HR and a user with an
/// assigned manager does not appear in the HR fallback queue.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ApprovalTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApprovalTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.ConfigureTestDatabase();
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var token = await AntiForgeryHelper.ExtractTokenAsync(loginPage);

        var form = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        };

        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));
    }

    private async Task<(HttpClient Client, ApplicationUser User)> LoginAsNewUserAsync(string role, Guid? managerId = null)
    {
        var email = $"appr-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        var user = await TestUserFactory.CreateUserAsync(_factory.Services, email, password, role, managerId: managerId);

        var client = _factory.CreateClient();
        await LoginAsync(client, email, password);
        return (client, user);
    }

    private async Task SetQuotaAsync(Guid employeeId, LeaveType leaveType, int year, decimal allocatedDays)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.LeavePolicies.Add(new LeavePolicy { Id = Guid.NewGuid(), EmployeeId = employeeId, LeaveType = leaveType, Year = year, AllocatedDays = allocatedDays });
        await db.SaveChangesAsync();
    }

    private static DateOnly NextOccurrenceOf(DayOfWeek dayOfWeek)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysToAdd = ((int)dayOfWeek - (int)today.DayOfWeek + 7) % 7;
        daysToAdd = daysToAdd == 0 ? 7 : daysToAdd;
        return today.AddDays(daysToAdd);
    }

    private async Task<HttpResponseMessage> SubmitApplyAsync(
        HttpClient client, string leaveType, DateOnly startDate, DateOnly endDate, string? reason = null)
    {
        var applyPage = await client.GetAsync("/Leave/Apply");
        var token = await AntiForgeryHelper.ExtractTokenAsync(applyPage);

        var form = new Dictionary<string, string>
        {
            ["LeaveType"] = leaveType,
            ["StartDate"] = startDate.ToString("yyyy-MM-dd"),
            ["EndDate"] = endDate.ToString("yyyy-MM-dd"),
            ["__RequestVerificationToken"] = token,
        };

        if (reason is not null)
        {
            form["Reason"] = reason;
        }

        return await client.PostAsync("/Leave/Apply", new FormUrlEncodedContent(form));
    }

    private async Task<Guid> GetPendingRequestIdAsync(Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var request = await db.LeaveRequests.SingleAsync(r => r.EmployeeId == employeeId && r.State == LeaveRequestState.Pending);
        return request.Id;
    }

    private async Task<LeaveRequest> GetRequestAsync(Guid requestId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.LeaveRequests.SingleAsync(r => r.Id == requestId);
    }

    private async Task<HttpResponseMessage> DecideAsync(HttpClient client, string action, Guid requestId, string? note = null)
    {
        var queuePage = await client.GetAsync("/Approval/Index");
        var token = await AntiForgeryHelper.ExtractTokenAsync(queuePage);

        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        if (note is not null)
        {
            form["note"] = note;
        }

        return await client.PostAsync($"/Approval/{action}/{requestId}", new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task Manager_SeesOnlyPendingRequestsFromOwnReports()
    {
        var (managerAClient, managerA) = await LoginAsNewUserAsync(Roles.Manager);
        var (managerBClient, managerB) = await LoginAsNewUserAsync(Roles.Manager);
        var (employeeAClient, employeeA) = await LoginAsNewUserAsync(Roles.Employee, managerA.Id);
        var (employeeBClient, employeeB) = await LoginAsNewUserAsync(Roles.Employee, managerB.Id);

        await SetQuotaAsync(employeeA.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);
        await SetQuotaAsync(employeeB.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        await SubmitApplyAsync(employeeAClient, "Annual", monday, monday, reason: "Report of A's request");
        await SubmitApplyAsync(employeeBClient, "Annual", monday, monday, reason: "Report of B's request");

        var managerAQueue = await (await managerAClient.GetAsync("/Approval/Index")).Content.ReadAsStringAsync();

        Assert.Contains("Report of A&#x27;s request", managerAQueue);
        Assert.DoesNotContain("Report of B&#x27;s request", managerAQueue);
        _ = managerBClient; // used only to log in managerB above so employeeB can be assigned to them
    }

    [Fact]
    public async Task Manager_Approves_MovesToApproved_AndBalanceStaysAtReservedLevel()
    {
        var (managerClient, manager) = await LoginAsNewUserAsync(Roles.Manager);
        var (employeeClient, employee) = await LoginAsNewUserAsync(Roles.Employee, manager.Id);
        await SetQuotaAsync(employee.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        var tuesday = monday.AddDays(1);
        await SubmitApplyAsync(employeeClient, "Annual", monday, tuesday); // 2 chargeable days

        var requestId = await GetPendingRequestIdAsync(employee.Id);

        var response = await DecideAsync(managerClient, "Approve", requestId);

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Approval", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Request approved.", await response.Content.ReadAsStringAsync());

        var decided = await GetRequestAsync(requestId);
        Assert.Equal(LeaveRequestState.Approved, decided.State);
        Assert.Equal(manager.Id, decided.DecidedByEmployeeId);
        Assert.NotNull(decided.DecidedAt);

        var employeeIndex = await (await employeeClient.GetAsync("/Leave/Index")).Content.ReadAsStringAsync();
        Assert.Contains("<span class=\"badge bg-secondary\">Approved</span>", employeeIndex);
        Assert.Contains("id=\"remaining-Annual\">12</td>", employeeIndex); // 14 - 2, unchanged from Pending reservation
    }

    [Fact]
    public async Task Manager_Rejects_WithNote_ReleasesBalance_AndRecordsNote()
    {
        var (managerClient, manager) = await LoginAsNewUserAsync(Roles.Manager);
        var (employeeClient, employee) = await LoginAsNewUserAsync(Roles.Employee, manager.Id);
        await SetQuotaAsync(employee.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        var friday = monday.AddDays(4);
        await SubmitApplyAsync(employeeClient, "Annual", monday, friday); // 5 chargeable days

        var requestId = await GetPendingRequestIdAsync(employee.Id);

        var response = await DecideAsync(managerClient, "Reject", requestId, note: "Team is short-staffed that week.");

        response.EnsureSuccessStatusCode();
        Assert.Contains("Request rejected.", await response.Content.ReadAsStringAsync());

        var decided = await GetRequestAsync(requestId);
        Assert.Equal(LeaveRequestState.Rejected, decided.State);
        Assert.Equal("Team is short-staffed that week.", decided.DecisionNote);

        var employeeIndex = await (await employeeClient.GetAsync("/Leave/Index")).Content.ReadAsStringAsync();
        Assert.Contains("<span class=\"badge bg-secondary\">Rejected</span>", employeeIndex);
        Assert.Contains("Team is short-staffed that week.", employeeIndex);
        Assert.Contains("id=\"remaining-Annual\">14</td>", employeeIndex); // fully released
    }

    [Fact]
    public async Task Manager_RejectsWithoutFillingInTheNoteField_PersistsNullNotEmptyString()
    {
        // A real browser submits a blank <input type="text" name="note"> as note="", not an
        // omitted form key. DecideAsync's helper adds the "note" key whenever `note` is
        // non-null (including ""), which mirrors that real submission — unlike the other
        // reject tests here, which pass note: null to mirror an omitted key instead.
        var (managerClient, manager) = await LoginAsNewUserAsync(Roles.Manager);
        var (employeeClient, employee) = await LoginAsNewUserAsync(Roles.Employee, manager.Id);
        await SetQuotaAsync(employee.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        await SubmitApplyAsync(employeeClient, "Annual", monday, monday);
        var requestId = await GetPendingRequestIdAsync(employee.Id);

        var response = await DecideAsync(managerClient, "Reject", requestId, note: "");

        response.EnsureSuccessStatusCode();
        var decided = await GetRequestAsync(requestId);
        Assert.Equal(LeaveRequestState.Rejected, decided.State);
        Assert.Null(decided.DecisionNote);

        var employeeIndex = await (await employeeClient.GetAsync("/Leave/Index")).Content.ReadAsStringAsync();
        // Razor HTML-encodes the em-dash placeholder to a numeric character reference (as it
        // does for the apostrophe elsewhere in this file, e.g. "&#x27;").
        Assert.Contains($"id=\"decision-note-{requestId}\">&#x2014;<", employeeIndex);
    }

    [Fact]
    public async Task ApprovalsNavLink_ShownToManagersAndHr_HiddenFromEmployeesWithNoReports()
    {
        var (managerClient, manager) = await LoginAsNewUserAsync(Roles.Manager);
        var (employeeClient, _) = await LoginAsNewUserAsync(Roles.Employee, managerId: null);
        var (hrClient, _) = await LoginAsNewUserAsync(Roles.Hr);

        // manager has no reports yet — should not see the link until they do.
        var managerHomeBefore = await (await managerClient.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Approvals</a>", managerHomeBefore);

        await LoginAsNewUserAsync(Roles.Employee, manager.Id); // give the manager a report

        var managerHomeAfter = await (await managerClient.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("Approvals</a>", managerHomeAfter);

        var employeeHome = await (await employeeClient.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Approvals</a>", employeeHome);

        var hrHome = await (await hrClient.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("Approvals</a>", hrHome);
    }

    [Fact]
    public async Task Manager_CannotActOnARequestFromAnEmployeeNotAssignedToThem()
    {
        var (managerAClient, managerA) = await LoginAsNewUserAsync(Roles.Manager);
        var (_, managerB) = await LoginAsNewUserAsync(Roles.Manager);
        var (employeeBClient, employeeB) = await LoginAsNewUserAsync(Roles.Employee, managerB.Id);
        await SetQuotaAsync(employeeB.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        await SubmitApplyAsync(employeeBClient, "Annual", monday, monday);
        var requestId = await GetPendingRequestIdAsync(employeeB.Id);

        var response = await DecideAsync(managerAClient, "Approve", requestId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var unchanged = await GetRequestAsync(requestId);
        Assert.Equal(LeaveRequestState.Pending, unchanged.State);
    }

    [Fact]
    public async Task AlreadyDecidedRequest_CannotBeReDecided()
    {
        var (managerClient, manager) = await LoginAsNewUserAsync(Roles.Manager);
        var (employeeClient, employee) = await LoginAsNewUserAsync(Roles.Employee, manager.Id);
        await SetQuotaAsync(employee.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        await SubmitApplyAsync(employeeClient, "Annual", monday, monday);
        var requestId = await GetPendingRequestIdAsync(employee.Id);

        var firstDecision = await DecideAsync(managerClient, "Approve", requestId);
        firstDecision.EnsureSuccessStatusCode();

        var secondDecision = await DecideAsync(managerClient, "Reject", requestId, note: "too late");

        secondDecision.EnsureSuccessStatusCode();
        Assert.Contains("already Approved and cannot be re-decided", await secondDecision.Content.ReadAsStringAsync());

        var final = await GetRequestAsync(requestId);
        Assert.Equal(LeaveRequestState.Approved, final.State); // unchanged by the rejected second attempt
    }

    [Fact]
    public async Task ManagerLessEmployeesRequest_SurfacesToHr_AndHrCanDecideIt()
    {
        var (hrClient, hr) = await LoginAsNewUserAsync(Roles.Hr);
        var (employeeClient, employee) = await LoginAsNewUserAsync(Roles.Employee, managerId: null);
        await SetQuotaAsync(employee.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        await SubmitApplyAsync(employeeClient, "Annual", monday, monday, reason: "Manager-less applicant's request");
        var requestId = await GetPendingRequestIdAsync(employee.Id);

        var hrQueuePage = await (await hrClient.GetAsync("/Approval/Index")).Content.ReadAsStringAsync();
        Assert.Contains("Manager-less applicant&#x27;s request", hrQueuePage);

        var response = await DecideAsync(hrClient, "Approve", requestId);

        response.EnsureSuccessStatusCode();
        var decided = await GetRequestAsync(requestId);
        Assert.Equal(LeaveRequestState.Approved, decided.State);
        Assert.Equal(hr.Id, decided.DecidedByEmployeeId);
        _ = hr;
    }

    [Fact]
    public async Task EmployeeWithAnAssignedManager_DoesNotAppearInHrFallbackQueue()
    {
        var (hrClient, _) = await LoginAsNewUserAsync(Roles.Hr);
        var (managerClient, manager) = await LoginAsNewUserAsync(Roles.Manager);
        var (employeeClient, employee) = await LoginAsNewUserAsync(Roles.Employee, manager.Id);
        await SetQuotaAsync(employee.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        await SubmitApplyAsync(employeeClient, "Annual", monday, monday, reason: "Managed applicant's request");

        var hrQueuePage = await (await hrClient.GetAsync("/Approval/Index")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Managed applicant&#x27;s request", hrQueuePage);
        _ = managerClient;
    }
}
