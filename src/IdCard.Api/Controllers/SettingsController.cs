using IdCard.Domain.Enums;
using IdCard.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdCard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;
    private readonly IAuditService _audit;

    public SettingsController(ISettingsService settings, IAuditService audit)
    {
        _settings = settings;
        _audit = audit;
    }

    [HttpGet("sso")]
    public async Task<ActionResult<SsoSettings>> GetSso(CancellationToken ct)
        => Ok(await _settings.GetSsoSettingsAsync(ct));

    [HttpPut("sso")]
    public async Task<ActionResult<SsoSettings>> SaveSso([FromBody] SaveSsoRequest request, CancellationToken ct)
    {
        var model = new SsoSettings
        {
            Enabled = request.Enabled,
            TenantId = request.TenantId,
            ClientId = request.ClientId,
            RedirectUri = request.RedirectUri
        };
        await _settings.SaveSsoSettingsAsync(model, request.ClientSecret, ct);
        await _audit.LogAsync("Settings.SsoUpdated", ActorType.User,
            Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null,
            User.Identity?.Name,
            details: new { request.Enabled, request.TenantId, request.ClientId, secretUpdated = !string.IsNullOrWhiteSpace(request.ClientSecret) },
            ct: ct);
        return Ok(await _settings.GetSsoSettingsAsync(ct));
    }
}

public record SaveSsoRequest(bool Enabled, string? TenantId, string? ClientId, string? ClientSecret, string? RedirectUri);
