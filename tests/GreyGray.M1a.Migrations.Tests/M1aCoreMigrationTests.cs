using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.Migrations.Tests;

public sealed class M1aCoreMigrationTests : IAsyncLifetime
{
    private const string TenantA = "10000000-0000-0000-0000-000000000001";
    private const string TenantB = "20000000-0000-0000-0000-000000000002";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "0001→0006 可重跑，M1a schema/owner/tenant/amount 約束完整")]
    public async Task Full_chain_is_idempotent_owned_and_enforces_m1a_boundaries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var connectionString = _postgres.GetConnectionString();

        await ExecuteMigrationChainAsync(connectionString, migrations, 6, cancellationToken);
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0006_m1a_core.sql"),
            cancellationToken);

        (await ScalarAsync<int>(connectionString, """
            SELECT count(*)::int
            FROM pg_tables
            WHERE schemaname IN (
                'iam', 'catalog', 'inventory', 'campaign', 'pricing', 'checkout', 'ordering');
            """, cancellationToken)).ShouldBe(19);

        (await ScalarAsync<int>(connectionString, """
            SELECT count(*)::int
            FROM pg_tables
            WHERE schemaname IN (
                'iam', 'catalog', 'inventory', 'campaign', 'pricing', 'checkout', 'ordering')
              AND tableowner = 'greygray_owner';
            """, cancellationToken)).ShouldBe(19);

        (await ScalarAsync<string>(connectionString, """
            SELECT count(*)::text || ':' || bool_and(data_type = 'bigint')::text
            FROM information_schema.columns
            WHERE table_schema IN (
                'iam', 'catalog', 'inventory', 'campaign', 'pricing', 'checkout', 'ordering')
              AND column_name LIKE '%\_minor' ESCAPE '\';
            """, cancellationToken)).ShouldBe("12:true");

        (await ScalarAsync<bool>(connectionString, """
            SELECT bool_and(to_regclass(index_name) IS NOT NULL)
            FROM (VALUES
                ('iam.ux_customer_credential_tenant_phone'),
                ('catalog.ix_product_tenant_category_active'),
                ('catalog.ix_sku_tenant_product_active'),
                ('inventory.ix_reservation_tenant_active_created'),
                ('inventory.ix_reservation_allocation_tenant_sku'),
                ('campaign.ix_campaign_tenant_status_created'),
                ('pricing.ix_pricing_snapshot_tenant_created'),
                ('checkout.ux_cart_line_identity'),
                ('ordering.ux_orders_tenant_checkout_key'),
                ('ordering.ix_order_line_tenant_order')) expected(index_name);
            """, cancellationToken)).ShouldBeTrue();

        (await ScalarAsync<bool>(connectionString, """
            SELECT has_table_privilege(
                'greygray_catalog', 'catalog.product', 'SELECT,INSERT,UPDATE,DELETE');
            """, cancellationToken)).ShouldBeTrue();
        (await ScalarAsync<bool>(connectionString, """
            SELECT has_table_privilege('greygray_catalog', 'ordering.orders', 'SELECT');
            """, cancellationToken)).ShouldBeFalse();

        await AssertTenantAndValueConstraintsAsync(connectionString, cancellationToken);

        await ExecuteSqlAsync(
            connectionString,
            "CREATE TABLE ordering.owner_assertion_probe (id integer);",
            cancellationToken);
        var ownerFailure = await Should.ThrowAsync<PostgresException>(() => ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0006_m1a_core.sql"),
            cancellationToken));
        ownerFailure.SqlState.ShouldBe("P0001");
        ownerFailure.MessageText.ShouldContain("owner");
        await ExecuteSqlAsync(
            connectionString,
            "DROP TABLE ordering.owner_assertion_probe;",
            cancellationToken);

        var migrationText = await File.ReadAllTextAsync(
            Path.Combine(migrations, "0006_m1a_core.sql"),
            cancellationToken);
        migrationText.ShouldNotContain("ALTER TABLE payment.");
        migrationText.ShouldNotContain("ALTER TABLE ledger.");
        migrationText.ShouldNotContain("CREATE TABLE IF NOT EXISTS payment.");
        migrationText.ShouldNotContain("CREATE TABLE IF NOT EXISTS ledger.");
    }

    [Fact(DisplayName = "兩個 STOCK reservation 競爭同一 lot 時只有一個能保留，不會超賣")]
    public async Task Concurrent_atomic_reservations_cannot_exceed_lot_availability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var connectionString = _postgres.GetConnectionString();
        await ExecuteMigrationChainAsync(connectionString, migrations, 6, cancellationToken);

        var lotId = Guid.CreateVersion7();
        var skuId = Guid.CreateVersion7();
        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO inventory.lot (
                id, tenant_id, sku_id, quantity_on_hand, quantity_reserved)
            VALUES ('{lotId}'::uuid, '{TenantA}'::uuid, '{skuId}'::uuid, 10, 0);
            """, cancellationToken);

        var results = await Task.WhenAll(
            TryReserveAsync(connectionString, lotId, skuId, "reserve-a", 6, cancellationToken),
            TryReserveAsync(connectionString, lotId, skuId, "reserve-b", 6, cancellationToken));

        results.Count(result => result).ShouldBe(1);
        (await ScalarAsync<int>(connectionString, $"""
            SELECT quantity_reserved
            FROM inventory.lot
            WHERE id = '{lotId}'::uuid;
            """, cancellationToken)).ShouldBe(6);
        (await ScalarAsync<int>(connectionString, """
            SELECT count(*)::int FROM inventory.reservation;
            """, cancellationToken)).ShouldBe(1);
        (await ScalarAsync<int>(connectionString, """
            SELECT count(*)::int FROM inventory.reservation_allocation;
            """, cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "舊 seam 資料未補真實欄位時，0006 明確失敗而不捏造快照")]
    public async Task Incomplete_seam_rows_fail_instead_of_receiving_fabricated_defaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var connectionString = await CreateDatabaseAsync(cancellationToken);

        await ExecuteMigrationChainAsync(connectionString, migrations, 5, cancellationToken);
        await ExecuteSqlAsync(
            connectionString,
            $"INSERT INTO catalog.sku (id) VALUES ('{Guid.CreateVersion7()}'::uuid);",
            cancellationToken);

        var skuFailure = await Should.ThrowAsync<PostgresException>(() => ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0006_m1a_core.sql"),
            cancellationToken));
        skuFailure.SqlState.ShouldBe("P0001");
        skuFailure.MessageText.ShouldContain("catalog.sku");

        await ExecuteSqlAsync(connectionString, "DELETE FROM catalog.sku;", cancellationToken);
        await ExecuteSqlAsync(
            connectionString,
            $"INSERT INTO ordering.orders (id) VALUES ('{Guid.CreateVersion7()}'::uuid);",
            cancellationToken);

        var orderFailure = await Should.ThrowAsync<PostgresException>(() => ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0006_m1a_core.sql"),
            cancellationToken));
        orderFailure.SqlState.ShouldBe("P0001");
        orderFailure.MessageText.ShouldContain("ordering.orders");
    }

    [Fact(DisplayName = "0007 Procurement 可重跑，owner／權限／tenant-order-line／TWD 成本約束完整")]
    public async Task Procurement_migration_is_idempotent_owned_and_constrained()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var connectionString = await CreateDatabaseAsync(cancellationToken);

        await ExecuteMigrationChainAsync(connectionString, migrations, 7, cancellationToken);
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0007_m1b_procurement.sql"),
            cancellationToken);

        (await ScalarAsync<string>(connectionString, """
            SELECT tableowner
            FROM pg_tables
            WHERE schemaname = 'procurement' AND tablename = 'purchase_item';
            """, cancellationToken)).ShouldBe("greygray_owner");
        (await ScalarAsync<bool>(connectionString, """
            SELECT has_table_privilege(
                'greygray_procurement',
                'procurement.purchase_item',
                'SELECT,INSERT,UPDATE,DELETE');
            """, cancellationToken)).ShouldBeTrue();
        (await ScalarAsync<bool>(connectionString, """
            SELECT has_table_privilege(
                'greygray_ordering',
                'procurement.purchase_item',
                'SELECT');
            """, cancellationToken)).ShouldBeFalse();

        var orderLineId = Guid.CreateVersion7();
        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO procurement.purchase_item (
                id, tenant_id, campaign_id, sku_id, order_line_id,
                quantity_requested, quantity_purchased, status, created_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantA}'::uuid,
                '{Guid.CreateVersion7()}'::uuid, '{Guid.CreateVersion7()}'::uuid,
                '{orderLineId}'::uuid, 2, 0, 0, now());
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO procurement.purchase_item (
                id, tenant_id, campaign_id, sku_id, order_line_id,
                quantity_requested, quantity_purchased, status, created_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantA}'::uuid,
                '{Guid.CreateVersion7()}'::uuid, '{Guid.CreateVersion7()}'::uuid,
                '{orderLineId}'::uuid, 1, 0, 0, now());
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO procurement.purchase_item (
                id, tenant_id, campaign_id, sku_id, order_line_id,
                quantity_requested, quantity_purchased,
                actual_paid_original_amount_minor, actual_paid_original_currency,
                actual_paid_booking_amount_minor, actual_paid_booking_currency,
                status, created_at, decided_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantA}'::uuid,
                '{Guid.CreateVersion7()}'::uuid, '{Guid.CreateVersion7()}'::uuid,
                '{Guid.CreateVersion7()}'::uuid, 1, 1,
                1000, 'JPY', 220, 'JPY', 1, now(), now());
            """, cancellationToken);
    }

    [Fact(DisplayName = "0008 回填歷史全額退款並約束部分退款累計，不得超過原付款")]
    public async Task Line_refund_migration_backfills_and_constrains_cumulative_amount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var connectionString = await CreateDatabaseAsync(cancellationToken);
        await ExecuteMigrationChainAsync(connectionString, migrations, 7, cancellationToken);
        var historicalRefund = Guid.CreateVersion7();

        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO payment.payment (
                id, tenant_id, provider, order_id, status,
                goods_amount_minor, shipping_amount_minor,
                merchant_trade_no, created_at, expires_at)
            VALUES (
                '{historicalRefund}'::uuid, '{TenantA}'::uuid, 1, '{Guid.CreateVersion7()}'::uuid, 3,
                10000, 6000, 'GGHISTORICALREFUND01', now(), now() + interval '30 minutes');
            """, cancellationToken);

        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0008_m1a_line_refund.sql"),
            cancellationToken);
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0008_m1a_line_refund.sql"),
            cancellationToken);

        (await ScalarAsync<long>(connectionString, $"""
            SELECT refunded_amount_minor
            FROM payment.payment
            WHERE id = '{historicalRefund}'::uuid;
            """, cancellationToken)).ShouldBe(16000);

        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO payment.payment (
                id, tenant_id, provider, order_id, status,
                goods_amount_minor, shipping_amount_minor, refunded_amount_minor,
                merchant_trade_no, created_at, expires_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantA}'::uuid, 1, '{Guid.CreateVersion7()}'::uuid, 4,
                10000, 6000, 4000, 'GGPARTIALREFUND0001', now(), now() + interval '30 minutes');
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO payment.payment (
                id, tenant_id, provider, order_id, status,
                goods_amount_minor, shipping_amount_minor, refunded_amount_minor,
                merchant_trade_no, created_at, expires_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantA}'::uuid, 1, '{Guid.CreateVersion7()}'::uuid, 4,
                10000, 6000, 17000, 'GGINVALIDREFUND0001', now(), now() + interval '30 minutes');
            """, cancellationToken);
    }

    private static async Task AssertTenantAndValueConstraintsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var customerId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var lotId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        var campaignId = Guid.CreateVersion7();
        var cartId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();

        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO iam.customer (id, tenant_id, display_name, tier, is_active, created_at)
            VALUES ('{customerId}'::uuid, '{TenantA}'::uuid, 'M1a customer', 0, true, now());

            INSERT INTO catalog.product (
                id, tenant_id, name, mode, is_active, created_at)
            VALUES ('{productId}'::uuid, '{TenantA}'::uuid, 'M1a product', 0, true, now());

            INSERT INTO inventory.lot (
                id, tenant_id, sku_id, quantity_on_hand, quantity_reserved)
            VALUES ('{lotId}'::uuid, '{TenantA}'::uuid, '{Guid.CreateVersion7()}'::uuid, 10, 0);

            INSERT INTO inventory.reservation (
                id, tenant_id, reservation_key, created_at)
            VALUES ('{reservationId}'::uuid, '{TenantA}'::uuid, 'constraint-probe', now());

            INSERT INTO campaign.campaign (
                id, tenant_id, title, destination, depart_at, return_at, closes_at,
                status, created_at, updated_at)
            VALUES (
                '{campaignId}'::uuid, '{TenantA}'::uuid, 'M1a campaign', 'Tokyo',
                current_date + 10, current_date + 12, now() + interval '5 days',
                0, now(), now());

            INSERT INTO checkout.cart (
                id, tenant_id, shipping_policy, created_at, updated_at)
            VALUES ('{cartId}'::uuid, '{TenantA}'::uuid, 2, now(), now());

            INSERT INTO ordering.orders (
                id, tenant_id, checkout_event_id, checkout_cart_id,
                checkout_idempotency_key, order_number, customer_id, source_channel,
                status, shipping_policy, delivery_method, pricing_snapshot_id,
                goods_total_amount_minor, goods_total_currency,
                shipping_fee_amount_minor, shipping_fee_currency,
                grand_total_amount_minor, grand_total_currency,
                quote_explain, placed_at)
            VALUES (
                '{orderId}'::uuid, '{TenantA}'::uuid, '{Guid.CreateVersion7()}'::uuid,
                '{Guid.CreateVersion7()}'::uuid, 'checkout-1', 'GG0000000001',
                '{customerId}'::uuid, 0, 0, 2, 1, '{Guid.CreateVersion7()}'::uuid,
                1000, 'TWD', 100, 'TWD', 1100, 'TWD', '[]'::jsonb, now());
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO inventory.reservation (
                id, tenant_id, reservation_key, created_at)
            VALUES ('{Guid.CreateVersion7()}'::uuid, '{TenantA}'::uuid, 'constraint-probe', now());
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO inventory.reservation_allocation (
                reservation_id, tenant_id, lot_id, sku_id, quantity)
            SELECT '{reservationId}'::uuid, '{TenantB}'::uuid, id, sku_id, 1
            FROM inventory.lot WHERE id = '{lotId}'::uuid;
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO iam.customer_address (
                id, customer_id, tenant_id, recipient_name_ciphertext, phone_ciphertext,
                postal_code_ciphertext, city_ciphertext, district_ciphertext,
                street_address_ciphertext, is_default, created_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{customerId}'::uuid, '{TenantB}'::uuid,
                'recipient', 'phone', 'postal', 'city', 'district', 'street', false, now());
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO catalog.sku (
                id, tenant_id, product_id, name, weight_gram, length_cm, width_cm,
                height_cm, list_price_amount_minor, list_price_currency, is_active, created_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantB}'::uuid, '{productId}'::uuid,
                'Cross tenant SKU', 100, 10, 10, 10, 1000, 'TWD', true, now());
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO catalog.sku (
                id, tenant_id, product_id, name, weight_gram, length_cm, width_cm,
                height_cm, list_price_amount_minor, list_price_currency, is_active, created_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantA}'::uuid, '{productId}'::uuid,
                'Negative weight SKU', -1, 10, 10, 10, 1000, 'TWD', true, now());
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO checkout.cart_line (
                id, cart_id, tenant_id, sku_id, product_id, name, fulfillment_mode,
                quantity, unit_price_amount_minor, unit_price_currency)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{cartId}'::uuid, '{TenantB}'::uuid,
                '{Guid.CreateVersion7()}'::uuid, '{productId}'::uuid, 'Cross tenant line',
                0, 1, 1000, 'TWD');
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO ordering.order_line (
                id, order_id, tenant_id, sku_id, fulfillment_mode, status, quantity,
                unit_price_amount_minor, unit_price_currency)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{orderId}'::uuid, '{TenantB}'::uuid,
                '{Guid.CreateVersion7()}'::uuid, 0, 0, 1, 1000, 'TWD');
            """, cancellationToken);

        await AssertConstraintFailureAsync(connectionString, $"""
            INSERT INTO campaign.campaign_offer (
                id, campaign_id, sku_id, selling_price_minor, selling_price_currency,
                target_purchase_price_minor, target_purchase_price_currency, is_active, created_at)
            VALUES (
                '30000000-0000-0000-0000-000000000003'::uuid,
                '{campaignId}'::uuid,
                '30000000-0000-0000-0000-000000000005'::uuid,
                1000, 901, 800, NULL, true, now());
            """, cancellationToken);
    }

    private static async Task<bool> TryReserveAsync(
        string connectionString,
        Guid lotId,
        Guid skuId,
        string reservationKey,
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var update = new NpgsqlCommand($"""
            UPDATE inventory.lot
            SET quantity_reserved = quantity_reserved + @quantity
            WHERE id = @lot_id
              AND tenant_id = '{TenantA}'::uuid
              AND quantity_available >= @quantity;
            """, connection, transaction);
        update.Parameters.AddWithValue("quantity", quantity);
        update.Parameters.AddWithValue("lot_id", lotId);
        if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var reservationId = Guid.CreateVersion7();
        await using var insert = new NpgsqlCommand($"""
            INSERT INTO inventory.reservation (
                id, tenant_id, reservation_key, created_at)
            VALUES (@reservation_id, '{TenantA}'::uuid, @reservation_key, now());

            INSERT INTO inventory.reservation_allocation (
                reservation_id, tenant_id, lot_id, sku_id, quantity)
            VALUES (
                @reservation_id, '{TenantA}'::uuid, @lot_id, @sku_id, @quantity);
            """, connection, transaction);
        insert.Parameters.AddWithValue("reservation_id", reservationId);
        insert.Parameters.AddWithValue("reservation_key", reservationKey);
        insert.Parameters.AddWithValue("lot_id", lotId);
        insert.Parameters.AddWithValue("sku_id", skuId);
        insert.Parameters.AddWithValue("quantity", quantity);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task AssertConstraintFailureAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        var exception = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteSqlAsync(connectionString, sql, cancellationToken));
        exception.SqlState.ShouldBeOneOf("23503", "23505", "23514");
    }

    private async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var databaseName = $"m1a_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task ExecuteMigrationChainAsync(
        string connectionString,
        string migrationDirectory,
        int lastMigration,
        CancellationToken cancellationToken)
    {
        var paths = Directory.GetFiles(migrationDirectory, "*.sql")
            .Where(path => int.TryParse(Path.GetFileName(path).AsSpan(0, 4), out var number)
                && number <= lastMigration)
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            await ExecuteScriptAsync(connectionString, path, cancellationToken);
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

    private static async Task ExecuteScriptAsync(
        string connectionString,
        string path,
        CancellationToken cancellationToken)
    {
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
        await ExecuteSqlAsync(connectionString, sql, cancellationToken);
    }

    private static async Task ExecuteSqlAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 60,
        };
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
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return (T)(value ?? throw new InvalidOperationException("Expected a scalar value."));
    }
}
