namespace IdCard.Domain.Entities;

public class AppSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
