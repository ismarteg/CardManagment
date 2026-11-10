using IdCard.Domain.Entities;
using IdCard.Infrastructure.Persistence;
using IdCard.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace IdCard.Infrastructure.Services;

public interface ISettingsService
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, bool isSecret, CancellationToken ct = default);
    Task<Dictionary<string, string?>> GetPublicSettingsAsync(CancellationToken ct = default);
    Task<SsoSettings> GetSsoSettingsAsync(CancellationToken ct = default);
    Task SaveSsoSettingsAsync(SsoSettings settings, string? clientSecretPlain, CancellationToken ct = default);
}

public class SsoSettings
{
    public bool Enabled { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public bool HasClientSecret { get; set; }
    public string? RedirectUri { get; set; }
}

public class SettingsService : ISettingsService
{
    public const string SsoEnabled = "Sso.Enabled";
    public const string SsoTenantId = "Sso.TenantId";
    public const string SsoClientId = "Sso.ClientId";
    public const string SsoClientSecret = "Sso.ClientSecret";
    public const string SsoRedirectUri = "Sso.RedirectUri";

    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;

    public SettingsService(AppDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var setting = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        if (setting is null) return null;
        return setting.IsSecret ? _protector.Unprotect(setting.Value) : setting.Value;
    }

    public async Task SetAsync(string key, string value, bool isSecret, CancellationToken ct = default)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(x => x.Key == key, ct);
        var stored = isSecret ? _protector.Protect(value) : value;
        if (setting is null)
        {
            _db.Settings.Add(new AppSetting { Key = key, Value = stored, IsSecret = isSecret });
        }
        else
        {
            setting.Value = stored;
            setting.IsSecret = isSecret;
            setting.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<string, string?>> GetPublicSettingsAsync(CancellationToken ct = default)
    {
        var items = await _db.Settings.AsNoTracking().Where(x => !x.IsSecret).ToListAsync(ct);
        return items.ToDictionary(x => x.Key, x => (string?)x.Value);
    }

    public async Task<SsoSettings> GetSsoSettingsAsync(CancellationToken ct = default)
    {
        var enabled = await GetAsync(SsoEnabled, ct);
        var secret = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SsoClientSecret, ct);
        return new SsoSettings
        {
            Enabled = string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase),
            TenantId = await GetAsync(SsoTenantId, ct),
            ClientId = await GetAsync(SsoClientId, ct),
            HasClientSecret = secret is not null && !string.IsNullOrWhiteSpace(secret.Value),
            RedirectUri = await GetAsync(SsoRedirectUri, ct)
        };
    }

    public async Task SaveSsoSettingsAsync(SsoSettings settings, string? clientSecretPlain, CancellationToken ct = default)
    {
        await SetAsync(SsoEnabled, settings.Enabled ? "true" : "false", false, ct);
        await SetAsync(SsoTenantId, settings.TenantId ?? string.Empty, false, ct);
        await SetAsync(SsoClientId, settings.ClientId ?? string.Empty, false, ct);
        await SetAsync(SsoRedirectUri, settings.RedirectUri ?? string.Empty, false, ct);
        if (!string.IsNullOrWhiteSpace(clientSecretPlain))
            await SetAsync(SsoClientSecret, clientSecretPlain, true, ct);
    }
}
