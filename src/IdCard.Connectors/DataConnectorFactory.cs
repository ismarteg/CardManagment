using IdCard.Connectors.Abstractions;
using IdCard.Domain.Enums;

namespace IdCard.Connectors;

public class DataConnectorFactory : IDataConnectorFactory
{
    private readonly Dictionary<DatabaseType, IDataConnector> _connectors;

    public DataConnectorFactory()
    {
        _connectors = new Dictionary<DatabaseType, IDataConnector>
        {
            [DatabaseType.Sqlite] = new SqliteDataConnector(),
            [DatabaseType.SqlServer] = new SqlServerDataConnector(),
            [DatabaseType.MySql] = new MySqlDataConnector()
        };
    }

    public IDataConnector GetConnector(DatabaseType databaseType)
    {
        if (_connectors.TryGetValue(databaseType, out var connector))
            return connector;
        throw new NotSupportedException($"Database type {databaseType} is not supported.");
    }
}
