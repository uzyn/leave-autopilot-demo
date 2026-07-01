using System.Net;
using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// S3-3 acceptance criteria: HR can set/edit an employee's Annual and Medical allocated
/// days for the current year; allocated days must be non-negative; edits are reflected
/// immediately; the action is HR-only.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class HrQuotaManagementTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HrQuotaManagementTests(WebApplicationFactory<Program> factory)
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
        var hrEmail = $"quota-hr-{Guid.NewGuid():N}@leaveautopilot.local";
        const string hrPassword = "HrPass123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, hrEmail, hrPassword, Roles.Hr);

        var client = _factory.CreateClient();
        await LoginAsync(client, hrEmail, hrPassword);
        return client;
    }

    [Fact]
    public async Task Hr_SetsAnnualAndMedicalQuotas_AndTheyAreReflectedImmediately()
    {
        var hrClient = await LoginAsHrAsync();
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"quota-emp-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee);

        var editPage = await hrClient.GetAsync($"/Hr/EditQuotas/{employee.Id}");
        var token = await AntiForgeryHelper.ExtractTokenAsync(editPage);

        var form = new Dictionary<string, string>
        {
            ["AnnualAllocatedDays"] = "18",
            ["MedicalAllocatedDays"] = "12.5",
            ["__RequestVerificationToken"] = token,
        };

        var response = await hrClient.PostAsync($"/Hr/EditQuotas/{employee.Id}", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("value=\"18.0\"", body);
        Assert.Contains("value=\"12.5\"", body);

        // Reload the page fresh to confirm the values were persisted, not just echoed from the POST.
        var reloadedPage = await hrClient.GetAsync($"/Hr/EditQuotas/{employee.Id}");
        var reloadedBody = await reloadedPage.Content.ReadAsStringAsync();
        Assert.Contains("value=\"18.0\"", reloadedBody);
        Assert.Contains("value=\"12.5\"", reloadedBody);
    }

    [Fact]
    public async Task Hr_EditingAnExistingQuota_UpdatesItRatherThanDuplicating()
    {
        var hrClient = await LoginAsHrAsync();
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"quota-update-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee);

        async Task SetQuotasAsync(string annual, string medical)
        {
            var page = await hrClient.GetAsync($"/Hr/EditQuotas/{employee.Id}");
            var token = await AntiForgeryHelper.ExtractTokenAsync(page);
            var form = new Dictionary<string, string>
            {
                ["AnnualAllocatedDays"] = annual,
                ["MedicalAllocatedDays"] = medical,
                ["__RequestVerificationToken"] = token,
            };
            var response = await hrClient.PostAsync($"/Hr/EditQuotas/{employee.Id}", new FormUrlEncodedContent(form));
            response.EnsureSuccessStatusCode();
        }

        await SetQuotasAsync("10", "5");
        await SetQuotasAsync("20", "8");

        var finalPage = await hrClient.GetAsync($"/Hr/EditQuotas/{employee.Id}");
        var finalBody = await finalPage.Content.ReadAsStringAsync();
        Assert.Contains("value=\"20.0\"", finalBody);
        Assert.Contains("value=\"8.0\"", finalBody);
        Assert.DoesNotContain("value=\"10.0\"", finalBody);
    }

    [Fact]
    public async Task Hr_SettingNegativeQuota_IsRejected()
    {
        var hrClient = await LoginAsHrAsync();
        var employee = await TestUserFactory.CreateUserAsync(
            _factory.Services, $"quota-neg-{Guid.NewGuid():N}@leaveautopilot.local", "Password123!", Roles.Employee);

        var editPage = await hrClient.GetAsync($"/Hr/EditQuotas/{employee.Id}");
        var token = await AntiForgeryHelper.ExtractTokenAsync(editPage);

        var form = new Dictionary<string, string>
        {
            ["AnnualAllocatedDays"] = "-1",
            ["MedicalAllocatedDays"] = "5",
            ["__RequestVerificationToken"] = token,
        };

        var response = await hrClient.PostAsync($"/Hr/EditQuotas/{employee.Id}", new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("must be zero or greater", body);
    }

    [Fact]
    public async Task Employee_CannotReach_QuotaManagement()
    {
        var employeeEmail = $"quota-authz-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        var employee = await TestUserFactory.CreateUserAsync(_factory.Services, employeeEmail, password, Roles.Employee);

        var client = _factory.CreateClient();
        await LoginAsync(client, employeeEmail, password);

        var response = await client.GetAsync($"/Hr/EditQuotas/{employee.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
