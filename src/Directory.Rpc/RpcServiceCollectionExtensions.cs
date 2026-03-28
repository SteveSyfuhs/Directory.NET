using Directory.Rpc.Dispatch;
using Directory.Rpc.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Directory.Rpc;

public static class RpcServiceCollectionExtensions
{
    public static IServiceCollection AddRpcTransport(this IServiceCollection services)
    {
        services.AddSingleton<RpcInterfaceDispatcher>();
        services.AddSingleton<IRpcInterfaceHandler, EndpointMapper>();
        services.AddHostedService<RpcServer>();

        return services;
    }
}
