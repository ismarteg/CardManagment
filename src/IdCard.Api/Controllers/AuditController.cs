using IdCard.Domain.Enums;
using IdCard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace IdCard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult> Query(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? action,
        [FromQuery] string? userName,
        [FromQuery] Guid? projectId,
        [FromQuery] AuditSeverity? severity,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (from is not null) query = query.Where(a => a.CreatedAtUtc >= from);
        if (to is not null) query = query.Where(a => a.CreatedAtUtc <= to);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action.Contains(action));
        if (!string.IsNullOrWhiteSpace(userName)) query = query.Where(a => a.UserName != null && a.UserName.Contains(userName));
        if (projectId is not null) query = query.Where(a => a.ProjectId == projectId);
        if (severity is not null) query = query.Where(a => a.Severity == severity);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAtUtc)
            .Skip(skip).Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);
        return Ok(new { total, items });
    }

    [HttpGet("export")]
    public async Task<ActionResult> Export(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (from is not null) query = query.Where(a => a.CreatedAtUtc >= from);
        if (to is not null) query = query.Where(a => a.CreatedAtUtc <= to);
        var items = await query.OrderByDescending(a => a.CreatedAtUtc).Take(5000).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("CreatedAtUtc,ActorType,UserName,Action,Severity,ProjectId,EntityType,EntityId,DetailsJson");
        foreach (var a in items)
        {
            sb.AppendLine($"\"{a.CreatedAtUtc:o}\",{a.ActorType},\"{a.UserName}\",\"{a.Action}\",{a.Severity},{a.ProjectId},{a.EntityType},{a.EntityId},\"{a.DetailsJson?.Replace("\"", "\"\"")}\"");
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "audit-log.csv");
    }
}
