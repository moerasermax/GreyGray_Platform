using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Inventory.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Modules;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Modules.Inventory.Infra;

public static class InventoryModuleRegistration
{
    public static IServiceCollection AddInventoryModule(
        this IServiceCollection services,
        IConfiguration configuration) => InventoryModule.Register(services, configuration);
}

internal sealed class InventoryModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_inventory";

    public static string ModuleName => "Inventory";

    public static string SchemaName => "inventory";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<InventoryDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Inventory 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_inventory'；請在使用 InventoryDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });
        services.AddScoped<IInventoryQuery, InventoryAvailabilityQuery>();
        services.TryAddSingleton<EventTypeRegistry>();
        services.AddScoped<StockReservationService>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<InventoryDbContext>();
            return new StockReservationService(
                dbContext,
                new OutboxEventPublisher<InventoryDbContext>(
                    dbContext,
                    serviceProvider.GetRequiredService<ICorrelationContext>(),
                    serviceProvider.GetRequiredService<EventTypeRegistry>()),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>());
        });
        services.AddScoped<IStockReservation>(serviceProvider =>
            serviceProvider.GetRequiredService<StockReservationService>());
        services.AddIdempotentIntegrationEventHandler<
            OrderPlaced,
            OrderPlacedInventoryHandler,
            InventoryDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            OrderCancelled,
            OrderCancelledInventoryHandler,
            InventoryDbContext>();
        services.AddScoped<IInventoryLotRepository, InventoryLotRepository>();
        services.AddScoped<IEventPublisher>(serviceProvider =>
            new OutboxEventPublisher<InventoryDbContext>(
                serviceProvider.GetRequiredService<InventoryDbContext>(),
                serviceProvider.GetRequiredService<ICorrelationContext>(),
                serviceProvider.GetRequiredService<EventTypeRegistry>()));
        services.AddIdempotentIntegrationEventHandler<
            GoodsReceived,
            GoodsReceivedInventoryHandler,
            InventoryDbContext>();
        return services;
    }
}

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : DbContext(options)
{
    public DbSet<LotAggregate> Lots => Set<LotAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("inventory");
        var lot = modelBuilder.Entity<LotAggregate>();
        lot.ToTable("lot", "inventory");
        lot.HasKey(value => value.Id);
        lot.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        lot.Property(value => value.TenantId).HasColumnName("tenant_id").IsRequired();
        lot.Property(value => value.SkuId).HasColumnName("sku_id").IsRequired();
        lot.Property(value => value.QuantityOnHand).HasColumnName("quantity_on_hand").IsRequired();
        lot.Property(value => value.QuantityReserved).HasColumnName("quantity_reserved").IsRequired();
        lot.Property(value => value.QuantityChannelAllocated)
            .HasColumnName("quantity_channel_allocated")
            .IsRequired();
        lot.Property(value => value.QuantityAvailable)
            .HasColumnName("quantity_available")
            .ValueGeneratedOnAddOrUpdate();
        lot.Property(value => value.Source)
            .HasColumnName("source")
            .HasConversion<short>();
        lot.Property(value => value.UnitCostAmountMinor)
            .HasColumnName("unit_cost_amount_minor");
        lot.Property(value => value.UnitCostCurrency)
            .HasColumnName("unit_cost_currency")
            .HasConversion<string>()
            .HasMaxLength(3);
        lot.Property(value => value.FromCampaignId)
            .HasColumnName("from_campaign_id");
        lot.Property(value => value.BatchCode)
            .HasColumnName("batch_code")
            .HasMaxLength(64);
        lot.Property(value => value.ReceivedAt)
            .HasColumnName("received_at");

        modelBuilder.AddPlatformTables();
    }
}

internal sealed class InventoryLotRepository(InventoryDbContext dbContext) : IInventoryLotRepository
{
    public void Add(LotAggregate lot) => dbContext.Lots.Add(lot);
}

internal sealed class GoodsReceivedInventoryHandler(
    IInventoryLotRepository lots,
    IEventPublisher eventPublisher)
    : IIntegrationEventHandler<GoodsReceived>
{
    public async Task HandleAsync(GoodsReceived @event, CancellationToken cancellationToken)
    {
        var lotId = LotId.New();
        var created = LotAggregate.CreateFromGoodsReceived(
            lotId,
            @event.TenantId,
            @event.SkuId,
            @event.Quantity,
            @event.UnitCost,
            @event.Source,
            @event.CampaignId,
            @event.OccurredAt);
        if (created.IsFailure)
        {
            throw new InvalidOperationException(
                $"GoodsReceived 無法建立批號：{created.Error.Code} {created.Error.Message}");
        }

        lots.Add(created.Value);
        await eventPublisher.PublishAsync(
            new LotCreated(
                Guid.CreateVersion7(),
                @event.OccurredAt,
                @event.TenantId,
                lotId,
                @event.SkuId,
                @event.Source,
                @event.UnitCost,
                @event.Quantity,
                @event.CampaignId),
            cancellationToken);
    }
}

internal sealed class InventoryAvailabilityQuery(
    InventoryDbContext dbContext,
    ICorrelationContext correlationContext) : IInventoryQuery
{
    public Task<Result<Lot>> GetLotAsync(LotId id, CancellationToken cancellationToken) =>
        Task.FromResult(Result<Lot>.Failure(
            "inventory.lot-detail-not-in-m1a",
            "批號詳情屬於 M1b/M2；M1a 只提供可信的可用量查詢。"));

    public async Task<Result<IReadOnlyList<StockAvailability>>> GetAvailabilityAsync(
        IReadOnlyCollection<SkuId> skuIds,
        CancellationToken cancellationToken)
    {
        var ids = skuIds.Select(id => id.Value).Distinct().ToArray();
        var rows = await dbContext.Lots.AsNoTracking()
            .Where(lot => lot.TenantId == correlationContext.TenantId.Value && ids.Contains(lot.SkuId))
            .GroupBy(lot => lot.SkuId)
            .Select(group => new { SkuId = group.Key, Available = group.Sum(lot => lot.QuantityAvailable) })
            .ToArrayAsync(cancellationToken);
        var available = rows.ToDictionary(row => row.SkuId, row => row.Available);
        return ids.Select(id => new StockAvailability(
                new SkuId(id),
                available.GetValueOrDefault(id)))
            .ToArray();
    }
}
