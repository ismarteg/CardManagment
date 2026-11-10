namespace IdCard.Domain.Entities;

public class PrintAgentDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PairedAtUtc { get; set; }

    public ICollection<Printer> Printers { get; set; } = new List<Printer>();
}
