using IdCard.Domain.Enums;

namespace IdCard.Domain.Entities;

public class PrintJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid PrinterId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string RecordKey { get; set; } = string.Empty;
    public string RecordSnapshotJson { get; set; } = "{}";
    public string? FrontImagePath { get; set; }
    public string? BackImagePath { get; set; }
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Queued;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public CardProject? Project { get; set; }
    public Printer? Printer { get; set; }
}
