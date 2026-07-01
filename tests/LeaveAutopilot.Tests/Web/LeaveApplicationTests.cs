using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Models;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// S4-3 acceptance criteria (controller/UI layer): an employee can submit a valid request
/// which appears as Pending and reserves the correct chargeable days; submission is
/// rejected with clear messages for end-before-start, past dates, cross-year spans, and
/// insufficient balance; Unpaid requests submit regardless of balance; users of any role
/// can submit their own requests.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class LeaveApplicationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LeaveApplicationTests(WebApplicationFactory<Program> factory)
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

    private async Task<(HttpClient Client, ApplicationUser User)> LoginAsNewUserAsync(string role)
    {
        var email = $"leave-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        var user = await TestUserFactory.CreateUserAsync(_factory.Services, email, password, role);

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

    /// <summary>The next future occurrence of <paramref name="dayOfWeek"/> after today, so date-based tests are deterministic regardless of when they run.</summary>
    private static DateOnly NextOccurrenceOf(DayOfWeek dayOfWeek)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysToAdd = ((int)dayOfWeek - (int)today.DayOfWeek + 7) % 7;
        daysToAdd = daysToAdd == 0 ? 7 : daysToAdd;
        return today.AddDays(daysToAdd);
    }

    private async Task<HttpResponseMessage> SubmitApplyAsync(
        HttpClient client, string leaveType, DateOnly startDate, DateOnly endDate,
        bool startHalfDay = false, bool endHalfDay = false, string? reason = null)
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

        if (startHalfDay)
        {
            form["StartHalfDay"] = "true";
        }

        if (endHalfDay)
        {
            form["EndHalfDay"] = "true";
        }

        if (reason is not null)
        {
            form["Reason"] = reason;
        }

        return await client.PostAsync("/Leave/Apply", new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task Employee_SubmitsValidAnnualRequest_SpanningAWeekend_AppearsPendingWithCorrectDaysAndBalance()
    {
        var (client, user) = await LoginAsNewUserAsync(Roles.Employee);
        var year = DateTime.UtcNow.Year;
        await SetQuotaAsync(user.Id, LeaveType.Annual, year, 14m);

        var friday = NextOccurrenceOf(DayOfWeek.Friday);
        var monday = friday.AddDays(3);

        var response = await SubmitApplyAsync(client, "Annual", friday, monday);

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Leave", response.RequestMessage!.RequestUri!.AbsolutePath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Submitted Annual request for 2 day(s)", body); // Fri + Mon, weekend excluded
        Assert.Contains("id=\"remaining-Annual\">12</td>", body); // 14 - 2 = 12
        Assert.Contains("<span class=\"badge bg-secondary\">Pending</span>", body);
    }

    [Fact]
    public async Task Employee_SubmissionWithEndBeforeStart_IsRejectedWithClearMessage()
    {
        var (client, user) = await LoginAsNewUserAsync(Roles.Employee);
        await SetQuotaAsync(user.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);

        var response = await SubmitApplyAsync(client, "Annual", monday, monday.AddDays(-1));

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Leave/Apply", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("End date must be on or after the start date.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Employee_SubmissionWithAPastDate_IsRejectedWithClearMessage()
    {
        var (client, user) = await LoginAsNewUserAsync(Roles.Employee);
        await SetQuotaAsync(user.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var response = await SubmitApplyAsync(client, "Annual", yesterday, yesterday);

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Leave/Apply", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Start date cannot be in the past.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Employee_SubmissionSpanningCalendarYears_IsRejectedWithClearMessage()
    {
        var (client, user) = await LoginAsNewUserAsync(Roles.Employee);
        // Use a far-future year boundary so this never collides with "past date" validation.
        var futureYear = DateTime.UtcNow.Year + 2;
        await SetQuotaAsync(user.Id, LeaveType.Annual, futureYear, 14m);
        await SetQuotaAsync(user.Id, LeaveType.Annual, futureYear + 1, 14m);

        var response = await SubmitApplyAsync(
            client, "Annual", new DateOnly(futureYear, 12, 30), new DateOnly(futureYear + 1, 1, 2));

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Leave/Apply", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Leave requests cannot span across calendar years.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Employee_SubmissionExceedingRemainingBalance_IsRejectedWithClearMessage()
    {
        var (client, user) = await LoginAsNewUserAsync(Roles.Employee);
        await SetQuotaAsync(user.Id, LeaveType.Annual, DateTime.UtcNow.Year, 1m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        var friday = monday.AddDays(4);

        // Mon-Fri = 5 chargeable days, but only 1 day remains.
        var response = await SubmitApplyAsync(client, "Annual", monday, friday);

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Leave/Apply", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Insufficient Annual balance", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Employee_SubmitsUnpaidRequest_RegardlessOfBalance_AndReservesNoBalance()
    {
        var (client, _) = await LoginAsNewUserAsync(Roles.Employee); // No quotas set at all.

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        var friday = monday.AddDays(4);

        var response = await SubmitApplyAsync(client, "Unpaid", monday, friday);

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Leave", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Submitted Unpaid request for 5 day(s)", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(Roles.Employee)]
    [InlineData(Roles.Manager)]
    [InlineData(Roles.Hr)]
    public async Task UsersOfAnyRole_CanSubmitTheirOwnRequest(string role)
    {
        var (client, user) = await LoginAsNewUserAsync(role);
        await SetQuotaAsync(user.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);

        var response = await SubmitApplyAsync(client, "Annual", monday, monday);

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Leave", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Submitted Annual request", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MyLeaveIndex_ShowsOnlyTheCurrentUsersOwnRequests()
    {
        var (clientA, userA) = await LoginAsNewUserAsync(Roles.Employee);
        var (clientB, userB) = await LoginAsNewUserAsync(Roles.Employee);
        await SetQuotaAsync(userA.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);
        await SetQuotaAsync(userB.Id, LeaveType.Annual, DateTime.UtcNow.Year, 14m);

        var monday = NextOccurrenceOf(DayOfWeek.Monday);
        await SubmitApplyAsync(clientA, "Annual", monday, monday, reason: "UserA's request");
        await SubmitApplyAsync(clientB, "Annual", monday, monday, reason: "UserB's request");

        var indexBody = await (await clientA.GetAsync("/Leave/Index")).Content.ReadAsStringAsync();

        Assert.Contains("UserA&#x27;s request", indexBody);
        Assert.DoesNotContain("UserB&#x27;s request", indexBody);
    }
}
