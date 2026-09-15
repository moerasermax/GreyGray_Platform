using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.IdentityCatalog.Tests;

/// <summary>使用正式 migration 鏈驗 Catalog 最愛的 SQL 可見規則、併發與 keyset cursor。</summary>
public sealed class CatalogFavoriteIntegrationTests : IAsyncLifetime
{
    private static readonly TenantId OtherTenant = new(
        new Guid("00000000-0000-0000-0000-000000000002"));
    private static readonly DateTimeOffset InitialTime =
        new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await ExecuteMigrationChainAsync(
            _postgres.GetConnectionString(),
            TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "正式 DB：最愛可見規則、冪等併發、tenant 與雙欄 cursor 邊界完整")]
    public async Task Favorites_obey_visibility_concurrency_tenant_and_cursor_boundaries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = _postgres.GetConnectionString();
        var clock = new MutableClock(InitialTime);
        var correlation = new MutableCorrelation(TenantId.Default);
        await using var provider = BuildProvider(connectionString, clock, correlation);
        var customerA = CustomerId.New();
        var customerB = CustomerId.New();

        var priced = ProductId.New();
        var unpriced = ProductId.New();
        var inactive = ProductId.New();
        var noActiveSku = ProductId.New();
        await InsertProductAsync(connectionString, priced, true, true, true, "priced", cancellationToken);
        await InsertProductAsync(connectionString, unpriced, true, true, false, "unpriced", cancellationToken);
        await InsertProductAsync(connectionString, inactive, false, true, true, "inactive", cancellationToken);
        await InsertProductAsync(connectionString, noActiveSku, true, false, true, "no active sku", cancellationToken);

        await using (var scope = provider.CreateAsyncScope())
        {
            var favorites = scope.ServiceProvider.GetRequiredService<IStorefrontFavorites>();
            favorites.ShouldBe(scope.ServiceProvider.GetRequiredService<IStorefrontFavorites>());

            (await favorites.AddAsync(customerA, ProductId.New(), cancellationToken)).Error.Code
                .ShouldBe("catalog.product-not-found");
            (await favorites.AddAsync(customerA, inactive, cancellationToken)).Error.Code
                .ShouldBe("catalog.product-not-found");
            (await favorites.AddAsync(customerA, noActiveSku, cancellationToken)).Error.Code
                .ShouldBe("catalog.product-not-found");

            (await favorites.AddAsync(customerA, priced, cancellationToken)).IsSuccess.ShouldBeTrue();
            var firstCreatedAt = await FavoriteCreatedAtAsync(
                connectionString, TenantId.Default, customerA, priced, cancellationToken);
            clock.UtcNow = InitialTime.AddDays(1);
            (await favorites.AddAsync(customerA, priced, cancellationToken)).IsSuccess.ShouldBeTrue();
            (await FavoriteCreatedAtAsync(
                connectionString, TenantId.Default, customerA, priced, cancellationToken))
                .ShouldBe(firstCreatedAt);

            clock.UtcNow = InitialTime.AddMinutes(1);
            (await favorites.AddAsync(customerA, unpriced, cancellationToken)).IsSuccess.ShouldBeTrue();
            var unpricedPage = (await favorites.ListAsync(customerA, null, 100, cancellationToken)).Value;
            unpricedPage.Items.Single(item => item.Id == unpriced).PriceFrom.ShouldBeNull();

            (await favorites.ListAsync(customerA, null, 0, cancellationToken)).Error.Code
                .ShouldBe("catalog.invalid-cursor");
            (await favorites.ListAsync(customerA, null, 101, cancellationToken)).Error.Code
                .ShouldBe("catalog.invalid-cursor");
            (await favorites.ListAsync(customerA, "not-base64", 20, cancellationToken)).Error.Code
                .ShouldBe("catalog.invalid-cursor");
            var empty = (await favorites.ListAsync(customerB, null, 20, cancellationToken)).Value;
            empty.Items.ShouldBeEmpty();
            empty.NextCursor.ShouldBeNull();
            (await favorites.FindAsync(customerA, [], cancellationToken)).ShouldBeEmpty();
        }

        await using (var disconnectedProvider = BuildProvider(
                         "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1",
                         clock,
                         correlation))
        await using (var disconnectedScope = disconnectedProvider.CreateAsyncScope())
        {
            (await disconnectedScope.ServiceProvider.GetRequiredService<IStorefrontFavorites>()
                    .FindAsync(customerA, [], cancellationToken))
                .ShouldBeEmpty();
        }

        var concurrent = ProductId.New();
        await InsertProductAsync(connectionString, concurrent, true, true, true, "concurrent", cancellationToken);
        clock.UtcNow = InitialTime.AddMinutes(2);
        await using (var firstScope = provider.CreateAsyncScope())
        await using (var secondScope = provider.CreateAsyncScope())
        {
            var barrier = new AsyncBarrier(2);
            var first = new BarrierFavorites(
                firstScope.ServiceProvider.GetRequiredService<IStorefrontFavorites>(), barrier);
            var second = new BarrierFavorites(
                secondScope.ServiceProvider.GetRequiredService<IStorefrontFavorites>(), barrier);
            var results = await Task.WhenAll(
                first.AddAsync(customerA, concurrent, cancellationToken),
                second.AddAsync(customerA, concurrent, cancellationToken));
            results.ShouldAllBe(result => result.IsSuccess);
        }

        (await FavoriteCountAsync(
            connectionString, TenantId.Default, customerA, concurrent, cancellationToken)).ShouldBe(1);

        var tied = new[] { ProductId.New(), ProductId.New(), ProductId.New() };
        foreach (var (productId, index) in tied.Select((value, index) => (value, index)))
        {
            await InsertProductAsync(
                connectionString, productId, true, true, true, $"tie-{index}", cancellationToken);
        }

        var tiedCustomer = CustomerId.New();
        clock.UtcNow = InitialTime.AddMinutes(3);
        await using (var scope = provider.CreateAsyncScope())
        {
            var favorites = scope.ServiceProvider.GetRequiredService<IStorefrontFavorites>();
            foreach (var productId in tied)
            {
                (await favorites.AddAsync(tiedCustomer, productId, cancellationToken)).IsSuccess.ShouldBeTrue();
            }

            var seen = new List<ProductId>();
            string? cursor = null;
            do
            {
                var page = (await favorites.ListAsync(tiedCustomer, cursor, 1, cancellationToken)).Value;
                page.Items.Count.ShouldBe(1);
                seen.Add(page.Items[0].Id);
                cursor = page.NextCursor;
            }
            while (cursor is not null);

            seen.Count.ShouldBe(3);
            seen.Distinct().ToHashSet().SetEquals(tied).ShouldBeTrue();
        }

        var newest = ProductId.New();
        var middle = ProductId.New();
        var oldest = ProductId.New();
        foreach (var (productId, name) in new[]
                 {
                     (newest, "newest"), (middle, "middle"), (oldest, "oldest"),
                 })
        {
            await InsertProductAsync(connectionString, productId, true, true, true, name, cancellationToken);
        }

        var visibilityCustomer = CustomerId.New();
        await using (var scope = provider.CreateAsyncScope())
        {
            var favorites = scope.ServiceProvider.GetRequiredService<IStorefrontFavorites>();
            clock.UtcNow = InitialTime.AddMinutes(30);
            await favorites.AddAsync(visibilityCustomer, newest, cancellationToken);
            clock.UtcNow = InitialTime.AddMinutes(20);
            await favorites.AddAsync(visibilityCustomer, middle, cancellationToken);
            clock.UtcNow = InitialTime.AddMinutes(10);
            await favorites.AddAsync(visibilityCustomer, oldest, cancellationToken);

            var firstPage = (await favorites.ListAsync(
                visibilityCustomer, null, 1, cancellationToken)).Value;
            firstPage.Items.Single().Id.ShouldBe(newest);
            firstPage.NextCursor.ShouldNotBeNull();
            await ExecuteSqlAsync(connectionString, $"""
                UPDATE catalog.product SET is_active = false WHERE id = '{middle.Value}'::uuid;
                """, cancellationToken);

            var pageAfterDown = (await favorites.ListAsync(
                visibilityCustomer, firstPage.NextCursor, 1, cancellationToken)).Value;
            pageAfterDown.Items.Single().Id.ShouldBe(oldest);
            pageAfterDown.NextCursor.ShouldBeNull();

            var page = (await favorites.ListAsync(visibilityCustomer, null, 2, cancellationToken)).Value;
            page.Items.Select(item => item.Id).ShouldBe([newest, oldest]);
            page.NextCursor.ShouldBeNull();

            await ExecuteSqlAsync(connectionString, $"""
                UPDATE catalog.product SET is_active = true WHERE id = '{middle.Value}'::uuid;
                """, cancellationToken);
            (await favorites.ListAsync(visibilityCustomer, null, 3, cancellationToken)).Value.Items.Count
                .ShouldBe(3);

            await ExecuteSqlAsync(connectionString, $"""
                UPDATE catalog.product SET is_active = false WHERE id = '{middle.Value}'::uuid;
                """, cancellationToken);
            (await favorites.RemoveAsync(visibilityCustomer, middle, cancellationToken)).IsSuccess.ShouldBeTrue();
            await ExecuteSqlAsync(connectionString, $"""
                UPDATE catalog.product SET is_active = true WHERE id = '{middle.Value}'::uuid;
                """, cancellationToken);
            (await favorites.ListAsync(visibilityCustomer, null, 3, cancellationToken)).Value.Items
                .ShouldNotContain(item => item.Id == middle);

            (await favorites.FindAsync(customerB, [priced], cancellationToken)).ShouldBeEmpty();
            correlation.TenantId = OtherTenant;
            (await favorites.FindAsync(customerA, [priced], cancellationToken)).ShouldBeEmpty();
            (await favorites.ListAsync(customerA, null, 20, cancellationToken)).Value.Items.ShouldBeEmpty();
            (await favorites.AddAsync(customerA, priced, cancellationToken)).Error.Code
                .ShouldBe("catalog.product-not-found");
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        MutableClock clock,
        MutableCorrelation correlation)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GreyGray_catalog"] = connectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ICorrelationContext>(correlation);
        services.AddCatalogModule(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task InsertProductAsync(
        string connectionString,
        ProductId productId,
        bool isActive,
        bool skuIsActive,
        bool priced,
        string name,
        CancellationToken cancellationToken)
    {
        var skuId = SkuId.New();
        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO catalog.product (
                id, tenant_id, name, mode, is_active, created_at)
            VALUES (
                '{productId.Value}'::uuid, '{TenantId.Default.Value}'::uuid,
                '{name}', 0, {isActive.ToString().ToLowerInvariant()}, now());
            INSERT INTO catalog.product_image (product_id, position, url)
            VALUES ('{productId.Value}'::uuid, 0, 'https://example.invalid/{name}.jpg');
            INSERT INTO catalog.sku (
                id, tenant_id, product_id, name, weight_gram,
                length_cm, width_cm, height_cm, unit_of_measure, unit_count,
                list_price_amount_minor, list_price_currency, is_active, created_at)
            VALUES (
                '{skuId.Value}'::uuid, '{TenantId.Default.Value}'::uuid,
                '{productId.Value}'::uuid, '{name} sku', 0, 0, 0, 0, '件', 1,
                {(priced ? "100" : "NULL")}, {(priced ? "'TWD'" : "NULL")},
                {skuIsActive.ToString().ToLowerInvariant()}, now());
            """, cancellationToken);
    }

    private static async Task<long> FavoriteCreatedAtAsync(
        string connectionString,
        TenantId tenantId,
        CustomerId customerId,
        ProductId productId,
        CancellationToken cancellationToken) =>
        await ScalarAsync<long>(connectionString, $"""
            SELECT (extract(epoch FROM created_at) * 1000000)::bigint
            FROM catalog.favorite
            WHERE tenant_id = '{tenantId.Value}'::uuid
              AND customer_id = '{customerId.Value}'::uuid
              AND product_id = '{productId.Value}'::uuid;
            """, cancellationToken);

    private static async Task<int> FavoriteCountAsync(
        string connectionString,
        TenantId tenantId,
        CustomerId customerId,
        ProductId productId,
        CancellationToken cancellationToken) =>
        await ScalarAsync<int>(connectionString, $"""
            SELECT count(*)::int FROM catalog.favorite
            WHERE tenant_id = '{tenantId.Value}'::uuid
              AND customer_id = '{customerId.Value}'::uuid
              AND product_id = '{productId.Value}'::uuid;
            """, cancellationToken);

    private static async Task ExecuteMigrationChainAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var root = FindRepositoryRoot();
        foreach (var path in Directory.GetFiles(Path.Combine(root, "db", "migrations"), "*.sql")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var sql = string.Join(
                Environment.NewLine,
                File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
            await ExecuteSqlAsync(connectionString, sql, cancellationToken);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GreyGray.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Cannot locate GreyGray.slnx from test output directory.");
    }

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
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Expected scalar value."));
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public DateOnly TodayInTaipei => DateOnly.FromDateTime(UtcNow.AddHours(8).DateTime);
    }

    private sealed class MutableCorrelation(TenantId tenantId) : ICorrelationContext
    {
        public string CorrelationId => "favorite-test";
        public string? CausationId => null;
        public TenantId TenantId { get; set; } = tenantId;
    }

    private sealed class AsyncBarrier(int participantCount)
    {
        private readonly TaskCompletionSource _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == participantCount)
            {
                _released.TrySetResult();
            }

            await _released.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BarrierFavorites(IStorefrontFavorites inner, AsyncBarrier barrier)
        : IStorefrontFavorites
    {
        public async Task<Result> AddAsync(
            CustomerId customerId,
            ProductId productId,
            CancellationToken cancellationToken)
        {
            await barrier.SignalAndWaitAsync(cancellationToken);
            return await inner.AddAsync(customerId, productId, cancellationToken);
        }

        public Task<Result> RemoveAsync(
            CustomerId customerId,
            ProductId productId,
            CancellationToken cancellationToken) =>
            inner.RemoveAsync(customerId, productId, cancellationToken);

        public Task<Result<CursorPage<StorefrontProductListItem>>> ListAsync(
            CustomerId customerId,
            string? cursor,
            int limit,
            CancellationToken cancellationToken) =>
            inner.ListAsync(customerId, cursor, limit, cancellationToken);

        public Task<IReadOnlySet<ProductId>> FindAsync(
            CustomerId customerId,
            IReadOnlyCollection<ProductId> productIds,
            CancellationToken cancellationToken) =>
            inner.FindAsync(customerId, productIds, cancellationToken);
    }
}
