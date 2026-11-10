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

    public ProjectsController(AppDbContext db, ISecretProtector protector, IDataConnectorFactory connectors, IAuditService audit)
    {
        _db = db;
        _protector = protector;
        _connectors = connectors;
        _audit = audit;
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
        var project = new CardProject
        {
            Name = request.Name,
            Description = request.Description,
            CardSize = request.CardSize,
            DualSided = request.DualSided,
            DataMode = request.DataMode,
            ExternalDatabaseType = request.ExternalDatabaseType,
            EncryptedConnectionString = string.IsNullOrWhiteSpace(request.ConnectionString) ? null : _protector.Protect(request.ConnectionString),
            ExternalTableName = request.ExternalTableName,
            ExternalFilterSql = request.ExternalFilterSql,
            AllowExternalWriteBack = request.AllowExternalWriteBack,
            FieldMappingsJson = JsonSerializer.Serialize(request.FieldMappings ?? new List<FieldMapping>()),
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
        project.ExternalDatabaseType = request.ExternalDatabaseType;
        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
            project.EncryptedConnectionString = _protector.Protect(request.ConnectionString);
        project.ExternalTableName = request.ExternalTableName;
        project.ExternalFilterSql = request.ExternalFilterSql;
        project.AllowExternalWriteBack = request.AllowExternalWriteBack;
        if (request.FieldMappings is not null)
            project.FieldMappingsJson = JsonSerializer.Serialize(request.FieldMappings);
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

    private ProjectDto Map(CardProject p)
    {
        var mappings = JsonSerializer.Deserialize<List<FieldMapping>>(p.FieldMappingsJson) ?? new();
        return new ProjectDto(
            p.Id, p.Name, p.Description, p.CardSize, p.DualSided, p.DataMode, p.ExternalDatabaseType,
            p.ExternalTableName, p.AllowExternalWriteBack, mappings, p.DesignJson, p.DesignVersion,
            p.DefaultPrinterId, !string.IsNullOrEmpty(p.EncryptedConnectionString));
    }

    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? GetUserName() => User.Identity?.Name;
}
