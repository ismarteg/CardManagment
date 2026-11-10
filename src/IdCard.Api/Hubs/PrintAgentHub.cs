using IdCard.Domain.Enums;
using IdCard.Infrastructure.Persistence;
using IdCard.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace IdCard.Api.Hubs;

public class PrintAgentHub : Hub
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public PrintAgentHub(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public override async Task OnConnectedAsync()
    {
        var agentId = Context.GetHttpContext()?.Request.Query["agentId"].FirstOrDefault();
        if (Guid.TryParse(agentId, out var id))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AgentGroup(id));
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == id);
            if (agent is not null)
            {
                agent.IsOnline = true;
                agent.LastHeartbeatAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var agentId = Context.GetHttpContext()?.Request.Query["agentId"].FirstOrDefault();
        if (Guid.TryParse(agentId, out var id))
        {
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == id);
            if (agent is not null)
            {
                agent.IsOnline = false;
                await _db.SaveChangesAsync();
                await _audit.LogAsync("Agent.Offline", ActorType.PrintAgent, agentId: id, severity: AuditSeverity.Warning);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Heartbeat(Guid agentId, string? status)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId);
        if (agent is null) return;
        agent.IsOnline = true;
        agent.LastHeartbeatAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task ReportJobStatus(Guid jobId, string status, string? error)
    {
        var job = await _db.PrintJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job is null) return;

        if (Enum.TryParse<PrintJobStatus>(status, true, out var parsed))
            job.Status = parsed;
        job.ErrorMessage = error;
        job.UpdatedAtUtc = DateTime.UtcNow;
        if (parsed is PrintJobStatus.Done or PrintJobStatus.Failed or PrintJobStatus.Cancelled)
            job.CompletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            $"Print.{status}",
            ActorType.PrintAgent,
            agentId: job.AgentId,
            printerId: job.PrinterId,
            projectId: job.ProjectId,
            entityType: "PrintJob",
            entityId: job.Id.ToString(),
            severity: parsed == PrintJobStatus.Failed ? AuditSeverity.Error : AuditSeverity.Info,
            details: new { error });
    }

    public static string AgentGroup(Guid agentId) => $"agent:{agentId}";
}
