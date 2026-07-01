using System.Text.RegularExpressions;
using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// S3-2 acceptance criteria: HR can set/change an employee's single assigned manager from
/// the list of active users; no-manager is allowed; self-assignment is prevented;
/// deactivated users are not selectable as managers.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class HrManagerAssignmentTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HrManagerAssignmentTests(WebApplicationFactory<Program> factory)
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

    private async Task<HttpClient> LoginAsHrAsync()
    {
        var hrEmail = $"mgr-assign-hr-{Guid.NewGuid():N}@leaveautopilot.local";
        const string hrPassword = "HrPass123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, hrEmail, hrPassword, Roles.Hr);

        var client = _factory.CreateClient();
        await LoginAsync(client, hrEmail, hrPassword);
        return client;
    }

    private async Task<HttpResponseMessage> SubmitEditAsync(
        HttpClient hrClient, Guid employeeId, string fullName, string email, string role, Guid? managerId)
    {
        var editPage = await hrClient.GetAsync($"/Hr/EditEmployee/{employeeId}");
        var token = await AntiForgeryHelper.ExtractTokenAsync(editPage);

        var form = new Dictionary<string, string>
        {
            ["FullName"] = fullName,
            ["Email"] = email,
            ["Role"] = role,
            ["__RequestVerificationToken"] = token,
        };

        if (managerId.HasValue)
        {
            form["ManagerId"] = managerId.Value.ToString();
        }

        return await hrClient.PostAsync($"/Hr/EditEmployee/{employeeId}", new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task Hr_SetsEmployeesManager_FromActiveUsers()
    {
        var hrClient = await LoginAsHrAsync();
        var manager = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"manager-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Manager, fullName: "Morgan Manager");
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"report-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee, fullName: "Reese Report");

        var response = await SubmitEditAsync(hrClient, employee.Id, employee.FullName, employee.Email!, Roles.Employee, manager.Id);
        response.EnsureSuccessStatusCode();

        var listBody = await (await hrClient.GetAsync("/Hr/Employees")).Content.ReadAsStringAsync();
        Assert.Contains("Morgan Manager", listBody);

        var editPage = await hrClient.GetAsync($"/Hr/EditEmployee/{employee.Id}");
        var editBody = await editPage.Content.ReadAsStringAsync();
        Assert.True(
            OptionIsSelected(editBody, manager.Id.ToString()),
            $"Expected the manager option for {manager.Id} to be selected in:\n{editBody}");
    }

    /// <summary>
    /// Checks that an `<option>` for the given value is marked selected, tolerant of
    /// attribute order (the SelectTagHelper doesn't guarantee `value` before `selected`).
    /// </summary>
    private static bool OptionIsSelected(string html, string optionValue)
    {
        var pattern =
            $"<option(?=[^>]*value=\"{Regex.Escape(optionValue)}\")(?=[^>]*selected)[^>]*>";
        return Regex.IsMatch(html, pattern);
    }

    [Fact]
    public async Task Hr_LeavesEmployeeWithNoManager_IsAllowed()
    {
        var hrClient = await LoginAsHrAsync();
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"nomanager-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee);

        var response = await SubmitEditAsync(hrClient, employee.Id, employee.FullName, employee.Email!, Roles.Employee, managerId: null);

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Hr/Employees", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Hr_AssigningEmployeeAsTheirOwnManager_IsRejected()
    {
        var hrClient = await LoginAsHrAsync();
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"self-manage-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee);

        var response = await SubmitEditAsync(hrClient, employee.Id, employee.FullName, employee.Email!, Roles.Employee, employee.Id);

        response.EnsureSuccessStatusCode();
        Assert.Equal($"/Hr/EditEmployee/{employee.Id}", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("cannot be their own manager", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Hr_AssigningADeactivatedUserAsManager_IsRejected()
    {
        var hrClient = await LoginAsHrAsync();
        var deactivatedManager = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"inactive-mgr-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Manager, isActive: false);
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"report-of-inactive-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee);

        var response = await SubmitEditAsync(hrClient, employee.Id, employee.FullName, employee.Email!, Roles.Employee, deactivatedManager.Id);

        response.EnsureSuccessStatusCode();
        Assert.Equal($"/Hr/EditEmployee/{employee.Id}", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Select an active manager", await response.Content.ReadAsStringAsync());
    }
}
