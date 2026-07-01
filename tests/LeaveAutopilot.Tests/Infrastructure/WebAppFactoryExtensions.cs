using Microsoft.AspNetCore.Mvc.Testing;

namespace LeaveAutopilot.Tests.Infrastructure;

/// <summary>Shared setup for tests hosting the app via <see cref="WebApplicationFactory{TEntryPoint}"/> against the dedicated test database.</summary>
public static class WebAppFactoryExtensions
{
    public static WebApplicationFactory<Program> ConfigureTestDatabase(this WebApplicationFactory<Program> factory)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            var testConnectionString =
                Environment.GetEnvironmentVariable("ConnectionStrings__TestConnection")
                ?? "Host=localhost;Port=5432;Database=leaveapp_test;Username=postgres;Password=postgres";

            builder.UseSetting("ConnectionStrings:DefaultConnection", testConnectionString);
        });
    }
}
