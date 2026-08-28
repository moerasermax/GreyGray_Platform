using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Platform.Idempotency;
using GreyGray.Platform.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Platform;

public static class ApiPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddGreyGrayApiPlatform(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<PlatformDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("GreyGray_platform")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:GreyGray_platform is required when platform persistence is used.");

            options.UseNpgsql(connectionString);
        });

        services.TryAddScoped<IIdempotencyStore, IdempotencyStore>();

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("GreyGray_valkey")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:GreyGray_valkey is required when session storage is used.");
            options.InstanceName = "greygray:";
        });

        services.TryAddScoped<ISessionStore, DistributedSessionStore>();

        return services;
    }
}
