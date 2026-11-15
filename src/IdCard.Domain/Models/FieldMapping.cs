namespace IdCard.Domain.Models;

public class FieldMapping
{
    public string DesignField { get; set; } = string.Empty;
    public string SourceColumn { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DataType { get; set; } = "string"; // string, number, date, photo, photo_binary
    public bool IsPhoto { get; set; }
    public bool IsKey { get; set; }
    public bool Required { get; set; }
}
