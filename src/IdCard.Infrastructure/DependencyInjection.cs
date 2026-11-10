using IdCard.Infrastructure.Persistence;
using IdCard.Infrastructure.Security;
using IdCard.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdCard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdCardInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AppDb")
            ?? "Data Source=idcard.db";

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

        var keysPath = Path.Combine(AppContext.BaseDirectory, "dataprotection-keys");
        Directory.CreateDirectory(keysPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("CardManagement");

        services.AddScoped<ISecretProtector, SecretProtector>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ISettingsService, SettingsService>();

        return services;
    }
}
