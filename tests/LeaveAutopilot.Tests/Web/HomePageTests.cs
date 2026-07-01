using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// End-to-end smoke test (S1-1 AC): the app boots (running migrations/seeding against the
/// configured database, same as Program.cs does at startup) and serves the landing page.
/// Since Sprint 2 (S2-2), the landing page requires authentication — an anonymous request
/// is redirected to login (covered by AuthorizationTests); this test verifies the
/// authenticated path still renders successfully.
/// Runs in the shared "Database" collection so it never races the constraint/migration
/// tests that drop and recreate the same test database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class HomePageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HomePageTests(WebApplicationFactory<Program> factory)
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
    public async Task GetHome_WhenAuthenticated_ReturnsSuccess_AndRendersLandingPage()
    {
        var email = $"home-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee);

        var client = _factory.CreateClient();
        var response = await LoginAsync(client, email, password);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Welcome", body);
    }

    // S2.5-1: no test previously covered the Manager-role branch of Home/Index.cshtml
    // (`else if (User.IsInRole(Roles.Manager))`) — only the HR and default/Employee branches
    // were exercised.
    [Fact]
    public async Task GetHome_ForManager_RendersManagerBranch()
    {
        var email = $"home-manager-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Manager);

        var client = _factory.CreateClient();
        var response = await LoginAsync(client, email, password);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("You're signed in as a <strong>Manager</strong>.", body);
        Assert.DoesNotContain("signed in as <strong>HR</strong>", body);
        Assert.DoesNotContain("signed in as an <strong>Employee</strong>", body);
    }
}
