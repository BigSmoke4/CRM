using Crm.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Crm.Web.Services;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<ApplicationDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();

        // Apply migrations (creates DB on first run)
        await db.Database.MigrateAsync();

        // Seed roles
        var roles = new[] { "SuperAdmin" }.Concat(RolePermissions.AllRoles).ToArray();
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        logger.LogInformation("Database initialized: {Db}", db.Database.GetDbConnection().Database);
    }
}
