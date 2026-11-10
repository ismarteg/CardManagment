using IdCard.Connectors.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IdCard.Connectors;

public static class DependencyInjection
{
    public static IServiceCollection AddIdCardConnectors(this IServiceCollection services)
    {
        services.AddSingleton<IDataConnectorFactory, DataConnectorFactory>();
        return services;
    }
}
