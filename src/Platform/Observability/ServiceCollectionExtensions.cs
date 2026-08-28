using GreyGray.Platform.Time;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Platform.Observability;

/// <summary>註冊所有 Host 共用的時間與關聯內容實作。</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGreyGrayRuntimeContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<CorrelationContext>();
        services.TryAddSingleton<ICorrelationContext>(static provider =>
            provider.GetRequiredService<CorrelationContext>());

        return services;
    }
}
