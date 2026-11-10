using IdCard.Domain.Enums;

namespace IdCard.Connectors.Abstractions;

public interface IDataConnectorFactory
{
    IDataConnector GetConnector(DatabaseType databaseType);
}
