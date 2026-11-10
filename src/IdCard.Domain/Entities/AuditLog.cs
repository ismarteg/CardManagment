using IdCard.Domain.Enums;

namespace IdCard.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ActorType ActorType { get; set; } = ActorType.User;
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty;
    public AuditSeverity Severity { get; set; } = AuditSeverity.Info;
    public Guid? ProjectId { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? PrinterId { get; set; }
    public string? DetailsJson { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
