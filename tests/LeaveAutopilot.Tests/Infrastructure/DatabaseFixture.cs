using LeaveAutopilot.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace LeaveAutopilot.Tests.Infrastructure;

/// <summary>
/// Shared fixture for tests that need a real Postgres database (migrations, constraints,
/// seeding). Uses a dedicated "leaveapp_test" database — separate from the dev database —
/// so running the test suite never touches local dev/demo data. The database is dropped
/// and recreated once per test collection run.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ConnectionStrings__TestConnection")
        ?? "Host=localhost;Port=5432;Database=leaveapp_test;Username=postgres;Password=postgres";

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
