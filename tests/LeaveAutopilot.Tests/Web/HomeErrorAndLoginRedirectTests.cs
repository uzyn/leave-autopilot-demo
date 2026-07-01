using LeaveAutopilot.Tests.Infrastructure;
using LeaveAutopilot.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// S2.5-3 acceptance criteria: an anonymous request to the error page renders the friendly
/// error view instead of being bounced through a login redirect (which would mask the real
/// failure that sent the user here in the first place), and an already-authenticated user
/// hitting the login form is redirected to the home page instead of seeing the form again.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class HomeErrorAndLoginRedirectTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HomeErrorAndLoginRedirectTests(WebApplicationFactory<Program> factory)
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
    public async Task AnonymousRequest_ToErrorPage_RendersFriendlyErrorPage_NotLoginRedirect()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/Home/Error");

        response.EnsureSuccessStatusCode();
        Assert.Equal("/Home/Error", response.RequestMessage!.RequestUri!.AbsolutePath);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("An error occurred while processing your request.", body);
    }

    [Fact]
    public async Task AuthenticatedUser_RequestingLoginPage_IsRedirectedToHome()
    {
        var email = $"loginredirect-{Guid.NewGuid():N}@leaveautopilot.local";
        const string password = "Password123!";
        await TestUserFactory.CreateUserAsync(_factory.Services, email, password, Roles.Employee);

        var client = _factory.CreateClient();
        await LoginAsync(client, email, password);

        var response = await client.GetAsync("/Account/Login");

        response.EnsureSuccessStatusCode();
        Assert.Equal("/", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Welcome", await response.Content.ReadAsStringAsync());
    }
}
