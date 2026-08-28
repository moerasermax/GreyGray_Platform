using GreyGray.Platform.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GreyGray.Modules.Identity.Infra;

/// <summary>Identity 模組唯一對外公開的組合根。</summary>
public static class IdentityModuleRegistration
{
    /// <summary>註冊 Identity 模組的基礎設施服務。</summary>
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        IdentityModule.Register(services, configuration);
}

/// <summary>Identity 模組的內部登錄描述。</summary>
internal sealed class IdentityModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_iam";

    public static string ModuleName => "Identity";

    public static string SchemaName => "iam";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // options factory 在真正解析 IdentityDbContext 時才執行，讓尚未掛載模組資料庫的
        // Host 仍可啟動並提供 /health；一旦使用模組則立即以明確訊息失敗。
        services.AddDbContext<IdentityDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Identity 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_iam'；請在使用 IdentityDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        return services;
    }
}
