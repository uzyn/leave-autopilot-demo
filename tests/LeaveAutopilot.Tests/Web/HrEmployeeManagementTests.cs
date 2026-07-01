using System.Net;
using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// S3-1 acceptance criteria: HR can create, edit, and deactivate/reactivate employee
/// accounts; duplicate email is rejected; every action is HR-only, enforced server-side.
/// S3-2 acceptance criteria (manager assignment) live in <see cref="HrManagerAssignmentTests"/>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class HrEmployeeManagementTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HrEmployeeManagementTests(WebApplicationFactory<Program> factory)
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
        var hrEmail = $"emp-mgmt-hr-{Guid.NewGuid():N}@leaveautopilot.local";
        const string hrPassword = "HrPass123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, hrEmail, hrPassword, Roles.Hr);

        var client = _factory.CreateClient();
        await LoginAsync(client, hrEmail, hrPassword);
        return client;
    }

    [Fact]
    public async Task Hr_CreatesEmployee_WithNameEmailRoleAndPassword_AndTheyCanLogIn()
    {
        var hrClient = await LoginAsHrAsync();
        var newEmail = $"newhire-{Guid.NewGuid():N}@leaveautopilot.local";

        var createPage = await hrClient.GetAsync("/Hr/CreateEmployee");
        var token = await AntiForgeryHelper.ExtractTokenAsync(createPage);

        var form = new Dictionary<string, string>
        {
            ["FullName"] = "New Hire",
            ["Email"] = newEmail,
            ["Role"] = Roles.Employee,
            ["InitialPassword"] = "NewHire123!",
            ["__RequestVerificationToken"] = token,
        };

        var response = await hrClient.PostAsync("/Hr/CreateEmployee", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        Assert.Equal("/Hr/Employees", response.RequestMessage!.RequestUri!.AbsolutePath);
        var listBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("New Hire", listBody);
        Assert.Contains(newEmail, listBody);

        var newHireClient = _factory.CreateClient();
        var loginResult = await LoginAsync(newHireClient, newEmail, "NewHire123!");
        Assert.Contains("Welcome", await loginResult.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Hr_CreatesEmployee_WithDuplicateEmail_IsRejected()
    {
        var hrClient = await LoginAsHrAsync();
        var duplicateEmail = $"dupe-{Guid.NewGuid():N}@leaveautopilot.local";
        await TestUserFactory.CreateUserAsync(_factory.Services, duplicateEmail, "Existing123!", Roles.Employee);

        var createPage = await hrClient.GetAsync("/Hr/CreateEmployee");
        var token = await AntiForgeryHelper.ExtractTokenAsync(createPage);

        var form = new Dictionary<string, string>
        {
            ["FullName"] = "Duplicate Person",
            ["Email"] = duplicateEmail,
            ["Role"] = Roles.Employee,
            ["InitialPassword"] = "AnotherPass123!",
            ["__RequestVerificationToken"] = token,
        };

        var response = await hrClient.PostAsync("/Hr/CreateEmployee", new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Hr/CreateEmployee", response.RequestMessage!.RequestUri!.AbsolutePath);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already taken", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hr_EditsEmployee_NameEmailAndRole()
    {
        var hrClient = await LoginAsHrAsync();
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"edit-me-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee, fullName: "Old Name");

        var editPage = await hrClient.GetAsync($"/Hr/EditEmployee/{employee.Id}");
        var token = await AntiForgeryHelper.ExtractTokenAsync(editPage);

        var newEmail = $"renamed-{Guid.NewGuid():N}@leaveautopilot.local";
        var form = new Dictionary<string, string>
        {
            ["FullName"] = "New Name",
            ["Email"] = newEmail,
            ["Role"] = Roles.Manager,
            ["__RequestVerificationToken"] = token,
        };

        var response = await hrClient.PostAsync($"/Hr/EditEmployee/{employee.Id}", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        Assert.Equal("/Hr/Employees", response.RequestMessage!.RequestUri!.AbsolutePath);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("New Name", body);
        Assert.Contains(newEmail, body);
        Assert.Contains(Roles.Manager, body);

        // The renamed/re-roled employee logs in with the new email.
        var employeeClient = _factory.CreateClient();
        var loginResult = await LoginAsync(employeeClient, newEmail, "Password123!");
        Assert.Contains("Welcome", await loginResult.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Hr_DeactivatesEmployee_TheyCannotLogIn_AndDoNotAppearAsAssignableManager()
    {
        var hrClient = await LoginAsHrAsync();
        var employeeEmail = $"deactivate-me-{Guid.NewGuid():N}@leaveautopilot.local";
        var employee = await TestUserFactory.CreateUserAsync(_factory.Services, employeeEmail, "Password123!", Roles.Employee);
        var other = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"other-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee);

        var deactivatePage = await hrClient.GetAsync("/Hr/Employees");
        var token = await AntiForgeryHelper.ExtractTokenAsync(deactivatePage);

        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        var response = await hrClient.PostAsync($"/Hr/DeactivateEmployee/{employee.Id}", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Deactivated", body);

        var deactivatedClient = _factory.CreateClient();
        var loginResult = await LoginAsync(deactivatedClient, employeeEmail, "Password123!");
        Assert.Contains("Invalid login attempt", await loginResult.Content.ReadAsStringAsync());

        // The deactivated employee should no longer appear in another employee's manager picker.
        var editOtherPage = await hrClient.GetAsync($"/Hr/EditEmployee/{other.Id}");
        var editOtherBody = await editOtherPage.Content.ReadAsStringAsync();
        Assert.DoesNotContain(employee.Id.ToString(), editOtherBody);
    }

    [Fact]
    public async Task Hr_ReactivatesEmployee_TheyCanLogInAgain()
    {
        var hrClient = await LoginAsHrAsync();
        var employeeEmail = $"reactivate-me-{Guid.NewGuid():N}@leaveautopilot.local";
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, employeeEmail, "Password123!", Roles.Employee, isActive: false);

        var employeesPage = await hrClient.GetAsync("/Hr/Employees");
        var token = await AntiForgeryHelper.ExtractTokenAsync(employeesPage);

        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        var response = await hrClient.PostAsync($"/Hr/ReactivateEmployee/{employee.Id}", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        Assert.Contains("Reactivated", await response.Content.ReadAsStringAsync());

        var reactivatedClient = _factory.CreateClient();
        var loginResult = await LoginAsync(reactivatedClient, employeeEmail, "Password123!");
        Assert.Contains("Welcome", await loginResult.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Employee_CannotReach_EmployeeAdminActions()
    {
        var employeeEmail = $"authz-emp-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, employeeEmail, password, Roles.Employee);

        var client = _factory.CreateClient();
        await LoginAsync(client, employeeEmail, password);

        var listResponse = await client.GetAsync("/Hr/Employees");
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);

        var createResponse = await client.GetAsync("/Hr/CreateEmployee");
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    /// <summary>
    /// Covers the write paths of the HR-only enforcement, not just the two GET actions
    /// above: the class-level [Authorize(Roles = Roles.Hr)] should reject every mutating
    /// employee-admin action for a non-HR caller.
    /// </summary>
    [Fact]
    public async Task Employee_CannotReach_EmployeeAdminPostActions()
    {
        var employeeEmail = $"authz-emp-post-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, employeeEmail, password, Roles.Employee);
        var target = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"authz-target-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee);

        var client = _factory.CreateClient();
        await LoginAsync(client, employeeEmail, password);

        // No CSRF token is fetched here: [Authorize] runs (and should reject) before
        // antiforgery validation would even be reached for these POSTs.
        var editResponse = await client.PostAsync(
            $"/Hr/EditEmployee/{target.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["FullName"] = target.FullName,
                ["Email"] = target.Email!,
                ["Role"] = Roles.Employee,
            }));
        Assert.Equal(HttpStatusCode.Forbidden, editResponse.StatusCode);

        var deactivateResponse = await client.PostAsync($"/Hr/DeactivateEmployee/{target.Id}", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Forbidden, deactivateResponse.StatusCode);

        var reactivateResponse = await client.PostAsync($"/Hr/ReactivateEmployee/{target.Id}", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Forbidden, reactivateResponse.StatusCode);

        var quotasResponse = await client.PostAsync(
            $"/Hr/EditQuotas/{target.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AnnualAllocatedDays"] = "10",
                ["MedicalAllocatedDays"] = "5",
            }));
        Assert.Equal(HttpStatusCode.Forbidden, quotasResponse.StatusCode);
    }

    [Fact]
    public async Task Hr_EditingEmployee_ToAnotherUsersEmail_IsRejected()
    {
        var hrClient = await LoginAsHrAsync();
        var existing = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"taken-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee);
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"editme-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee, fullName: "Edit Me");

        var editPage = await hrClient.GetAsync($"/Hr/EditEmployee/{employee.Id}");
        var token = await AntiForgeryHelper.ExtractTokenAsync(editPage);

        var form = new Dictionary<string, string>
        {
            ["FullName"] = employee.FullName,
            ["Email"] = existing.Email!,
            ["Role"] = Roles.Employee,
            ["__RequestVerificationToken"] = token,
        };

        var response = await hrClient.PostAsync($"/Hr/EditEmployee/{employee.Id}", new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        Assert.Equal($"/Hr/EditEmployee/{employee.Id}", response.RequestMessage!.RequestUri!.AbsolutePath);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already in use", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hr_CreatingEmployee_WithInvalidRole_IsRejected_AndNoUserIsCreated()
    {
        var hrClient = await LoginAsHrAsync();
        var newEmail = $"badrole-{Guid.NewGuid():N}@leaveautopilot.local";

        var createPage = await hrClient.GetAsync("/Hr/CreateEmployee");
        var token = await AntiForgeryHelper.ExtractTokenAsync(createPage);

        var form = new Dictionary<string, string>
        {
            ["FullName"] = "Bad Role",
            ["Email"] = newEmail,
            ["Role"] = "SuperAdmin",
            ["InitialPassword"] = "NewHire123!",
            ["__RequestVerificationToken"] = token,
        };

        var response = await hrClient.PostAsync("/Hr/CreateEmployee", new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Hr/CreateEmployee", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Select a valid role", await response.Content.ReadAsStringAsync());

        // No orphaned/roleless user should have been created for the rejected role.
        var loginResult = await LoginAsync(_factory.CreateClient(), newEmail, "NewHire123!");
        Assert.DoesNotContain("Welcome", await loginResult.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Hr_EditingEmployee_WithInvalidRole_IsRejected_AndOriginalRoleIsKept()
    {
        var hrClient = await LoginAsHrAsync();
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"keep-role-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee, fullName: "Keep Role");

        var editPage = await hrClient.GetAsync($"/Hr/EditEmployee/{employee.Id}");
        var token = await AntiForgeryHelper.ExtractTokenAsync(editPage);

        var form = new Dictionary<string, string>
        {
            ["FullName"] = employee.FullName,
            ["Email"] = employee.Email!,
            ["Role"] = "SuperAdmin",
            ["__RequestVerificationToken"] = token,
        };

        var response = await hrClient.PostAsync($"/Hr/EditEmployee/{employee.Id}", new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        Assert.Equal($"/Hr/EditEmployee/{employee.Id}", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Select a valid role", await response.Content.ReadAsStringAsync());

        // The employee should still have their original role, not be roleless.
        var listBody = await (await hrClient.GetAsync("/Hr/Employees")).Content.ReadAsStringAsync();
        Assert.Contains("Keep Role", listBody);
        var loginResult = await LoginAsync(_factory.CreateClient(), employee.Email!, "Password123!");
        Assert.Contains("Welcome", await loginResult.Content.ReadAsStringAsync());
    }
}
