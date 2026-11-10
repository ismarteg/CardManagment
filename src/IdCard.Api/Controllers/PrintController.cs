using System.Security.Claims;
using System.Text.Json;
using IdCard.Api.Hubs;
using IdCard.Api.Services;
using IdCard.Domain.Entities;
using IdCard.Domain.Enums;
using IdCard.Infrastructure.Persistence;
using IdCard.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace IdCard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PrintController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICardRenderService _renderer;
    private readonly IHubContext<PrintAgentHub> _hub;
    private readonly IAuditService _audit;
    private readonly IWebHostEnvironment _env;

    public PrintController(
        AppDbContext db,
        ICardRenderService renderer,
        IHubContext<PrintAgentHub> hub,
        IAuditService audit,
        IWebHostEnvironment env)
    {
        _db = db;
        _renderer = renderer;
        _hub = hub;
        _audit = audit;
        _env = env;
    }

    [HttpPost("queue")]
    public async Task<ActionResult> Queue([FromBody] QueuePrintRequest request, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);
        if (project is null) return NotFound(new { message = "Project not found" });

        var printerId = request.PrinterId ?? project.DefaultPrinterId;
        if (printerId is null) return BadRequest(new { message = "Printer is required" });

        var printer = await _db.Printers.FirstOrDefaultAsync(p => p.Id == printerId, ct);
        if (printer is null) return BadFound("Printer not found");

        var design = CardRenderService.ParseDesign(project.DesignJson);
        var outputDir = Path.Combine(_env.ContentRootPath, "uploads", "print-jobs");
        var jobs = new List<object>();

        foreach (var item in request.Records)
        {
            var jobId = Guid.NewGuid();
            var (front, back) = await _renderer.RenderAsync(design, item.Data, outputDir, jobId, project.DualSided, ct);
            var job = new PrintJob
            {
                Id = jobId,
                ProjectId = project.Id,
                PrinterId = printer.Id,
                AgentId = printer.AgentId,
                RequestedByUserId = GetUserId() ?? Guid.Empty,
                RecordKey = item.Key,
                RecordSnapshotJson = JsonSerializer.Serialize(item.Data),
                FrontImagePath = front,
                BackImagePath = back,
                Status = PrintJobStatus.Queued,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.PrintJobs.Add(job);
            jobs.Add(new { job.Id, job.RecordKey, job.Status });

            if (printer.AgentId is Guid agentId)
            {
                await _hub.Clients.Group(PrintAgentHub.AgentGroup(agentId)).SendAsync("PrintJob", new
                {
                    jobId = job.Id,
                    printerId = printer.Id,
                    systemPrinterName = printer.SystemPrinterName,
                    frontImagePath = front,
                    backImagePath = back,
                    recordKey = item.Key
                }, ct);
                job.Status = PrintJobStatus.Sent;
            }
            else
            {
                // No agent: mock complete for local demo
                job.Status = PrintJobStatus.Done;
                job.CompletedAtUtc = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Print.Queued", ActorType.User, GetUserId(), GetUserName(), projectId: project.Id, printerId: printer.Id, details: new { count = request.Records.Count }, ct: ct);
        return Ok(new { jobs });
    }

    [HttpGet("jobs")]
    public async Task<ActionResult> Jobs([FromQuery] Guid? projectId, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var query = _db.PrintJobs.AsNoTracking().AsQueryable();
        if (projectId is not null) query = query.Where(j => j.ProjectId == projectId);
        var items = await query.OrderByDescending(j => j.CreatedAtUtc).Take(Math.Clamp(take, 1, 200))
            .Select(j => new
            {
                j.Id, j.ProjectId, j.PrinterId, j.AgentId, j.RecordKey, j.Status, j.ErrorMessage, j.CreatedAtUtc, j.CompletedAtUtc,
                j.FrontImagePath, j.BackImagePath
            }).ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("jobs/{id:guid}/cancel")]
    public async Task<ActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var job = await _db.PrintJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound();
        if (job.Status is PrintJobStatus.Done or PrintJobStatus.Cancelled)
            return BadRequest(new { message = "Job already finished" });
        job.Status = PrintJobStatus.Cancelled;
        job.UpdatedAtUtc = DateTime.UtcNow;
        job.CompletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Print.Cancelled", ActorType.User, GetUserId(), GetUserName(), projectId: job.ProjectId, entityType: "PrintJob", entityId: id.ToString(), ct: ct);
        return Ok(job);
    }

    [HttpGet("preview/{projectId:guid}")]
    public async Task<ActionResult> Preview(Guid projectId, [FromQuery] string? recordId, CancellationToken ct)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        Dictionary<string, object?> data;
        if (Guid.TryParse(recordId, out var rid))
        {
            var record = await _db.InternalRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid && r.ProjectId == projectId, ct);
            if (record is null) return NotFound();
            data = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.DataJson) ?? new();
            if (!string.IsNullOrEmpty(record.PhotoPath)) data["Photo"] = record.PhotoPath;
        }
        else
        {
            data = new Dictionary<string, object?> { ["FullName"] = "Sample User", ["StudentId"] = "0000" };
        }

        var design = CardRenderService.ParseDesign(project.DesignJson);
        var dir = Path.Combine(_env.ContentRootPath, "uploads", "previews");
        var id = Guid.NewGuid();
        var (front, back) = await _renderer.RenderAsync(design, data, dir, id, project.DualSided, ct);
        return Ok(new { frontImage = ToRelative(front), backImage = ToRelative(back) });
    }

    private string? ToRelative(string? path)
    {
        if (path is null) return null;
        var uploads = Path.Combine(_env.ContentRootPath, "uploads")
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (path.StartsWith(uploads, StringComparison.OrdinalIgnoreCase))
            return "/files/uploads/" + path[uploads.Length..].Replace('\\', '/');
        return path.Replace('\\', '/');
    }

    private ActionResult BadFound(string message) => NotFound(new { message });
    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? GetUserName() => User.Identity?.Name;
}

public record QueuePrintRequest(Guid ProjectId, Guid? PrinterId, List<PrintRecordItem> Records);
public record PrintRecordItem(string Key, Dictionary<string, object?> Data);
