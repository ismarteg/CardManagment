using IdCard.Domain.Enums;

namespace IdCard.Domain.Entities;

public class CardProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CardSize { get; set; } = "CR80";
    public bool DualSided { get; set; } = true;
    public DataMode DataMode { get; set; } = DataMode.Internal;
    public DatabaseType? ExternalDatabaseType { get; set; }
    public string? EncryptedConnectionString { get; set; }
    public string? ExternalTableName { get; set; }
    public string? ExternalFilterSql { get; set; }
    public bool AllowExternalWriteBack { get; set; }
    public string FieldMappingsJson { get; set; } = "[]";
    public string DesignJson { get; set; } = """{"designVersion":1,"front":{"elements":[]},"back":{"elements":[]}}""";
    public int DesignVersion { get; set; } = 1;
    public Guid? DefaultPrinterId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Printer? DefaultPrinter { get; set; }
    public ICollection<InternalRecord> InternalRecords { get; set; } = new List<InternalRecord>();
    public ICollection<PrintJob> PrintJobs { get; set; } = new List<PrintJob>();
}
