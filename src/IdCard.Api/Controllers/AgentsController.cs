using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IdCard.Domain.Entities;
using IdCard.Domain.Enums;
using IdCard.Infrastructure.Persistence;
using IdCard.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdCard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public AgentsController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult> List(CancellationToken ct)
    {
        var agents = await _db.Agents.AsNoTracking()
            .Select(a => new { a.Id, a.Name, a.MachineName, a.IsOnline, a.LastHeartbeatAtUtc, a.PairedAtUtc, printerCount = a.Printers.Count })
            .ToListAsync(ct);
        return Ok(agents);
    }

    [HttpPost("pairing-codes")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult> CreatePairingCode(CancellationToken ct)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        var entity = new AgentPairingCode
        {
            Code = code,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
        };
        _db.PairingCodes.Add(entity);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Agent.PairingCodeCreated", ActorType.User, GetUserId(), GetUserName(), details: new { code }, ct: ct);
        return Ok(new { code, expiresAtUtc = entity.ExpiresAtUtc });
    }

    [HttpPost("pair")]
    [AllowAnonymous]
    public async Task<ActionResult> Pair([FromBody] PairAgentRequest request, CancellationToken ct)
    {
        var pairing = await _db.PairingCodes.FirstOrDefaultAsync(c => c.Code == request.Code && !c.IsUsed, ct);
        if (pairing is null || pairing.ExpiresAtUtc < DateTime.UtcNow)
            return BadRequest(new { message = "Invalid or expired pairing code" });

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var agent = new PrintAgentDevice
        {
            Name = request.Name ?? request.MachineName ?? "Print Agent",
            MachineName = request.MachineName ?? Environment.MachineName,
            TokenHash = Hash(token),
            IsOnline = true,
            PairedAtUtc = DateTime.UtcNow,
            LastHeartbeatAtUtc = DateTime.UtcNow
        };
        _db.Agents.Add(agent);
        pairing.IsUsed = true;
        pairing.UsedByAgentId = agent.Id;

        if (request.Printers is not null)
        {
            foreach (var p in request.Printers)
            {
                _db.Printers.Add(new Printer
                {
                    Name = p.Name,
                    Model = p.Model ?? "Evolis",
                    SystemPrinterName = p.SystemPrinterName,
                    AgentId = agent.Id,
                    IsOnline = true,
                    LastSeenAtUtc = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Agent.Paired", ActorType.PrintAgent, agentId: agent.Id, details: new { agent.Name, agent.MachineName }, ct: ct);
        return Ok(new { agentId = agent.Id, token, agent.Name });
    }

    [HttpPost("{agentId:guid}/printers/sync")]
    [AllowAnonymous]
    public async Task<ActionResult> SyncPrinters(Guid agentId, [FromBody] SyncPrintersRequest request, CancellationToken ct)
    {
        var agent = await _db.Agents.Include(a => a.Printers).FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null) return NotFound();
        if (!ValidateToken(agent, request.Token)) return Unauthorized();

        foreach (var incoming in request.Printers)
        {
            var existing = agent.Printers.FirstOrDefault(p => p.SystemPrinterName == incoming.SystemPrinterName);
            if (existing is null)
            {
                _db.Printers.Add(new Printer
                {
                    Name = incoming.Name,
                    Model = incoming.Model ?? "Evolis",
                    SystemPrinterName = incoming.SystemPrinterName,
                    AgentId = agentId,
                    IsOnline = true,
                    LastSeenAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.Name = incoming.Name;
                existing.IsOnline = true;
                existing.LastSeenAtUtc = DateTime.UtcNow;
                existing.StatusMessage = incoming.StatusMessage;
            }
        }

        agent.IsOnline = true;
        agent.LastHeartbeatAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { synced = request.Printers.Count });
    }

    private static bool ValidateToken(PrintAgentDevice agent, string? token)
        => !string.IsNullOrWhiteSpace(token) && string.Equals(agent.TokenHash, Hash(token), StringComparison.Ordinal);

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? GetUserName() => User.Identity?.Name;
}

public record PairAgentRequest(string Code, string? Name, string? MachineName, List<AgentPrinterDto>? Printers);
public record AgentPrinterDto(string Name, string? Model, string? SystemPrinterName, string? StatusMessage);
public record SyncPrintersRequest(string Token, List<AgentPrinterDto> Printers);
