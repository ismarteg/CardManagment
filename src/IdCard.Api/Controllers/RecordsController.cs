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
            var ids = items.Select(i => i.Id).ToList();
            var photos = await _db.RecordPhotos.AsNoTracking().Where(p => ids.Contains(p.RecordId)).ToListAsync(ct);

            return Ok(items.Select(r =>
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(r.DataJson) ?? new();
                var fieldPhotos = photos.Where(p => p.RecordId == r.Id)
                    .ToDictionary(
                        p => p.FieldName,
                        p => $"/api/projects/{projectId}/records/{r.Id}/photos/{p.FieldName}",
                        StringComparer.OrdinalIgnoreCase);
                return new
                {
                    r.Id,
                    key = r.ExternalKey,
                    data,
                    photoPath = r.PhotoPath,
                    photos = fieldPhotos
                };
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
            var keyField = mappings.FirstOrDefault(m => m.IsKey)?.DesignField
                           ?? mappings.FirstOrDefault()?.DesignField;
            var key = keyField is not null && data.TryGetValue(keyField, out var kv)
                ? kv?.ToString() ?? Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");
            return new { id = (Guid?)null, key, data, photoPath = (string?)null, photos = new Dictionary<string, string>(), external = true };
        });
        return Ok(result);
    }

    [HttpGet("{recordId:guid}")]
    public async Task<ActionResult> Get(Guid projectId, Guid recordId, CancellationToken ct)
    {
        var record = await _db.InternalRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recordId && r.ProjectId == projectId, ct);
        if (record is null) return NotFound();
        var photos = await _db.RecordPhotos.AsNoTracking().Where(p => p.RecordId == recordId).ToListAsync(ct);
        return Ok(new
        {
            record.Id,
            key = record.ExternalKey,
            data = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.DataJson),
            photoPath = record.PhotoPath,
            photos = photos.ToDictionary(
                p => p.FieldName,
                p => $"/api/projects/{projectId}/records/{recordId}/photos/{p.FieldName}",
                StringComparer.OrdinalIgnoreCase)
        });
    }

    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Designer)},{nameof(UserRole.Operator)}")]
    public async Task<ActionResult> Create(Guid projectId, [FromBody] UpsertRecordRequest request, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();
        if (project.DataMode != DataMode.Internal)
            return BadRequest(new { message = "Create is only supported for internal data mode" });

        var data = StripPhotoPlaceholders(request.Data);
        var record = new InternalRecord
        {
            ProjectId = projectId,
            ExternalKey = string.IsNullOrWhiteSpace(request.Key) ? Guid.NewGuid().ToString("N") : request.Key,
            DataJson = JsonSerializer.Serialize(data)
        };
        _db.InternalRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Record.Created", ActorType.User, GetUserId(), GetUserName(), projectId: projectId, entityType: "InternalRecord", entityId: record.Id.ToString(), ct: ct);
        return Ok(new
        {
            record.Id,
            key = record.ExternalKey,
            data,
            photos = new Dictionary<string, string>()
        });
    }

    [HttpPut("{recordId:guid}")]
    public async Task<ActionResult> Update(Guid projectId, Guid recordId, [FromBody] UpsertRecordRequest request, CancellationToken ct)
    {
        var record = await _db.InternalRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.ProjectId == projectId, ct);
        if (record is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(request.Key)) record.ExternalKey = request.Key;

        var existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.DataJson) ?? new();
        var incoming = StripPhotoPlaceholders(request.Data);
        foreach (var kv in incoming)
            existing[kv.Key] = kv.Value;

        record.DataJson = JsonSerializer.Serialize(existing);
        record.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Record.Updated", ActorType.User, GetUserId(), GetUserName(), projectId: projectId, entityType: "InternalRecord", entityId: record.Id.ToString(), ct: ct);

        var photos = await _db.RecordPhotos.AsNoTracking().Where(p => p.RecordId == recordId).ToListAsync(ct);
        return Ok(new
        {
            record.Id,
            key = record.ExternalKey,
            data = existing,
            photoPath = record.PhotoPath,
            photos = photos.ToDictionary(
                p => p.FieldName,
                p => $"/api/projects/{projectId}/records/{recordId}/photos/{p.FieldName}",
                StringComparer.OrdinalIgnoreCase)
        });
    }

    [HttpDelete("{recordId:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, Guid recordId, CancellationToken ct)
    {
        var record = await _db.InternalRecords.Include(r => r.Photos).FirstOrDefaultAsync(r => r.Id == recordId && r.ProjectId == projectId, ct);
        if (record is null) return NotFound();

        foreach (var photo in record.Photos.Where(p => p.StorageMode == "File" && !string.IsNullOrWhiteSpace(p.FilePath)))
        {
            if (System.IO.File.Exists(photo.FilePath))
                System.IO.File.Delete(photo.FilePath!);
        }

        _db.InternalRecords.Remove(record);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Record.Deleted", ActorType.User, GetUserId(), GetUserName(), projectId: projectId, entityType: "InternalRecord", entityId: recordId.ToString(), ct: ct);
        return NoContent();
    }

    /// <summary>
    /// Upload photo for a field. storage=file saves under uploads/photos/{projectId}/ ; storage=binary stores bytes in DB.
    /// </summary>
    [HttpPost("{recordId:guid}/photos/{fieldName}")]
    [RequestSizeLimit(15_000_000)]
    public async Task<ActionResult> UploadFieldPhoto(
        Guid projectId,
        Guid recordId,
        string fieldName,
        IFormFile file,
        [FromQuery] string storage = "file",
        CancellationToken ct = default)
    {
        var record = await _db.InternalRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.ProjectId == projectId, ct);
        if (record is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest(new { message = "File required" });

        var mode = string.Equals(storage, "binary", StringComparison.OrdinalIgnoreCase) ? "Binary" : "File";
        var safeField = Sanitize(fieldName);
        var photo = await _db.RecordPhotos.FirstOrDefaultAsync(p => p.RecordId == recordId && p.FieldName == safeField, ct);
        if (photo is null)
        {
            photo = new RecordPhoto { RecordId = recordId, FieldName = safeField };
            _db.RecordPhotos.Add(photo);
        }

        photo.ContentType = file.ContentType;
        photo.FileName = file.FileName;
        photo.UpdatedAtUtc = DateTime.UtcNow;
        photo.StorageMode = mode;

        if (mode == "Binary")
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            photo.BinaryData = ms.ToArray();
            photo.FilePath = null;
        }
        else
        {
            var dir = Path.Combine(_env.ContentRootPath, "uploads", "photos", projectId.ToString());
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            var path = Path.Combine(dir, $"{recordId}_{safeField}{ext}");
            await using (var stream = System.IO.File.Create(path))
                await file.CopyToAsync(stream, ct);
            photo.FilePath = path;
            photo.BinaryData = null;
            record.PhotoPath = path;
        }

        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.DataJson) ?? new();
        data[safeField] = mode == "Binary"
            ? $"db://{safeField}"
            : $"/files/uploads/photos/{projectId}/{Path.GetFileName(photo.FilePath)}";
        record.DataJson = JsonSerializer.Serialize(data);
        record.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("Record.PhotoUploaded", ActorType.User, GetUserId(), GetUserName(),
            projectId: projectId, entityType: "InternalRecord", entityId: recordId.ToString(),
            details: new { field = safeField, mode }, ct: ct);

        return Ok(new
        {
            field = safeField,
            storage = mode,
            url = $"/api/projects/{projectId}/records/{recordId}/photos/{safeField}"
        });
    }

    [HttpGet("{recordId:guid}/photos/{fieldName}")]
    [AllowAnonymous] // preview/img tags; tighten later with short-lived tokens if needed
    public async Task<IActionResult> GetFieldPhoto(Guid projectId, Guid recordId, string fieldName, CancellationToken ct)
    {
        var safeField = Sanitize(fieldName);
        var photo = await _db.RecordPhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.RecordId == recordId && p.FieldName == safeField, ct);
        if (photo is null)
        {
            // fallback legacy single photo path
            var record = await _db.InternalRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recordId && r.ProjectId == projectId, ct);
            if (record?.PhotoPath is not null && System.IO.File.Exists(record.PhotoPath))
                return PhysicalFile(record.PhotoPath, "application/octet-stream");
            return NotFound();
        }

        if (photo.StorageMode == "Binary" && photo.BinaryData is { Length: > 0 })
            return File(photo.BinaryData, photo.ContentType ?? "image/jpeg", photo.FileName ?? $"{safeField}.jpg");

        if (!string.IsNullOrWhiteSpace(photo.FilePath) && System.IO.File.Exists(photo.FilePath))
            return PhysicalFile(photo.FilePath, photo.ContentType ?? "application/octet-stream", photo.FileName);

        return NotFound();
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

    private static Dictionary<string, object?> StripPhotoPlaceholders(Dictionary<string, object?>? data)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (data is null) return result;
        foreach (var kv in data)
        {
            var val = kv.Value?.ToString();
            if (val is null) { result[kv.Key] = null; continue; }
            // skip raw file markers from client; photos are uploaded separately
            if (val.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
                val.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;
            result[kv.Key] = kv.Value;
        }
        return result;
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Photo" : cleaned;
    }

    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? GetUserName() => User.Identity?.Name;
}

public record UpsertRecordRequest(string? Key, Dictionary<string, object?>? Data);
