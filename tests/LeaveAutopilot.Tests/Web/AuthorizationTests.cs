using System.Net;
using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// S2-2 acceptance criteria: authentication and role enforcement apply server-side to
/// every action (via the app-wide fallback authorization policy plus per-role attributes),
/// and navigation adapts to the signed-in user's role.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class AuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthorizationTests(WebApplicationFactory<Program> factory)
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
    public async Task AnonymousRequest_ToProtectedPage_RedirectsToLogin()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Account/Login", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Log in", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Employee_DirectlyRequestingHrOnlyAction_IsForbidden()
    {
        var email = $"authz-employee-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee);

        var client = _factory.CreateClient();
        await LoginAsync(client, email, password);

        var response = await client.GetAsync("/Hr/ResetPassword");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrUser_CanReach_HrOnlyAction()
    {
        var email = $"authz-hr-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Hr);

        var client = _factory.CreateClient();
        await LoginAsync(client, email, password);

        var response = await client.GetAsync("/Hr/ResetPassword");

        response.EnsureSuccessStatusCode();
        Assert.Contains("Reset a user's password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Navigation_ShowsHrLink_OnlyForHrRole()
    {
        var hrEmail = $"authz-nav-hr-{Guid.NewGuid():N}@leaveautopilot.local";
        var employeeEmail = $"authz-nav-emp-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, hrEmail, password, Roles.Hr);
        await TestUserFactory.CreateUserAsync(_factory.Services, employeeEmail, password, Roles.Employee);

        var hrClient = _factory.CreateClient();
        var hrHome = await LoginAsync(hrClient, hrEmail, password);
        Assert.Contains("Reset a password", await hrHome.Content.ReadAsStringAsync());

        var employeeClient = _factory.CreateClient();
        var employeeHome = await LoginAsync(employeeClient, employeeEmail, password);
        Assert.DoesNotContain("Reset a password", await employeeHome.Content.ReadAsStringAsync());
    }
}
