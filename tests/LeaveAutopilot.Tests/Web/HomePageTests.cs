using LeaveAutopilot.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Web;

/// <summary>
/// End-to-end smoke test (S1-1 AC): the app boots (running migrations/seeding against the
/// configured database, same as Program.cs does at startup) and serves the landing page.
/// Runs in the shared "Database" collection so it never races the constraint/migration
/// tests that drop and recreate the same test database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class HomePageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HomePageTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            var testConnectionString =
                Environment.GetEnvironmentVariable("ConnectionStrings__TestConnection")
                ?? "Host=localhost;Port=5432;Database=leaveapp_test;Username=postgres;Password=postgres";

            builder.UseSetting("ConnectionStrings:DefaultConnection", testConnectionString);
        });
    }

    [Fact]
    public async Task GetHome_ReturnsSuccess_AndRendersLandingPage()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Welcome", body);
    }
}
