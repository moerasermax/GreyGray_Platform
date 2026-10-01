using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Abstractions.Saga;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class OrderingPaymentDeadlineIntegrationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "BE-64 D2：冪等 handler 的期限更新與新 timer 在存檔失敗時一起 rollback")]
    public async Task Instructions_handler_rolls_back_timer_when_order_save_fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(cancellationToken);
        await using var provider = BuildProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var order = CreateOrder("instructions-rollback");
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var originalDueAt = order.PaymentDueAt;

        await ExecuteSqlAsync(connectionString, """
            CREATE OR REPLACE FUNCTION ordering.reject_payment_due_update()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.payment_due_at IS DISTINCT FROM OLD.payment_due_at THEN
                    RAISE EXCEPTION 'intentional payment due failure';
                END IF;
                RETURN NEW;
            END
            $$;
            CREATE TRIGGER reject_payment_due_update
            BEFORE UPDATE ON ordering.orders
            FOR EACH ROW EXECUTE FUNCTION ordering.reject_payment_due_update();
            """, cancellationToken);

        var handler = scope.ServiceProvider
            .GetRequiredService<IIntegrationEventHandler<PaymentInstructionsIssued>>();
        var @event = new PaymentInstructionsIssued(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            PaymentId.New(),
            order.Id,
            PaymentMethod.Atm,
            Now + TimeSpan.FromHours(48));

        var failure = await Should.ThrowAsync<DbUpdateException>(() =>
            handler.HandleAsync(@event, cancellationToken));
        failure.InnerException.ShouldBeOfType<PostgresException>();

        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due';
            """, cancellationToken)).ShouldBe(0L);
        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.processed_message
            WHERE event_id = @event_id;
            """, cancellationToken, new NpgsqlParameter("event_id", @event.EventId))).ShouldBe(0L);
        (await ScalarAsync<DateTime>(connectionString, """
            SELECT payment_due_at FROM ordering.orders WHERE id = @order_id;
            """, cancellationToken, new NpgsqlParameter("order_id", order.Id.Value)))
            .ShouldBe(originalDueAt!.Value.UtcDateTime);

        await ExecuteSqlAsync(connectionString, """
            DROP TRIGGER reject_payment_due_update ON ordering.orders;
            DROP FUNCTION ordering.reject_payment_due_update();
            """, cancellationToken);
        await handler.HandleAsync(@event, cancellationToken);
        await handler.HandleAsync(@event, cancellationToken);
        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due' AND saga_id = @saga_id;
            """, cancellationToken, new NpgsqlParameter("saga_id", order.Id.ToString())))
            .ShouldBe(1L, "同一取號事件重送由 processed_message 擋住，不排第二個 timer。");
    }

    [Fact(DisplayName = "BE-64 D2：較舊的取號事件晚到，不會縮短期限或新增 timer")]
    public async Task Older_payment_instructions_do_not_shorten_deadline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(cancellationToken);
        await using var provider = BuildProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var order = CreateOrder("older-instructions");
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);

        var handler = scope.ServiceProvider
            .GetRequiredService<IIntegrationEventHandler<PaymentInstructionsIssued>>();
        var newer = new PaymentInstructionsIssued(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            PaymentId.New(),
            order.Id,
            PaymentMethod.Atm,
            Now + TimeSpan.FromHours(48));
        var older = new PaymentInstructionsIssued(
            Guid.CreateVersion7(),
            Now + TimeSpan.FromMinutes(1),
            TenantId.Default,
            PaymentId.New(),
            order.Id,
            PaymentMethod.Atm,
            Now + TimeSpan.FromHours(36));

        await handler.HandleAsync(newer, cancellationToken);
        await handler.HandleAsync(older, cancellationToken);

        (await ScalarAsync<DateTime>(connectionString, """
            SELECT payment_due_at FROM ordering.orders WHERE id = @order_id;
            """, cancellationToken, new NpgsqlParameter("order_id", order.Id.Value)))
            .ShouldBe(newer.ExpiresAt.UtcDateTime);
        (await ScalarAsync<DateTime>(connectionString, """
            SELECT payment_auto_cancel_at FROM ordering.orders WHERE id = @order_id;
            """, cancellationToken, new NpgsqlParameter("order_id", order.Id.Value)))
            .ShouldBe((newer.ExpiresAt + TimeSpan.FromDays(2)).UtcDateTime);
        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due' AND saga_id = @saga_id;
            """, cancellationToken, new NpgsqlParameter("saga_id", order.Id.ToString())))
            .ShouldBe(1L);
        (await CountProcessedAsync(connectionString, newer.EventId, cancellationToken))
            .ShouldBe(1L);
        (await CountProcessedAsync(connectionString, older.EventId, cancellationToken))
            .ShouldBe(1L);
    }

    [Fact(DisplayName = "BE-64 D6：xmin 擋父／子舊快照；HTTP 轉業務失敗，事件與 timer 衝突往上拋")]
    public async Task Xmin_guards_parent_and_child_updates_with_correct_error_boundaries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var schema = new OrderingDbContext(options))
        {
            await schema.Database.EnsureCreatedAsync(cancellationToken);
            schema.Orders.Add(CreateOrder("child-version"));
            schema.Orders.Add(CreateOrder("http-conflict"));
            schema.Orders.Add(CreateOrder("timer-conflict"));
            await schema.SaveChangesAsync(cancellationToken);
        }

        var childOrderId = await OrderIdByKeyAsync(connectionString, "child-version", cancellationToken);
        await using (var stale = new OrderingDbContext(options))
        await using (var winner = new OrderingDbContext(options))
        {
            await stale.Orders.Include(order => order.Lines)
                .SingleAsync(order => order.Id == childOrderId, cancellationToken);
            await winner.Orders.Include(order => order.Lines)
                .SingleAsync(order => order.Id == childOrderId, cancellationToken);
            var winnerService = CreateService(winner, new FakeClock(Now));
            var staleService = CreateService(stale, new FakeClock(Now));
            (await winnerService.CancelAdminAsync(
                childOrderId,
                "先取消",
                RefundDestination.OriginalPaymentMethod,
                cancellationToken)).IsSuccess.ShouldBeTrue();

            await Should.ThrowAsync<OrderingConcurrencyException>(() =>
                staleService.RecordShipmentDispatchedAsync([childOrderId], cancellationToken));
        }
        (await ReadStatusAsync(connectionString, childOrderId, cancellationToken))
            .ShouldBe(OrderStatus.Cancelled);

        var httpOrderId = await OrderIdByKeyAsync(connectionString, "http-conflict", cancellationToken);
        await using (var stale = new OrderingDbContext(options))
        await using (var winner = new OrderingDbContext(options))
        {
            await stale.Orders.Include(order => order.Lines)
                .SingleAsync(order => order.Id == httpOrderId, cancellationToken);
            await winner.Orders.Include(order => order.Lines)
                .SingleAsync(order => order.Id == httpOrderId, cancellationToken);
            await CreateService(winner, new FakeClock(Now)).RecordPaymentFailedAsync(
                httpOrderId,
                "temporary",
                cancellationToken);

            var result = await CreateService(stale, new FakeClock(Now)).CancelAdminAsync(
                httpOrderId,
                "舊畫面取消",
                RefundDestination.OriginalPaymentMethod,
                cancellationToken);
            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe("ordering.concurrent-update");

            var recovery = CreateOrder("http-conflict-recovery");
            stale.Orders.Add(recovery);
            await stale.SaveChangesAsync(cancellationToken);
            (await OrderIdByKeyAsync(
                connectionString,
                "http-conflict-recovery",
                cancellationToken)).ShouldBe(recovery.Id);
        }

        var timerOrderId = await OrderIdByKeyAsync(connectionString, "timer-conflict", cancellationToken);
        await using (var stale = new OrderingDbContext(options))
        await using (var winner = new OrderingDbContext(options))
        {
            await stale.Orders.Include(order => order.Lines)
                .SingleAsync(order => order.Id == timerOrderId, cancellationToken);
            await winner.Orders.Include(order => order.Lines)
                .SingleAsync(order => order.Id == timerOrderId, cancellationToken);
            await CreateService(winner, new FakeClock(Now)).RecordPaymentFailedAsync(
                timerOrderId,
                "temporary",
                cancellationToken);
            var timeoutService = CreateService(stale, new FakeClock(Now + TimeSpan.FromDays(2)));

            await Should.ThrowAsync<OrderingConcurrencyException>(() =>
                timeoutService.ResolvePaymentDueTimeoutAsync(timerOrderId, cancellationToken));
        }
    }

    [Fact(DisplayName = "BE-64 D5：取消後不同 PaymentCaptured EventId 只要求退款一次，PaymentRefunded 正常入帳")]
    public async Task Late_capture_with_different_event_ids_refunds_once_and_records_refund()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var order = CreateOrder("late-capture-distinct-events");
        await using (var seed = new OrderingDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync(cancellationToken);
            seed.Orders.Add(order);
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using (var cancelContext = new OrderingDbContext(options))
        {
            var cancelled = await CreateOutboxService(cancelContext, new FakeClock(Now))
                .CancelAdminAsync(
                    order.Id,
                    "付款逾期",
                    RefundDestination.OriginalPaymentMethod,
                    cancellationToken);
            cancelled.IsSuccess.ShouldBeTrue();
        }

        await using var provider = BuildProvider(connectionString);
        var paymentId = PaymentId.New();
        var firstCapture = CapturedEvent(order, paymentId, Guid.CreateVersion7());
        var secondCapture = CapturedEvent(order, paymentId, Guid.CreateVersion7());
        await DispatchAsync(provider, firstCapture, cancellationToken);
        await DispatchAsync(provider, secondCapture, cancellationToken);

        var refunded = new PaymentRefunded(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            RefundId.New(),
            paymentId,
            order.Id,
            null,
            PaymentProvider.ECPay,
            order.GrandTotal,
            RefundDestination.OriginalPaymentMethod);
        await DispatchAsync(provider, refunded, cancellationToken);

        await using var verify = new OrderingDbContext(options);
        var persisted = await verify.Orders.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == order.Id, cancellationToken);
        persisted.Status.ShouldBe(OrderStatus.Cancelled);
        persisted.PaidAmount.ShouldBe(order.GrandTotal);
        persisted.RefundedAmount.ShouldBe(order.GrandTotal);
        (await CountOutboxAsync(connectionString, RefundRequested.EventType, cancellationToken))
            .ShouldBe(1L);
        (await CountOutboxAsync(connectionString, OrderPaid.EventType, cancellationToken))
            .ShouldBe(0L);
        (await CountOutboxAsync(connectionString, OrderReadyToShip.EventType, cancellationToken))
            .ShouldBe(0L);
        (await CountProcessedAsync(connectionString, firstCapture.EventId, cancellationToken))
            .ShouldBe(1L);
        (await CountProcessedAsync(connectionString, secondCapture.EventId, cancellationToken))
            .ShouldBe(1L);
        (await CountProcessedAsync(connectionString, refunded.EventId, cancellationToken))
            .ShouldBe(1L);
        (await CountPoisonedOutboxAsync(connectionString, cancellationToken)).ShouldBe(0L);
    }

    [Fact(DisplayName = "BE-64 D6：取消先提交，舊入帳衝突後 handler 重試只發一筆退款")]
    public async Task Cancellation_wins_then_capture_handler_retry_is_exactly_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var order = CreateOrder("cancel-wins-capture-retry");
        await using (var seed = new OrderingDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync(cancellationToken);
            seed.Orders.Add(order);
            await seed.SaveChangesAsync(cancellationToken);
        }

        var captured = CapturedEvent(order, PaymentId.New(), Guid.CreateVersion7());
        await using (var staleCapture = new OrderingDbContext(options))
        await using (var cancellationWinner = new OrderingDbContext(options))
        {
            await staleCapture.Orders.Include(candidate => candidate.Lines)
                .SingleAsync(candidate => candidate.Id == order.Id, cancellationToken);
            var cancelled = await CreateOutboxService(cancellationWinner, new FakeClock(Now))
                .CancelAdminAsync(
                    order.Id,
                    "付款逾期",
                    RefundDestination.OriginalPaymentMethod,
                    cancellationToken);
            cancelled.IsSuccess.ShouldBeTrue();

            var staleHandler = new PaymentCapturedHandler(
                CreateOutboxService(staleCapture, new FakeClock(Now)));
            await Should.ThrowAsync<OrderingConcurrencyException>(() =>
                staleHandler.HandleAsync(captured, cancellationToken));
        }

        await using var provider = BuildProvider(connectionString);
        await DispatchAsync(provider, captured, cancellationToken);
        await DispatchAsync(provider, captured, cancellationToken);

        (await ReadStatusAsync(connectionString, order.Id, cancellationToken))
            .ShouldBe(OrderStatus.Cancelled);
        (await CountOutboxAsync(connectionString, OrderCancelled.EventType, cancellationToken))
            .ShouldBe(1L);
        (await CountOutboxAsync(connectionString, RefundRequested.EventType, cancellationToken))
            .ShouldBe(1L);
        (await CountOutboxAsync(connectionString, OrderPaid.EventType, cancellationToken))
            .ShouldBe(0L);
        (await CountProcessedAsync(connectionString, captured.EventId, cancellationToken))
            .ShouldBe(1L);
        (await CountPoisonedOutboxAsync(connectionString, cancellationToken)).ShouldBe(0L);
    }

    [Fact(DisplayName = "BE-64 D6：入帳先提交，舊逾期 timer 衝突後 handler 重試為 no-op")]
    public async Task Capture_wins_then_timeout_handler_retry_is_no_op()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var order = CreateOrder("capture-wins-timeout-retry");
        await using (var seed = new OrderingDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync(cancellationToken);
            seed.Orders.Add(order);
            await seed.SaveChangesAsync(cancellationToken);
        }

        var captured = CapturedEvent(order, PaymentId.New(), Guid.CreateVersion7());
        await using (var staleTimeout = new OrderingDbContext(options))
        {
            await staleTimeout.Orders.Include(candidate => candidate.Lines)
                .SingleAsync(candidate => candidate.Id == order.Id, cancellationToken);

            await using var captureProvider = BuildProvider(connectionString);
            await DispatchAsync(captureProvider, captured, cancellationToken);
            await DispatchAsync(captureProvider, captured, cancellationToken);

            var staleHandler = new PaymentDueTimeoutHandler(
                CreateOutboxService(
                    staleTimeout,
                    new FakeClock(Now + TimeSpan.FromDays(2))));
            await Should.ThrowAsync<OrderingConcurrencyException>(() =>
                staleHandler.HandleTimeoutAsync(order.Id.ToString(), "{}", cancellationToken));
        }

        await using var retryProvider = BuildProvider(
            connectionString,
            Now + TimeSpan.FromDays(2));
        await DispatchPaymentDueTimeoutAsync(retryProvider, order.Id, cancellationToken);
        await DispatchPaymentDueTimeoutAsync(retryProvider, order.Id, cancellationToken);

        (await ReadStatusAsync(connectionString, order.Id, cancellationToken))
            .ShouldBe(OrderStatus.PaidAwaitingClose);
        (await CountOutboxAsync(connectionString, OrderPaid.EventType, cancellationToken))
            .ShouldBe(1L);
        (await CountOutboxAsync(connectionString, OrderCancelled.EventType, cancellationToken))
            .ShouldBe(0L);
        (await CountOutboxAsync(connectionString, RefundRequested.EventType, cancellationToken))
            .ShouldBe(0L);
        (await CountProcessedAsync(connectionString, captured.EventId, cancellationToken))
            .ShouldBe(1L);
        (await CountPoisonedOutboxAsync(connectionString, cancellationToken)).ShouldBe(0L);
    }

    private ServiceProvider BuildProvider(
        string connectionString,
        DateTimeOffset? now = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GreyGray_ordering"] = connectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FakeClock(now ?? Now));
        services.AddSingleton<ICorrelationContext>(new FakeCorrelation());
        services.AddSingleton<IPricingQuotation>(new UnusedPricing());
        services.AddSingleton(EventTypeRegistry.FromAssemblies([typeof(OrderPlaced).Assembly]));
        services.AddOrderingModule(configuration);
        return services.BuildServiceProvider();
    }

    private static OrderingApplicationService CreateService(
        OrderingDbContext dbContext,
        FakeClock clock) =>
        new(
            new OrderingRepository(dbContext),
            dbContext,
            new FakeEventPublisher(),
            new UnusedPricing(),
            clock,
            new FakeCorrelation());

    private static OrderingApplicationService CreateOutboxService(
        OrderingDbContext dbContext,
        FakeClock clock)
    {
        var correlation = new FakeCorrelation();
        return new OrderingApplicationService(
            new OrderingRepository(dbContext),
            dbContext,
            new OutboxEventPublisher<OrderingDbContext>(
                dbContext,
                correlation,
                EventTypeRegistry.FromAssemblies([typeof(OrderPlaced).Assembly])),
            new UnusedPricing(),
            clock,
            correlation);
    }

    private static PaymentCaptured CapturedEvent(
        Order order,
        PaymentId paymentId,
        Guid eventId) =>
        new(
            eventId,
            Now,
            TenantId.Default,
            paymentId,
            order.Id,
            PaymentProvider.ECPay,
            order.GrandTotal,
            Money.OfMajor(100, Currency.TWD),
            Money.OfMajor(60, Currency.TWD),
            $"BE64-{eventId:N}");

    private static async Task DispatchAsync<TEvent>(
        ServiceProvider provider,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        await using var scope = provider.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider
                     .GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }

    private static async Task DispatchPaymentDueTimeoutAsync(
        ServiceProvider provider,
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentDueTimeoutHandler>()
            .HandleTimeoutAsync(orderId.ToString(), "{}", cancellationToken);
    }

    private static Order CreateOrder(string key)
    {
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
            ["測試運費"],
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
            key);
        return Order.Place(OrderId.New(), checkout, snapshot, Now).Value;
    }

    private async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var databaseName = $"ordering_due_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task<OrderId> OrderIdByKeyAsync(
        string connectionString,
        string key,
        CancellationToken cancellationToken) =>
        new(await ScalarAsync<Guid>(connectionString, """
            SELECT id FROM ordering.orders WHERE checkout_idempotency_key = @key;
            """, cancellationToken, new NpgsqlParameter("key", key)));

    private static async Task<OrderStatus> ReadStatusAsync(
        string connectionString,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        (OrderStatus)await ScalarAsync<short>(connectionString, """
            SELECT status FROM ordering.orders WHERE id = @order_id;
            """, cancellationToken, new NpgsqlParameter("order_id", orderId.Value));

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

    private static Task<long> CountPoisonedOutboxAsync(
        string connectionString,
        CancellationToken cancellationToken) =>
        ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.outbox_message
            WHERE dead_lettered OR attempts <> 0 OR last_error IS NOT NULL;
            """, cancellationToken);

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

    private sealed class UnusedPricing : IPricingQuotation
    {
        public Task<Result<PricingSnapshot>> QuoteAsync(
            QuoteRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<PricingSnapshotId>> FreezeAsync(
            PricingSnapshot snapshot,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<PricingSnapshot>> GetSnapshotAsync(
            PricingSnapshotId id,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
