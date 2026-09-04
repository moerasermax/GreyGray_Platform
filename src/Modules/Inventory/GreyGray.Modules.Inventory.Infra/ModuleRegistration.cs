using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
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
using Microsoft.Extensions.Logging;

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

        // #46：StockReservationService 要在「已出庫所以不釋放」時留一行紀錄。冪等地補上
        // logging，測試那幾個手搭的 ServiceProvider 才不必每個都記得加。
        services.AddLogging();
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
                serviceProvider.GetRequiredService<ICorrelationContext>(),
                serviceProvider.GetRequiredService<ILogger<StockReservationService>>());
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
        // 交運出庫（#43）。之前完全沒有任何 shipment 事件的 handler，所以貨寄出去了、
        // 帳面上 quantity_on_hand 一件都沒少，reserved 還永遠掛著。
        services.AddIdempotentIntegrationEventHandler<
            ShipmentDispatched,
            ShipmentDispatchedInventoryHandler,
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
        services.AddScoped<InventoryApplicationService>(serviceProvider =>
            new InventoryApplicationService(
                serviceProvider.GetRequiredService<IInventoryLotRepository>(),
                serviceProvider.GetRequiredService<InventoryDbContext>(),
                serviceProvider.GetRequiredService<IEventPublisher>(),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>()));
        services.AddScoped<IInventoryReceiving>(serviceProvider =>
            serviceProvider.GetRequiredService<InventoryApplicationService>());
        services.AddScoped<IInventoryLotQuery>(serviceProvider =>
            serviceProvider.GetRequiredService<InventoryApplicationService>());
        return services;
    }
}

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : DbContext(options), IUnitOfWork
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
        lot.Property(value => value.CreationIdempotencyKey)
            .HasColumnName("creation_idempotency_key")
            .HasMaxLength(255);
        lot.Property(value => value.ReceivedAt)
            .HasColumnName("received_at");
        // 過濾式唯一索引：帶回入庫與 0017 之前建立的批號都沒有鍵（NULL），
        // 不能讓它們互相撞在一起。逐字對齊
        // db/migrations/0017_inventory_lot_wholesale_idempotency.sql。
        lot.HasIndex(value => new { value.TenantId, value.CreationIdempotencyKey })
            .IsUnique()
            .HasFilter("creation_idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_lot_tenant_creation_key");

        modelBuilder.AddPlatformTables();
    }
}

internal sealed class InventoryLotRepository(InventoryDbContext dbContext) : IInventoryLotRepository
{
    public void Add(LotAggregate lot) => dbContext.Lots.Add(lot);

    public Task<LotAggregate?> GetByCreationKeyAsync(
        TenantId tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.Lots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                lot => lot.TenantId == tenantId.Value
                    && lot.CreationIdempotencyKey == idempotencyKey,
                cancellationToken);

    public async Task<LotQueryPage> ListAsync(
        TenantId tenantId,
        AdminLotListRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Lots
            .AsNoTracking()
            .Where(lot => lot.TenantId == tenantId.Value);
        if (request.SkuId is { } skuId)
        {
            query = query.Where(lot => lot.SkuId == skuId.Value);
        }

        // 游標比對用 received_at 而不是 id：id 是 uuid，Guid 在 C# 沒有 `<`，
        // Npgsql 也不翻譯 Guid.CompareTo；比照 CampaignRepository 用時間欄位比大小。
        // 排序仍以 id 收尾，讓同一毫秒建立的批號有穩定順序。
        if (request.Cursor is { } cursor)
        {
            var cursorReceivedAt = await dbContext.Lots
                .AsNoTracking()
                .Where(lot => lot.TenantId == tenantId.Value && lot.Id == cursor.Value)
                .Select(lot => lot.ReceivedAt)
                .SingleOrDefaultAsync(cancellationToken);
            if (cursorReceivedAt is null)
            {
                return new LotQueryPage([], null);
            }

            query = query.Where(lot => lot.ReceivedAt < cursorReceivedAt);
        }

        var limit = request.Limit is > 0 ? request.Limit : 20;
        var rows = await query
            .OrderByDescending(lot => lot.ReceivedAt)
            .ThenByDescending(lot => lot.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasNext = rows.Length > limit;
        var page = rows.Take(limit).ToArray();
        return new LotQueryPage(page, hasNext ? new LotId(page[^1].Id) : null);
    }
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
