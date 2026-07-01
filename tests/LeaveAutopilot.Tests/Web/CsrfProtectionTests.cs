using System.Net;
using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// S2.5-2: every existing form-submission test fetches a valid antiforgery token before
/// posting, so nothing previously verified that a POST with a missing or invalid
/// `__RequestVerificationToken` is actually rejected. These tests close that gap for the
/// three POST actions protected by [ValidateAntiForgeryToken]: /Account/Login,
/// /Account/Logout, and /Hr/ResetPassword.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CsrfProtectionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CsrfProtectionTests(WebApplicationFactory<Program> factory)
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
    public async Task Login_WithMissingAntiForgeryToken_IsRejected()
    {
        var email = $"csrf-login-missing-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee);

        var client = _factory.CreateClient();
        // Visiting the login page first sets a real antiforgery cookie, but the hidden form
        // field is omitted from the POST — the shape of a cross-site forged request.
        await client.GetAsync("/Account/Login");

        var form = new Dictionary<string, string> { ["Email"] = email, ["Password"] = password };
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithInvalidAntiForgeryToken_IsRejected()
    {
        var email = $"csrf-login-invalid-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee);

        var client = _factory.CreateClient();
        await client.GetAsync("/Account/Login");

        var form = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = "not-a-real-token",
        };
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithMissingAntiForgeryToken_IsRejected_AndSessionRemainsSignedIn()
    {
        var email = $"csrf-logout-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee);

        var client = _factory.CreateClient();
        await LoginAsync(client, email, password);

        var response = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var home = await client.GetAsync("/");
        home.EnsureSuccessStatusCode();
        Assert.Contains("Welcome", await home.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HrResetPassword_WithMissingAntiForgeryToken_IsRejected_AndPasswordUnchanged()
    {
        var hrEmail = $"csrf-hr-{Guid.NewGuid():N}@leaveautopilot.local";
        var targetEmail = $"csrf-target-{Guid.NewGuid():N}@leaveautopilot.local";
        const string hrPassword = "HrPass123!";
        const string targetPassword = "TargetOld123!";

        await TestUserFactory.CreateUserAsync(_factory.Services, hrEmail, hrPassword, Roles.Hr);
        var target = await TestUserFactory.CreateUserAsync(_factory.Services, targetEmail, targetPassword, Roles.Employee);

        var client = _factory.CreateClient();
        await LoginAsync(client, hrEmail, hrPassword);
        await client.GetAsync("/Hr/ResetPassword");

        var form = new Dictionary<string, string>
        {
            ["UserId"] = target.Id.ToString(),
            ["NewPassword"] = "Hacked123!",
            ["ConfirmPassword"] = "Hacked123!",
        };
        var response = await client.PostAsync("/Hr/ResetPassword", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var targetClient = _factory.CreateClient();
        var loginResult = await LoginAsync(targetClient, targetEmail, targetPassword);
        Assert.Contains("Welcome", await loginResult.Content.ReadAsStringAsync());
    }
}
