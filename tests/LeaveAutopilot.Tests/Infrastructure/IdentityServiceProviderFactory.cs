using LeaveAutopilot.Web.Data;
using LeaveAutopilot.Web.Data.Seed;
using LeaveAutopilot.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeaveAutopilot.Tests.Infrastructure;

/// <summary>Builds a minimal DI container mirroring Program.cs's Identity/EF wiring, for testing DataSeeder in isolation.</summary>
public static class IdentityServiceProviderFactory
{
    public static ServiceProvider Build(string connectionString, SeedOptions? seedOptions = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug());
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<SeedOptions>(o =>
        {
            var effective = seedOptions ?? new SeedOptions();
            o.HrEmail = effective.HrEmail;
            o.HrPassword = effective.HrPassword;
            o.HrFullName = effective.HrFullName;
            o.IncludeSampleData = effective.IncludeSampleData;
        });

        return services.BuildServiceProvider();
    }
}
