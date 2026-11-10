using System.Security.Claims;
using System.Text.Json;
using IdCard.Connectors.Abstractions;
using IdCard.Domain.Entities;
using IdCard.Domain.Enums;
using IdCard.Domain.Models;
using IdCard.Infrastructure.Persistence;
using IdCard.Infrastructure.Security;
using IdCard.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdCard.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/records")]
[Authorize]
public class RecordsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IDataConnectorFactory _connectors;
    private readonly IAuditService _audit;
    private readonly IWebHostEnvironment _env;

    public RecordsController(
        AppDbContext db,
        ISecretProtector protector,
        IDataConnectorFactory connectors,
        IAuditService audit,
        IWebHostEnvironment env)
    {
        _db = db;
        _protector = protector;
        _connectors = connectors;
        _audit = audit;
        _env = env;
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        if (project.DataMode == DataMode.Internal)
        {
            var query = _db.InternalRecords.AsNoTracking().Where(r => r.ProjectId == projectId);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(r => r.DataJson.Contains(search) || r.ExternalKey.Contains(search));
            var items = await query.OrderByDescending(r => r.UpdatedAtUtc).Skip(skip).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
            return Ok(items.Select(r => new
            {
                r.Id,
                key = r.ExternalKey,
                data = JsonSerializer.Deserialize<Dictionary<string, object?>>(r.DataJson),
                photoPath = r.PhotoPath
            }));
        }

        if (project.ExternalDatabaseType is null || string.IsNullOrEmpty(project.EncryptedConnectionString) || string.IsNullOrEmpty(project.ExternalTableName))
            return BadRequest(new { message = "External connection is not configured" });

        var mappings = JsonSerializer.Deserialize<List<FieldMapping>>(project.FieldMappingsJson) ?? new();
        var columns = mappings.Select(m => m.SourceColumn).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        var conn = _protector.Unprotect(project.EncryptedConnectionString);
        var rows = await _connectors.GetConnector(project.ExternalDatabaseType.Value)
            .QueryRecordsAsync(conn, project.ExternalTableName, columns, project.ExternalFilterSql, skip, take, search, ct);

        var result = rows.Select(row =>
        {
            var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in mappings)
            {
                if (row.TryGetValue(map.SourceColumn, out var value))
                    data[map.DesignField] = value;
            }
            var key = mappings.FirstOrDefault()?.DesignField is { } k && data.TryGetValue(k, out var kv)
                ? kv?.ToString() ?? Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");
            return new { id = (Guid?)null, key, data, photoPath = (string?)null };
        });
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Designer)},{nameof(UserRole.Operator)}")]
    public async Task<ActionResult> Create(Guid projectId, [FromBody] UpsertRecordRequest request, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();
        if (project.DataMode != DataMode.Internal)
            return BadRequest(new { message = "Create is only supported for internal data mode" });

        var record = new InternalRecord
        {
            ProjectId = projectId,
            ExternalKey = string.IsNullOrWhiteSpace(request.Key) ? Guid.NewGuid().ToString("N") : request.Key,
            DataJson = JsonSerializer.Serialize(request.Data ?? new Dictionary<string, object?>())
        };
        _db.InternalRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Record.Created", ActorType.User, GetUserId(), GetUserName(), projectId: projectId, entityType: "InternalRecord", entityId: record.Id.ToString(), ct: ct);
        return Ok(new { record.Id, key = record.ExternalKey, data = request.Data });
    }

    [HttpPut("{recordId:guid}")]
    public async Task<ActionResult> Update(Guid projectId, Guid recordId, [FromBody] UpsertRecordRequest request, CancellationToken ct)
    {
        var record = await _db.InternalRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.ProjectId == projectId, ct);
        if (record is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(request.Key)) record.ExternalKey = request.Key;
        record.DataJson = JsonSerializer.Serialize(request.Data ?? new Dictionary<string, object?>());
        record.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Record.Updated", ActorType.User, GetUserId(), GetUserName(), projectId: projectId, entityType: "InternalRecord", entityId: record.Id.ToString(), ct: ct);
        return Ok(new { record.Id, key = record.ExternalKey, data = request.Data, photoPath = record.PhotoPath });
    }

    [HttpDelete("{recordId:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, Guid recordId, CancellationToken ct)
    {
        var record = await _db.InternalRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.ProjectId == projectId, ct);
        if (record is null) return NotFound();
        _db.InternalRecords.Remove(record);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Record.Deleted", ActorType.User, GetUserId(), GetUserName(), projectId: projectId, entityType: "InternalRecord", entityId: recordId.ToString(), ct: ct);
        return NoContent();
    }

    [HttpPost("{recordId:guid}/photo")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult> UploadPhoto(Guid projectId, Guid recordId, IFormFile file, CancellationToken ct)
    {
        var record = await _db.InternalRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.ProjectId == projectId, ct);
        if (record is null) return NotFound();
        var dir = Path.Combine(_env.ContentRootPath, "uploads", "photos", projectId.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{recordId}{Path.GetExtension(file.FileName)}");
        await using (var stream = System.IO.File.Create(path))
            await file.CopyToAsync(stream, ct);
        record.PhotoPath = path;
        record.UpdatedAtUtc = DateTime.UtcNow;

        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.DataJson) ?? new();
        data["Photo"] = path;
        record.DataJson = JsonSerializer.Serialize(data);
        await _db.SaveChangesAsync(ct);
        return Ok(new { photoPath = path });
    }

    [HttpPost("import-csv")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Designer)}")]
    public async Task<ActionResult> ImportCsv(Guid projectId, IFormFile file, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();
        if (project.DataMode != DataMode.Internal)
            return BadRequest(new { message = "CSV import is only for internal mode" });

        using var reader = new StreamReader(file.OpenReadStream());
        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(headerLine)) return BadRequest(new { message = "Empty CSV" });
        var headers = headerLine.Split(',').Select(h => h.Trim().Trim('"')).ToArray();
        var count = 0;
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = line.Split(',').Select(v => v.Trim().Trim('"')).ToArray();
            var data = new Dictionary<string, object?>();
            for (var i = 0; i < headers.Length && i < values.Length; i++)
                data[headers[i]] = values[i];
            var key = data.Values.FirstOrDefault()?.ToString() ?? Guid.NewGuid().ToString("N");
            _db.InternalRecords.Add(new InternalRecord
            {
                ProjectId = projectId,
                ExternalKey = key,
                DataJson = JsonSerializer.Serialize(data)
            });
            count++;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Record.Imported", ActorType.User, GetUserId(), GetUserName(), projectId: projectId, details: new { count }, ct: ct);
        return Ok(new { imported = count });
    }

    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? GetUserName() => User.Identity?.Name;
}

public record UpsertRecordRequest(string? Key, Dictionary<string, object?>? Data);
