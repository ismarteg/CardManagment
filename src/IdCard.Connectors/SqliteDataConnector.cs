using System.Data.Common;
using IdCard.Connectors.Abstractions;
using IdCard.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace IdCard.Connectors;

public class SqliteDataConnector : IDataConnector
{
    public DatabaseType DatabaseType => DatabaseType.Sqlite;

    public async Task<bool> TestAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn.State == System.Data.ConnectionState.Open;
    }

    public async Task<IReadOnlyList<string>> ListTablesAsync(string connectionString, CancellationToken ct = default)
    {
        const string sql = "SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name";
        return await ReadStringsAsync(connectionString, sql, ct);
    }

    public async Task<IReadOnlyList<ColumnInfo>> ListColumnsAsync(string connectionString, string tableName, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({Quote(SanitizeIdentifier(tableName))})";
        var list = new List<ColumnInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new ColumnInfo(reader.GetString(1), reader.GetString(2), reader.GetInt64(3) == 0));
        return list;
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> QueryRecordsAsync(
        string connectionString,
        string tableName,
        IEnumerable<string> columns,
        string? filterSql,
        int skip,
        int take,
        string? search,
        CancellationToken ct = default)
    {
        var cols = columns.Select(SanitizeIdentifier).ToList();
        if (cols.Count == 0) cols.Add("*");
        var selectList = string.Join(", ", cols.Select(c => c == "*" ? "*" : Quote(c)));
        var sql = $"SELECT {selectList} FROM {Quote(SanitizeIdentifier(tableName))}";
        if (!string.IsNullOrWhiteSpace(filterSql))
            sql += $" WHERE ({filterSql})";
        sql += $" LIMIT {Math.Clamp(take, 1, 500)} OFFSET {Math.Max(0, skip)}";

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await ReadRowsAsync(cmd, ct);
    }

    public async Task<byte[]?> GetPhotoAsync(string connectionString, string tableName, string photoColumn, string keyColumn, string keyValue, CancellationToken ct = default)
    {
        var sql = $"SELECT {Quote(SanitizeIdentifier(photoColumn))} FROM {Quote(SanitizeIdentifier(tableName))} WHERE {Quote(SanitizeIdentifier(keyColumn))} = $key LIMIT 1";
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$key", keyValue);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result switch
        {
            byte[] bytes => bytes,
            string s when File.Exists(s) => await File.ReadAllBytesAsync(s, ct),
            _ => null
        };
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var list = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(reader.GetString(0));
        return list;
    }

    private static async Task<IReadOnlyList<Dictionary<string, object?>>> ReadRowsAsync(DbCommand cmd, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static string SanitizeIdentifier(string value) =>
        string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-'));

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
