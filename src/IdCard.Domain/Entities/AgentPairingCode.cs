namespace IdCard.Domain.Entities;

public class AgentPairingCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public Guid? UsedByAgentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
