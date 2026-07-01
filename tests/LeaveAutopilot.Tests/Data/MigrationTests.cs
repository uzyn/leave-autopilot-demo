using LeaveAutopilot.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LeaveAutopilot.Tests.Data;

/// <summary>Verifies the EF Core migration applies cleanly to an empty database (S1-2 AC).</summary>
[Collection(DatabaseCollection.Name)]
public class MigrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Migrations_ApplyCleanly_AndLeaveNoPendingMigrations()
    {
        await using var db = fixture.CreateContext();

        Assert.True(await db.Database.CanConnectAsync());

        var pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.Contains("InitialCreate"));
    }
}
