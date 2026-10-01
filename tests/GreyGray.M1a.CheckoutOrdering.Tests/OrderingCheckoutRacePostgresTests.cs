using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Platform.Saga;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class OrderingCheckoutRacePostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 2, 6, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "BE-69 R1：Host 無外層交易相撞後回贏家，孤兒 timer 安靜 no-op")]
    public async Task Host_race_returns_winner_and_orphan_timer_is_a_noop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await CreateScenarioAsync("host-race", cancellationToken);
        await using var loserContext = new OrderingDbContext(scenario.Options);
        OrderView? winner = null;
        var repository = new FirstCheckoutMissRepository(
            new OrderingRepository(loserContext),
            async () => winner = await CreateWinnerAsync(scenario, scenario.Checkout, cancellationToken));
        var loser = CreateService(loserContext, repository, scenario);

        var result = await loser.CreateFromCheckoutAsync(scenario.Checkout, cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        winner.ShouldNotBeNull();
        result.Value.Id.ShouldBe(winner.Id);
        (await CountOrdersAsync(scenario.ConnectionString, scenario.Checkout.CartId, cancellationToken))
            .ShouldBe(1L);
        (await CountOutboxAsync(scenario.ConnectionString, OrderPlaced.EventType, cancellationToken))
            .ShouldBe(1L);
        (await CountOutboxAsync(scenario.ConnectionString, PaymentRequested.EventType, cancellationToken))
            .ShouldBe(1L);

        var timerSagaIds = await QueryStringsAsync(scenario.ConnectionString, """
            SELECT saga_id FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due'
            ORDER BY saga_id;
            """, cancellationToken);
        timerSagaIds.Count.ShouldBe(2);
        timerSagaIds.ShouldContain(winner.Id.ToString());
        var orphanSagaId = timerSagaIds.Single(sagaId => sagaId != winner.Id.ToString());
        (await CountOrdersByIdAsync(
            scenario.ConnectionString,
            Guid.Parse(orphanSagaId),
            cancellationToken)).ShouldBe(0L);

        await using var timeoutContext = new OrderingDbContext(scenario.Options);
        var timeoutClock = new FakeClock(Now + TimeSpan.FromDays(2));
        var timeoutService = new OrderingApplicationService(
            new OrderingRepository(timeoutContext),
            timeoutContext,
            new FakeEventPublisher(),
            scenario.Pricing,
            timeoutClock,
            new FakeCorrelation());
        var timeoutHandler = new PaymentDueTimeoutHandler(timeoutService);
        await Should.NotThrowAsync(() => timeoutHandler.HandleTimeoutAsync(
            orphanSagaId,
            "{}",
            cancellationToken));

        (await ReadStatusAsync(scenario.ConnectionString, winner.Id, cancellationToken))
            .ShouldBe(OrderStatus.AwaitingPayment);
        (await CountOutboxAsync(scenario.ConnectionString, OrderCancelled.EventType, cancellationToken))
            .ShouldBe(0L);
    }

    [Fact(DisplayName = "BE-69 R2：異鍵相撞回 checkout-already-processed，不丟例外")]
    public async Task Different_key_race_returns_business_failure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await CreateScenarioAsync("loser-key", cancellationToken);
        var winnerCheckout = scenario.Checkout with { IdempotencyKey = "winner-key" };
        await using var loserContext = new OrderingDbContext(scenario.Options);
        var repository = new FirstCheckoutMissRepository(
            new OrderingRepository(loserContext),
            () => CreateWinnerAsync(scenario, winnerCheckout, cancellationToken));
        var loser = CreateService(loserContext, repository, scenario);

        var result = await loser.CreateFromCheckoutAsync(scenario.Checkout, cancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ordering.checkout-already-processed");
        (await CountOrdersAsync(scenario.ConnectionString, scenario.Checkout.CartId, cancellationToken))
            .ShouldBe(1L);
    }

    [Fact(DisplayName = "BE-69 R3：Worker 交易內相撞可重讀、commit marker，事件重送不重複")]
    public async Task Worker_transaction_race_commits_marker_and_replays_as_noop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await CreateScenarioAsync("worker-race", cancellationToken);
        await using var loserContext = new OrderingDbContext(scenario.Options);
        OrderView? winner = null;
        var repository = new FirstCheckoutMissRepository(
            new OrderingRepository(loserContext),
            async () => winner = await CreateWinnerAsync(scenario, scenario.Checkout, cancellationToken));
        var loser = CreateService(loserContext, repository, scenario);
        var inner = new CheckoutCompletedHandler(loser);
        var handler = new IdempotentIntegrationEventHandler<
            CheckoutCompleted,
            CheckoutCompletedHandler,
            OrderingDbContext>(inner, loserContext, scenario.Clock);

        await Should.NotThrowAsync(() => handler.HandleAsync(scenario.Checkout, cancellationToken));
        await Should.NotThrowAsync(() => handler.HandleAsync(scenario.Checkout, cancellationToken));

        winner.ShouldNotBeNull();
        (await CountOrdersAsync(scenario.ConnectionString, scenario.Checkout.CartId, cancellationToken))
            .ShouldBe(1L);
        (await CountProcessedAsync(
            scenario.ConnectionString,
            scenario.Checkout.EventId,
            cancellationToken)).ShouldBe(1L);
        (await CountOutboxAsync(scenario.ConnectionString, OrderPlaced.EventType, cancellationToken))
            .ShouldBe(1L);
        (await CountOutboxAsync(scenario.ConnectionString, PaymentRequested.EventType, cancellationToken))
            .ShouldBe(1L);
        (await CountTimersAsync(scenario.ConnectionString, cancellationToken))
            .ShouldBe(2L, "贏家 timer 與交易內提交的輸家孤兒 timer 各一筆。");
    }

    [Fact(DisplayName = "BE-69 R4：checkout_event 唯一索引翻譯後，購物車重讀仍為 null 就重拋")]
    public async Task Checkout_event_constraint_is_translated_and_null_reread_rethrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await CreateScenarioAsync("event-winner", cancellationToken);
        await CreateWinnerAsync(scenario, scenario.Checkout, cancellationToken);
        var loserCheckout = scenario.Checkout with
        {
            CartId = CartId.New(),
            IdempotencyKey = "event-loser",
        };
        await using var loserContext = new OrderingDbContext(scenario.Options);
        var loser = CreateService(
            loserContext,
            new OrderingRepository(loserContext),
            scenario);

        var failure = await Should.ThrowAsync<OrderingCheckoutAlreadyPlacedException>(() =>
            loser.CreateFromCheckoutAsync(loserCheckout, cancellationToken));

        var postgres = failure.InnerException.ShouldBeOfType<DbUpdateException>()
            .InnerException.ShouldBeOfType<PostgresException>();
        postgres.ConstraintName.ShouldBe("ux_orders_checkout_event");
        (await CountAllOrdersAsync(scenario.ConnectionString, cancellationToken)).ShouldBe(1L);
    }

    [Fact(DisplayName = "BE-69 R5：其他 23505 constraint 維持原 DbUpdateException")]
    public async Task Other_unique_constraint_keeps_original_exception()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await CreateScenarioAsync("shared-key", cancellationToken);
        await CreateWinnerAsync(scenario, scenario.Checkout, cancellationToken);
        var loserCheckout = scenario.Checkout with
        {
            EventId = Guid.CreateVersion7(),
            CartId = CartId.New(),
        };
        await using var loserContext = new OrderingDbContext(scenario.Options);
        var loser = CreateService(
            loserContext,
            new OrderingRepository(loserContext),
            scenario);

        var failure = await Should.ThrowAsync<DbUpdateException>(() =>
            loser.CreateFromCheckoutAsync(loserCheckout, cancellationToken));

        failure.ShouldNotBeOfType<OrderingCheckoutAlreadyPlacedException>();
        failure.InnerException.ShouldBeOfType<PostgresException>()
            .ConstraintName.ShouldBe("ux_orders_tenant_checkout_key");
        (await CountAllOrdersAsync(scenario.ConnectionString, cancellationToken)).ShouldBe(1L);
    }

    private async Task<Scenario> CreateScenarioAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var connectionString = await CreateDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var schema = new OrderingDbContext(options))
        {
            await schema.Database.EnsureCreatedAsync(cancellationToken);
        }

        var snapshot = new PricingSnapshot(
            PricingSnapshotId.New(),
            DeliveryMethod.ConvenienceStore,
            300,
            0,
            300,
            Money.OfMajor(60, Currency.TWD),
            FeeRuleSetId.New(),
            FeeRuleId.New(),
            ShippingStrategyKind.Flat,
            ["BE-69 競態測試運費"],
            Now);
        var checkout = new CheckoutCompleted(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            CartId.New(),
            CustomerId.New(),
            null,
            DeliveryMethod.ConvenienceStore,
            ShippingPolicy.HoldUntilComplete,
            snapshot.Id,
            [
                new CheckoutLine(
                    SkuId.New(),
                    FulfillmentMode.Preorder,
                    CampaignId.New(),
                    CampaignOfferId.New(),
                    1,
                    Money.OfMajor(100, Currency.TWD)),
            ],
            idempotencyKey);
        return new Scenario(
            connectionString,
            options,
            new FixedPricing(snapshot),
            new FakeClock(Now),
            checkout);
    }

    private static OrderingApplicationService CreateService(
        OrderingDbContext dbContext,
        IOrderRepository repository,
        Scenario scenario)
    {
        var correlation = new FakeCorrelation();
        return new OrderingApplicationService(
            repository,
            dbContext,
            new OutboxEventPublisher<OrderingDbContext>(
                dbContext,
                correlation,
                EventTypeRegistry.FromAssemblies([typeof(OrderPlaced).Assembly])),
            scenario.Pricing,
            scenario.Clock,
            correlation,
            timerScheduler: new SagaTimerScheduler<OrderingDbContext>(
                dbContext,
                scenario.Clock));
    }

    private static async Task<OrderView> CreateWinnerAsync(
        Scenario scenario,
        CheckoutCompleted checkout,
        CancellationToken cancellationToken)
    {
        await using var winnerContext = new OrderingDbContext(scenario.Options);
        var winner = CreateService(
            winnerContext,
            new OrderingRepository(winnerContext),
            scenario);
        var result = await winner.CreateFromCheckoutAsync(checkout, cancellationToken);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var databaseName = $"ordering_race_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static Task<long> CountOrdersAsync(
        string connectionString,
        CartId cartId,
        CancellationToken cancellationToken) =>
        ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM ordering.orders WHERE checkout_cart_id = @cart_id;
            """, cancellationToken, new NpgsqlParameter("cart_id", cartId.Value));

    private static Task<long> CountOrdersByIdAsync(
        string connectionString,
        Guid orderId,
        CancellationToken cancellationToken) =>
        ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM ordering.orders WHERE id = @order_id;
            """, cancellationToken, new NpgsqlParameter("order_id", orderId));

    private static Task<long> CountAllOrdersAsync(
        string connectionString,
        CancellationToken cancellationToken) =>
        ScalarAsync<long>(
            connectionString,
            "SELECT count(*) FROM ordering.orders;",
            cancellationToken);

    private static Task<long> CountOutboxAsync(
        string connectionString,
        string eventType,
        CancellationToken cancellationToken) =>
        ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.outbox_message WHERE event_type = @event_type;
            """, cancellationToken, new NpgsqlParameter("event_type", eventType));

    private static Task<long> CountProcessedAsync(
        string connectionString,
        Guid eventId,
        CancellationToken cancellationToken) =>
        ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.processed_message WHERE event_id = @event_id;
            """, cancellationToken, new NpgsqlParameter("event_id", eventId));

    private static Task<long> CountTimersAsync(
        string connectionString,
        CancellationToken cancellationToken) =>
        ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due';
            """, cancellationToken);

    private static async Task<OrderStatus> ReadStatusAsync(
        string connectionString,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        (OrderStatus)await ScalarAsync<short>(connectionString, """
            SELECT status FROM ordering.orders WHERE id = @order_id;
            """, cancellationToken, new NpgsqlParameter("order_id", orderId.Value));

    private static async Task ExecuteSqlAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(
        string connectionString,
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("查詢未回傳 scalar。"));
    }

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private sealed record Scenario(
        string ConnectionString,
        DbContextOptions<OrderingDbContext> Options,
        FixedPricing Pricing,
        FakeClock Clock,
        CheckoutCompleted Checkout);

    private sealed class FixedPricing(PricingSnapshot snapshot) : IPricingQuotation
    {
        public Task<Result<PricingSnapshot>> QuoteAsync(
            QuoteRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<PricingSnapshotId>> FreezeAsync(
            PricingSnapshot value,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<PricingSnapshot>> GetSnapshotAsync(
            PricingSnapshotId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(id == snapshot.Id
                ? Result<PricingSnapshot>.Success(snapshot)
                : Result<PricingSnapshot>.Failure(
                    "pricing.snapshot-not-found",
                    "找不到報價快照。"));
    }

    private sealed class FirstCheckoutMissRepository(
        IOrderRepository inner,
        Func<Task> afterFirstMiss) : IOrderRepository
    {
        private bool _firstCheckoutLookup = true;

        public Task<Order?> GetAsync(
            TenantId tenantId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            inner.GetAsync(tenantId, orderId, cancellationToken);

        public async Task<Order?> GetByCheckoutAsync(
            TenantId tenantId,
            CartId cartId,
            CancellationToken cancellationToken)
        {
            if (!_firstCheckoutLookup)
            {
                return await inner.GetByCheckoutAsync(tenantId, cartId, cancellationToken);
            }

            _firstCheckoutLookup = false;
            var beforeWinner = await inner.GetByCheckoutAsync(
                tenantId,
                cartId,
                cancellationToken);
            beforeWinner.ShouldBeNull("輸家的第一次查詢必須發生在贏家寫入之前。");
            await afterFirstMiss();
            return null;
        }

        public Task<OrderQueryPage> ListCustomerAsync(
            TenantId tenantId,
            CustomerOrderListRequest request,
            CancellationToken cancellationToken) =>
            inner.ListCustomerAsync(tenantId, request, cancellationToken);

        public Task<OrderQueryPage> ListAdminAsync(
            TenantId tenantId,
            AdminOrderListRequest request,
            CancellationToken cancellationToken) =>
            inner.ListAdminAsync(tenantId, request, cancellationToken);

        public Task<IReadOnlyList<Order>> GetByCampaignAsync(
            TenantId tenantId,
            CampaignId campaignId,
            CancellationToken cancellationToken) =>
            inner.GetByCampaignAsync(tenantId, campaignId, cancellationToken);

        public Task<Order?> GetByLineAsync(
            TenantId tenantId,
            OrderLineId orderLineId,
            CancellationToken cancellationToken) =>
            inner.GetByLineAsync(tenantId, orderLineId, cancellationToken);

        public void Add(Order order) => inner.Add(order);
    }
}

public sealed class OrderingCheckoutConstraintTranslationTests
{
    [Theory(DisplayName = "BE-69 R4/R5：翻譯判斷只接受兩個指定的 23505 constraint")]
    [InlineData("23505", "ux_orders_tenant_checkout_cart", true)]
    [InlineData("23505", "ux_orders_checkout_event", true)]
    [InlineData("23505", "ux_orders_tenant_checkout_key", false)]
    [InlineData("23514", "ux_orders_tenant_checkout_cart", false)]
    public void Translation_predicate_is_exact(
        string sqlState,
        string constraintName,
        bool expected)
    {
        var exception = new PostgresException(
            "測試錯誤",
            "ERROR",
            "ERROR",
            sqlState,
            constraintName: constraintName);

        OrderingDbContext.IsCheckoutAlreadyPlaced(exception).ShouldBe(expected);
    }
}
