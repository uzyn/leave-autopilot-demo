using System.Net;
using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Web;

/// <summary>S2-3 acceptance criteria: HR can reset another active user's password; the change takes effect immediately and is HR-only, server-side.</summary>
[Collection(DatabaseCollection.Name)]
public class HrPasswordResetTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HrPasswordResetTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task Hr_ResetsAnotherActiveUsersPassword_NewPasswordWorks_OldPasswordFails()
    {
        var hrEmail = $"hrreset-hr-{Guid.NewGuid():N}@leaveautopilot.local";
        var targetEmail = $"hrreset-target-{Guid.NewGuid():N}@leaveautopilot.local";
        const string hrPassword = "HrPass123!";
        const string oldPassword = "OldPass123!";
        const string newPassword = "NewPass456!";

        await TestUserFactory.CreateUserAsync(_factory.Services, hrEmail, hrPassword, Roles.Hr);
        var target = await TestUserFactory.CreateUserAsync(_factory.Services, targetEmail, oldPassword, Roles.Employee);

        var hrClient = _factory.CreateClient();
        await LoginAsync(hrClient, hrEmail, hrPassword);

        var resetPage = await hrClient.GetAsync("/Hr/ResetPassword");
        var token = await AntiForgeryHelper.ExtractTokenAsync(resetPage);

        var form = new Dictionary<string, string>
        {
            ["UserId"] = target.Id.ToString(),
            ["NewPassword"] = newPassword,
            ["ConfirmPassword"] = newPassword,
            ["__RequestVerificationToken"] = token,
        };

        var resetResponse = await hrClient.PostAsync("/Hr/ResetPassword", new FormUrlEncodedContent(form));
        resetResponse.EnsureSuccessStatusCode();
        Assert.Contains($"Password reset for {targetEmail}", await resetResponse.Content.ReadAsStringAsync());

        var oldPasswordClient = _factory.CreateClient();
        var oldLoginResult = await LoginAsync(oldPasswordClient, targetEmail, oldPassword);
        Assert.Contains("Invalid login attempt", await oldLoginResult.Content.ReadAsStringAsync());

        var newPasswordClient = _factory.CreateClient();
        var newLoginResult = await LoginAsync(newPasswordClient, targetEmail, newPassword);
        Assert.Contains("Welcome", await newLoginResult.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Employee_PostingDirectlyToResetPassword_IsForbidden_AndTargetPasswordUnchanged()
    {
        var employeeEmail = $"hrreset-emp-{Guid.NewGuid():N}@leaveautopilot.local";
        var targetEmail = $"hrreset-victim-{Guid.NewGuid():N}@leaveautopilot.local";
        const string employeePassword = "EmpPass123!";
        const string targetOldPassword = "TargetOld123!";
        const string attemptedNewPassword = "Hacked123!";

        await TestUserFactory.CreateUserAsync(_factory.Services, employeeEmail, employeePassword, Roles.Employee);
        var target = await TestUserFactory.CreateUserAsync(_factory.Services, targetEmail, targetOldPassword, Roles.Employee);

        var client = _factory.CreateClient();
        await LoginAsync(client, employeeEmail, employeePassword);

        // Grab a valid antiforgery token from any page rendered in this authenticated session.
        var home = await client.GetAsync("/");
        var token = await AntiForgeryHelper.ExtractTokenAsync(home);

        var form = new Dictionary<string, string>
        {
            ["UserId"] = target.Id.ToString(),
            ["NewPassword"] = attemptedNewPassword,
            ["ConfirmPassword"] = attemptedNewPassword,
            ["__RequestVerificationToken"] = token,
        };

        var response = await client.PostAsync("/Hr/ResetPassword", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var targetClient = _factory.CreateClient();
        var loginResult = await LoginAsync(targetClient, targetEmail, targetOldPassword);
        Assert.Contains("Welcome", await loginResult.Content.ReadAsStringAsync());
    }
}
