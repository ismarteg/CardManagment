using System.Security.Claims;
using System.Text.Json;
using IdCard.Api.Dtos;
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
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IDataConnectorFactory _connectors;
    private readonly IAuditService _audit;
    private readonly IWebHostEnvironment _env;

    public ProjectsController(
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
    public async Task<ActionResult<IEnumerable<ProjectDto>>> List(CancellationToken ct)
    {
        var projects = await _db.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        return Ok(projects.Select(Map));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> Get(Guid id, CancellationToken ct)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return project is null ? NotFound() : Ok(Map(project));
    }

    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Designer)}")]
    public async Task<ActionResult<ProjectDto>> Create([FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        var fields = NormalizeFields(request.FieldMappings);
        if (request.DataMode == DataMode.Internal && fields.Count == 0)
            return BadRequest(new { message = "Define at least one column for the internal table." });

        var project = new CardProject
        {
            Name = request.Name,
            Description = request.Description,
            CardSize = request.CardSize,
            DualSided = request.DualSided,
            DataMode = request.DataMode,
            InternalTableName = string.IsNullOrWhiteSpace(request.InternalTableName)
                ? SanitizeTableName(request.Name)
                : SanitizeTableName(request.InternalTableName),
            ExternalDatabaseType = request.ExternalDatabaseType,
            EncryptedConnectionString = string.IsNullOrWhiteSpace(request.ConnectionString) ? null : _protector.Protect(request.ConnectionString),
            ExternalTableName = request.ExternalTableName,
            ExternalFilterSql = request.ExternalFilterSql,
            AllowExternalWriteBack = request.AllowExternalWriteBack,
            FieldMappingsJson = JsonSerializer.Serialize(fields),
            DefaultPrinterId = request.DefaultPrinterId
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Project.Created", ActorType.User, GetUserId(), GetUserName(), projectId: project.Id, entityType: "CardProject", entityId: project.Id.ToString(), ct: ct);
        return CreatedAtAction(nameof(Get), new { id = project.Id }, Map(project));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Designer)}")]
    public async Task<ActionResult<ProjectDto>> Update(Guid id, [FromBody] UpdateProjectRequest request, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        project.Name = request.Name;
        project.Description = request.Description;
        project.CardSize = request.CardSize;
        project.DualSided = request.DualSided;
        project.DataMode = request.DataMode;
        if (!string.IsNullOrWhiteSpace(request.InternalTableName))
            project.InternalTableName = SanitizeTableName(request.InternalTableName);
        project.ExternalDatabaseType = request.ExternalDatabaseType;
        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
            project.EncryptedConnectionString = _protector.Protect(request.ConnectionString);
        project.ExternalTableName = request.ExternalTableName;
        project.ExternalFilterSql = request.ExternalFilterSql;
        project.AllowExternalWriteBack = request.AllowExternalWriteBack;
        if (request.FieldMappings is not null)
        {
            var fields = NormalizeFields(request.FieldMappings);
            if (project.DataMode == DataMode.Internal && fields.Count == 0)
                return BadRequest(new { message = "Define at least one column for the internal table." });
            project.FieldMappingsJson = JsonSerializer.Serialize(fields);
        }
        if (request.DesignJson is not null)
        {
            project.DesignJson = request.DesignJson;
            project.DesignVersion++;
        }
        project.DefaultPrinterId = request.DefaultPrinterId;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Project.Updated", ActorType.User, GetUserId(), GetUserName(), projectId: project.Id, entityType: "CardProject", entityId: project.Id.ToString(), ct: ct);
        return Ok(Map(project));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();
        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Project.Deleted", ActorType.User, GetUserId(), GetUserName(), projectId: id, entityType: "CardProject", entityId: id.ToString(), ct: ct);
        return NoContent();
    }

    [HttpPost("test-connection")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Designer)}")]
    public async Task<ActionResult> TestConnection([FromBody] TestConnectionRequest request, CancellationToken ct)
    {
        try
        {
            var ok = await _connectors.GetConnector(request.DatabaseType).TestAsync(request.ConnectionString, ct);
            return Ok(new { success = ok });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("discover/tables")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Designer)}")]
    public async Task<ActionResult> DiscoverTables([FromBody] DiscoverRequest request, CancellationToken ct)
    {
        var tables = await _connectors.GetConnector(request.DatabaseType).ListTablesAsync(request.ConnectionString, ct);
        return Ok(tables);
    }

    [HttpPost("discover/columns")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Designer)}")]
    public async Task<ActionResult> DiscoverColumns([FromBody] DiscoverRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { message = "TableName required" });
        var columns = await _connectors.GetConnector(request.DatabaseType).ListColumnsAsync(request.ConnectionString, request.TableName, ct);
        return Ok(columns);
    }

    /// <summary>Upload a static design image (logo, seal, etc). Returns a /files/uploads URL for design JSON.</summary>
    [HttpPost("{id:guid}/design-assets")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Designer)}")]
    [RequestSizeLimit(52_428_800)]
    public async Task<ActionResult> UploadDesignAsset(Guid id, IFormFile file, CancellationToken ct)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest(new { message = "File required" });

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 8) ext = ".png";
        var dir = Path.Combine(_env.ContentRootPath, "uploads", "design", id.ToString());
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var path = Path.Combine(dir, name);
        await using (var fs = System.IO.File.Create(path))
            await file.CopyToAsync(fs, ct);

        var url = $"/files/uploads/design/{id}/{name}";
        await _audit.LogAsync("Project.DesignAssetUploaded", ActorType.User, GetUserId(), GetUserName(),
            projectId: id, details: new { url, file.FileName, file.Length }, ct: ct);
        return Ok(new { url, fileName = file.FileName });
    }

    private ProjectDto Map(CardProject p)
    {
        var mappings = JsonSerializer.Deserialize<List<FieldMapping>>(p.FieldMappingsJson) ?? new();
        return new ProjectDto(
            p.Id, p.Name, p.Description, p.CardSize, p.DualSided, p.DataMode, p.InternalTableName,
            p.ExternalDatabaseType, p.ExternalTableName, p.AllowExternalWriteBack, mappings, p.DesignJson,
            p.DesignVersion, p.DefaultPrinterId, !string.IsNullOrEmpty(p.EncryptedConnectionString));
    }

    private static List<FieldMapping> NormalizeFields(List<FieldMapping>? fields)
    {
        if (fields is null) return new();
        var result = new List<FieldMapping>();
        foreach (var f in fields)
        {
            var name = SanitizeColumnName(string.IsNullOrWhiteSpace(f.SourceColumn) ? f.DesignField : f.SourceColumn);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var dataType = string.IsNullOrWhiteSpace(f.DataType) ? "string" : f.DataType.Trim().ToLowerInvariant();
            if (dataType is "photo (binary)" or "photobinary") dataType = "photo_binary";
            result.Add(new FieldMapping
            {
                DesignField = string.IsNullOrWhiteSpace(f.DesignField) ? name : f.DesignField.Trim(),
                SourceColumn = name,
                Label = string.IsNullOrWhiteSpace(f.Label) ? name : f.Label.Trim(),
                DataType = dataType,
                IsPhoto = f.IsPhoto || dataType is "photo" or "photo_binary",
                IsKey = f.IsKey,
                Required = f.Required || f.IsKey
            });
        }
        return result;
    }

    private static string SanitizeTableName(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) return "Table1";
        if (char.IsDigit(cleaned[0])) cleaned = "T_" + cleaned;
        return cleaned;
    }

    private static string SanitizeColumnName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
        if (char.IsDigit(cleaned[0])) cleaned = "C_" + cleaned;
        return cleaned;
    }

    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? GetUserName() => User.Identity?.Name;
}
