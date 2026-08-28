using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Identity.Infra;
using GreyGray.Modules.Notification.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.Architecture.Tests;

/// <summary>鎖住 M0-5 的兩個模組組合根樣板，後續 12 個模組照這個形狀複製。</summary>
public sealed class ModuleCompositionRootTests
{
    [Fact(DisplayName = "Identity / Catalog / Notification Infra 對外只暴露各自的組合根")]
    public void Infra_exposes_only_the_composition_root()
    {
        AssertOnlyExportedType(typeof(IdentityModuleRegistration));
        AssertOnlyExportedType(typeof(CatalogModuleRegistration));
        AssertOnlyExportedType(typeof(NotificationModuleRegistration));
    }

    [Fact(DisplayName = "模組連線字串延後到解析 DbContext 才檢查，且 model 含自己的 schema 與 Platform 表")]
    public void Module_dbcontexts_are_lazy_and_include_platform_tables()
    {
        AssertModule(
            typeof(IdentityModuleRegistration),
            static (services, configuration) => services.AddIdentityModule(configuration),
            "iam",
            "GreyGray_iam");
        AssertModule(
            typeof(CatalogModuleRegistration),
            static (services, configuration) => services.AddCatalogModule(configuration),
            "catalog",
            "GreyGray_catalog");
        AssertModule(
            typeof(NotificationModuleRegistration),
            static (services, configuration) => services.AddNotificationModule(configuration),
            "notify",
            "GreyGray_notify");
    }

    private static void AssertOnlyExportedType(Type compositionRoot)
    {
        compositionRoot.Assembly.GetExportedTypes().ShouldBe(
            [compositionRoot],
            ignoreOrder: true,
            $"{compositionRoot.Assembly.GetName().Name} 只能讓 Host 看見單一 Add*Module 組合根。");
    }

    private static void AssertModule(
        Type compositionRoot,
        Action<IServiceCollection, IConfiguration> register,
        string expectedSchema,
        string connectionStringName)
    {
        var emptyConfiguration = new ConfigurationBuilder().Build();
        var servicesWithoutConnection = new ServiceCollection();

        // 註冊本身不得取用連線字串，否則尚未接 DB 的 Host 連 /health 都起不來。
        Should.NotThrow(() => register(servicesWithoutConnection, emptyConfiguration));
        var dbContextType = FindModuleDbContext(servicesWithoutConnection, compositionRoot.Assembly);

        using (var provider = servicesWithoutConnection.BuildServiceProvider())
        {
            var exception = Should.Throw<InvalidOperationException>(
                () => provider.GetRequiredService(dbContextType));
            exception.Message.ShouldContain($"ConnectionStrings:{connectionStringName}");
        }

        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{connectionStringName}"] =
                    "Host=127.0.0.1;Port=5432;Database=greygray_model_probe;Username=probe;Password=probe",
            })
            .Build();
        var configuredServices = new ServiceCollection();
        register(configuredServices, configured);
        dbContextType = FindModuleDbContext(configuredServices, compositionRoot.Assembly);

        using var configuredProvider = configuredServices.BuildServiceProvider();
        using var dbContext = (DbContext)configuredProvider.GetRequiredService(dbContextType);
        dbContext.Model.GetDefaultSchema().ShouldBe(expectedSchema);
        dbContext.Model.GetEntityTypes()
            .Where(entity => entity.GetSchema() == "platform")
            .Select(entity => entity.GetTableName())
            .OrderBy(table => table, StringComparer.Ordinal)
            .ShouldBe(
                ["idempotency_key", "outbox_message", "processed_message", "saga_timer"],
                ignoreOrder: false,
                "模組 DbContext 必須套用 AddPlatformTables()，才能讓業務資料與 outbox 共用交易。");
    }

    private static Type FindModuleDbContext(IServiceCollection services, System.Reflection.Assembly assembly)
    {
        var candidates = services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.Assembly == assembly && typeof(DbContext).IsAssignableFrom(type))
            .Distinct()
            .ToArray();

        candidates.Length.ShouldBe(1, $"{assembly.GetName().Name} 必須正好註冊一個模組 DbContext。");
        return candidates[0];
    }
}
