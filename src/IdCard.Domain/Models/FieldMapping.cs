namespace IdCard.Domain.Models;

public class FieldMapping
{
    public string DesignField { get; set; } = string.Empty;
    public string SourceColumn { get; set; } = string.Empty;
    public string DataType { get; set; } = "string";
    public bool IsPhoto { get; set; }
}
