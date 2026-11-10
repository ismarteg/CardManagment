using IdCard.Domain.Entities;
using IdCard.Domain.Enums;
using IdCard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdCard.Infrastructure.Services;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        await db.Database.EnsureCreatedAsync();

        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new AppUser
            {
                UserName = "admin",
                Email = "admin@local",
                DisplayName = "Administrator",
                Role = UserRole.Admin,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123")
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded default admin user (admin / Admin@123)");
        }

        if (!await db.Settings.AnyAsync(x => x.Key == SettingsService.SsoEnabled))
        {
            db.Settings.Add(new AppSetting { Key = SettingsService.SsoEnabled, Value = "false", IsSecret = false });
            db.Settings.Add(new AppSetting { Key = SettingsService.SsoTenantId, Value = "", IsSecret = false });
            db.Settings.Add(new AppSetting { Key = SettingsService.SsoClientId, Value = "", IsSecret = false });
            db.Settings.Add(new AppSetting { Key = SettingsService.SsoRedirectUri, Value = "http://localhost:4200/auth/microsoft/callback", IsSecret = false });
            await db.SaveChangesAsync();
        }
    }
}
