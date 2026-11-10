using IdCard.Domain.Enums;

namespace IdCard.Connectors.Abstractions;

public interface IDataConnector
{
    DatabaseType DatabaseType { get; }
    Task<bool> TestAsync(string connectionString, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListTablesAsync(string connectionString, CancellationToken ct = default);
    Task<IReadOnlyList<ColumnInfo>> ListColumnsAsync(string connectionString, string tableName, CancellationToken ct = default);
    Task<IReadOnlyList<Dictionary<string, object?>>> QueryRecordsAsync(
        string connectionString,
        string tableName,
        IEnumerable<string> columns,
        string? filterSql,
        int skip,
        int take,
        string? search,
        CancellationToken ct = default);
    Task<byte[]?> GetPhotoAsync(string connectionString, string tableName, string photoColumn, string keyColumn, string keyValue, CancellationToken ct = default);
}

public record ColumnInfo(string Name, string DataType, bool IsNullable);
