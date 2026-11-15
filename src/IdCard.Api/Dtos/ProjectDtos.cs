using IdCard.Domain.Enums;
using IdCard.Domain.Models;

namespace IdCard.Api.Dtos;

public record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    string CardSize,
    bool DualSided,
    DataMode DataMode,
    string? InternalTableName,
    DatabaseType? ExternalDatabaseType,
    string? ExternalTableName,
    bool AllowExternalWriteBack,
    List<FieldMapping> FieldMappings,
    string DesignJson,
    int DesignVersion,
    Guid? DefaultPrinterId,
    bool HasConnectionString);

public record CreateProjectRequest(
    string Name,
    string? Description,
    string CardSize,
    bool DualSided,
    DataMode DataMode,
    string? InternalTableName,
    DatabaseType? ExternalDatabaseType,
    string? ConnectionString,
    string? ExternalTableName,
    string? ExternalFilterSql,
    bool AllowExternalWriteBack,
    List<FieldMapping>? FieldMappings,
    Guid? DefaultPrinterId);

public record UpdateProjectRequest(
    string Name,
    string? Description,
    string CardSize,
    bool DualSided,
    DataMode DataMode,
    string? InternalTableName,
    DatabaseType? ExternalDatabaseType,
    string? ConnectionString,
    string? ExternalTableName,
    string? ExternalFilterSql,
    bool AllowExternalWriteBack,
    List<FieldMapping>? FieldMappings,
    string? DesignJson,
    Guid? DefaultPrinterId);

public record TestConnectionRequest(DatabaseType DatabaseType, string ConnectionString);
public record DiscoverRequest(DatabaseType DatabaseType, string ConnectionString, string? TableName);
