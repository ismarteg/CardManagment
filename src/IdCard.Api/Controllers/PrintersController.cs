using System.Security.Claims;
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
[Authorize]
public class PrintersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public PrintersController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult> List(CancellationToken ct)
    {
        var printers = await _db.Printers.AsNoTracking()
            .Include(p => p.Agent)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id, p.Name, p.Model, p.SystemPrinterName, p.AgentId, p.IsOnline, p.StatusMessage, p.LastSeenAtUtc,
                agentName = p.Agent != null ? p.Agent.Name : null,
                agentOnline = p.Agent != null && p.Agent.IsOnline
            })
            .ToListAsync(ct);
        return Ok(printers);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult> Create([FromBody] CreatePrinterRequest request, CancellationToken ct)
    {
        var printer = new Printer
        {
            Name = request.Name,
            Model = request.Model ?? "Evolis",
            SystemPrinterName = request.SystemPrinterName,
            AgentId = request.AgentId
        };
        _db.Printers.Add(printer);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Printer.Created", ActorType.User, GetUserId(), GetUserName(), entityType: "Printer", entityId: printer.Id.ToString(), printerId: printer.Id, ct: ct);
        return Ok(printer);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var printer = await _db.Printers.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (printer is null) return NotFound();
        _db.Printers.Remove(printer);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Printer.Deleted", ActorType.User, GetUserId(), GetUserName(), entityType: "Printer", entityId: id.ToString(), printerId: id, ct: ct);
        return NoContent();
    }

    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? GetUserName() => User.Identity?.Name;
}

public record CreatePrinterRequest(string Name, string? Model, string? SystemPrinterName, Guid? AgentId);
