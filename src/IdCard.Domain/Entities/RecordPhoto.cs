namespace IdCard.Domain.Entities;

/// <summary>
/// Photo stored either as uploaded file path (StorageMode=File) or binary in DB (StorageMode=Binary).
/// </summary>
public class RecordPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecordId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string StorageMode { get; set; } = "File"; // File | Binary
    public string? FilePath { get; set; }
    public string? ContentType { get; set; }
    public string? FileName { get; set; }
    public byte[]? BinaryData { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public InternalRecord? Record { get; set; }
}
