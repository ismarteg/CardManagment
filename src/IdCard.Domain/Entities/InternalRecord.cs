namespace IdCard.Domain.Entities;

public class InternalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string ExternalKey { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public string? PhotoPath { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public CardProject? Project { get; set; }
    public ICollection<RecordPhoto> Photos { get; set; } = new List<RecordPhoto>();
}
