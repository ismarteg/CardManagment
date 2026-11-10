namespace IdCard.Domain.Entities;

public class Printer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = "Evolis";
    public string? SystemPrinterName { get; set; }
    public Guid? AgentId { get; set; }
    public bool IsOnline { get; set; }
    public string? StatusMessage { get; set; }
    public string CapabilitiesJson { get; set; } = "{}";
    public DateTime? LastSeenAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PrintAgentDevice? Agent { get; set; }
}
