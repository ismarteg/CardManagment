using System.Security.Claims;
using IdCard.Api.Dtos;
using IdCard.Domain.Entities;
using IdCard.Domain.Enums;
using IdCard.Infrastructure.Persistence;
using IdCard.Infrastructure.Security;
using IdCard.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdCard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IAuditService _audit;
    private readonly ISettingsService _settings;

    public AuthController(AppDbContext db, IJwtTokenService jwt, IAuditService audit, ISettingsService settings)
    {
        _db = db;
        _jwt = jwt;
        _audit = audit;
        _settings = settings;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == request.UserName && u.IsActive, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            await _audit.LogAsync("Auth.LoginFailed", ActorType.User, userName: request.UserName, severity: AuditSeverity.Warning, details: new { reason = "invalid_credentials" }, ct: ct);
            return Unauthorized(new { message = "Invalid username or password" });
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var token = _jwt.CreateToken(user);
        await _audit.LogAsync("Auth.Login", ActorType.User, user.Id, user.UserName, ct: ct);
        return Ok(new LoginResponse(token, user.Id, user.UserName, user.DisplayName, user.Role.ToString()));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<object>> Me(CancellationToken ct)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var userId)) return Unauthorized();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Unauthorized();
        return Ok(new { user.Id, user.UserName, user.Email, user.DisplayName, Role = user.Role.ToString() });
    }

    [HttpPost("users")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(u => u.UserName == request.UserName || u.Email == request.Email, ct))
            return Conflict(new { message = "User already exists" });

        var user = new AppUser
        {
            UserName = request.UserName,
            Email = request.Email,
            DisplayName = request.DisplayName,
            Role = request.Role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("User.Created", ActorType.User, GetUserId(), GetUserName(), entityType: "AppUser", entityId: user.Id.ToString(), ct: ct);
        return CreatedAtAction(nameof(Me), new { id = user.Id }, new { user.Id, user.UserName, user.Email, user.Role });
    }

    [HttpGet("sso/status")]
    [AllowAnonymous]
    public async Task<ActionResult> SsoStatus(CancellationToken ct)
    {
        var sso = await _settings.GetSsoSettingsAsync(ct);
        return Ok(new { sso.Enabled, sso.TenantId, sso.ClientId, sso.RedirectUri });
    }

    [HttpPost("sso/microsoft")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> MicrosoftSso([FromBody] MicrosoftSsoRequest request, CancellationToken ct)
    {
        var sso = await _settings.GetSsoSettingsAsync(ct);
        if (!sso.Enabled)
            return BadRequest(new { message = "Microsoft SSO is not enabled" });

        // Trust validated token claims from frontend MSAL after API-side optional validation later.
        // For now map by email/oid; create Operator user if missing (admin can elevate).
        if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.Oid))
            return BadRequest(new { message = "Email or OID required" });

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (!string.IsNullOrEmpty(request.Oid) && u.MicrosoftOid == request.Oid) ||
            (!string.IsNullOrEmpty(request.Email) && u.Email == request.Email), ct);

        if (user is null)
        {
            user = new AppUser
            {
                UserName = request.Email?.Split('@')[0] ?? request.Oid!,
                Email = request.Email ?? $"{request.Oid}@microsoft",
                DisplayName = request.DisplayName ?? request.Email ?? "Microsoft User",
                Role = UserRole.Operator,
                MicrosoftOid = request.Oid,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"))
            };
            _db.Users.Add(user);
        }
        else
        {
            user.MicrosoftOid ??= request.Oid;
            user.LastLoginAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        var token = _jwt.CreateToken(user);
        await _audit.LogAsync("Auth.SsoLogin", ActorType.User, user.Id, user.UserName, details: new { provider = "Microsoft" }, ct: ct);
        return Ok(new LoginResponse(token, user.Id, user.UserName, user.DisplayName, user.Role.ToString()));
    }

    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? GetUserName() => User.Identity?.Name;
}

public record MicrosoftSsoRequest(string? Email, string? Oid, string? DisplayName, string? IdToken);
