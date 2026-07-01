using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// S2-1 acceptance criteria: email/password login and logout, password hashing, and
/// rejection of deactivated accounts.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class AuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly DatabaseFixture _dbFixture;

    public AuthenticationTests(WebApplicationFactory<Program> factory, DatabaseFixture dbFixture)
    {
        _factory = factory.ConfigureTestDatabase();
        _dbFixture = dbFixture;
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        loginPage.EnsureSuccessStatusCode();
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
    public async Task Login_WithValidCredentials_Succeeds_AndRendersAuthenticatedHomePage()
    {
        var email = $"auth-valid-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee);

        var client = _factory.CreateClient();
        var response = await LoginAsync(client, email, password);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Welcome", body);
        Assert.Contains(email, body);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsGenericError_AndDoesNotAuthenticate()
    {
        var email = $"auth-wrongpw-{Guid.NewGuid():N}@leaveautopilot.local";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, "CorrectPass123!", Roles.Employee);

        var client = _factory.CreateClient();
        var response = await LoginAsync(client, email, "WrongPass123!");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt", body);
        Assert.DoesNotContain("Welcome,", body);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ShowsSameGenericError()
    {
        var client = _factory.CreateClient();
        var response = await LoginAsync(client, $"nobody-{Guid.NewGuid():N}@leaveautopilot.local", "WhateverPass123!");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt", body);
    }

    [Fact]
    public async Task Login_ForDeactivatedUser_IsRejected()
    {
        var email = $"auth-deactivated-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee, isActive: false);

        var client = _factory.CreateClient();
        var response = await LoginAsync(client, email, password);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt", body);
        Assert.DoesNotContain("Welcome,", body);
    }

    [Fact]
    public async Task Password_IsPersisted_AsSecureHash_NotPlaintext()
    {
        var email = $"auth-hash-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        var user = await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee);

        await using var db = _dbFixture.CreateContext();
        var stored = await db.Users.SingleAsync(u => u.Id == user.Id);

        Assert.NotNull(stored.PasswordHash);
        Assert.NotEqual(password, stored.PasswordHash);
        Assert.DoesNotContain(password, stored.PasswordHash!);
    }

    [Fact]
    public async Task Logout_InvalidatesSession_SoProtectedPageRequiresLoginAgain()
    {
        var email = $"auth-logout-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee);

        var client = _factory.CreateClient();
        await LoginAsync(client, email, password);

        var homeWhileSignedIn = await client.GetAsync("/");
        homeWhileSignedIn.EnsureSuccessStatusCode();
        Assert.Contains("Welcome", await homeWhileSignedIn.Content.ReadAsStringAsync());

        var logoutToken = await AntiForgeryHelper.ExtractTokenAsync(homeWhileSignedIn);
        var logoutResponse = await client.PostAsync(
            "/Account/Logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = logoutToken }));
        logoutResponse.EnsureSuccessStatusCode();

        var homeAfterLogout = await client.GetAsync("/");
        homeAfterLogout.EnsureSuccessStatusCode();
        var body = await homeAfterLogout.Content.ReadAsStringAsync();
        Assert.Contains("Log in", body);
        Assert.DoesNotContain("Welcome,", body);
        Assert.Equal("/Account/Login", homeAfterLogout.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
