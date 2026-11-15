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
        await EnsureSchemaAsync(db, logger);

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

    private static async Task EnsureSchemaAsync(AppDbContext db, ILogger logger)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Projects ADD COLUMN InternalTableName TEXT NULL");
            logger.LogInformation("Added Projects.InternalTableName column");
        }
        catch
        {
            // Column already exists
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS RecordPhotos (
                    Id TEXT NOT NULL PRIMARY KEY,
                    RecordId TEXT NOT NULL,
                    FieldName TEXT NOT NULL,
                    StorageMode TEXT NOT NULL,
                    FilePath TEXT NULL,
                    ContentType TEXT NULL,
                    FileName TEXT NULL,
                    BinaryData BLOB NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (RecordId) REFERENCES InternalRecords(Id) ON DELETE CASCADE
                );
                """);
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_RecordPhotos_RecordId_FieldName ON RecordPhotos (RecordId, FieldName);");
            logger.LogInformation("Ensured RecordPhotos table");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not ensure RecordPhotos table");
        }
    }
}
