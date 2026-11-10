using System.Text.Json;
using IdCard.Domain.Entities;
using IdCard.Domain.Enums;
using IdCard.Infrastructure.Persistence;

namespace IdCard.Infrastructure.Services;

public interface IAuditService
{
    Task LogAsync(
        string action,
        ActorType actorType = ActorType.User,
        Guid? userId = null,
        string? userName = null,
        AuditSeverity severity = AuditSeverity.Info,
        Guid? projectId = null,
        string? entityType = null,
        string? entityId = null,
        Guid? agentId = null,
        Guid? printerId = null,
        object? details = null,
        string? correlationId = null,
        CancellationToken ct = default);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        string action,
        ActorType actorType = ActorType.User,
        Guid? userId = null,
        string? userName = null,
        AuditSeverity severity = AuditSeverity.Info,
        Guid? projectId = null,
        string? entityType = null,
        string? entityId = null,
        Guid? agentId = null,
        Guid? printerId = null,
        object? details = null,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            ActorType = actorType,
            UserId = userId,
            UserName = userName,
            Severity = severity,
            ProjectId = projectId,
            EntityType = entityType,
            EntityId = entityId,
            AgentId = agentId,
            PrinterId = printerId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            CorrelationId = correlationId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }
}
