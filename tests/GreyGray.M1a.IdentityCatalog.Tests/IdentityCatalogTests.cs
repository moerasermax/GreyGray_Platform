using System.Security.Cryptography;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Identity.Infra;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.IdentityCatalog.Tests;

public sealed class IdentityCatalogTests : IAsyncLifetime
{
    private const string SchemaSql = """
        DROP SCHEMA IF EXISTS iam CASCADE;
        DROP SCHEMA IF EXISTS catalog CASCADE;
        DROP SCHEMA IF EXISTS platform CASCADE;

        CREATE SCHEMA iam;
        CREATE SCHEMA catalog;
        CREATE SCHEMA platform;

        CREATE TABLE platform.outbox_message (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            aggregate_type text NOT NULL,
            aggregate_id text NOT NULL,
            event_type text NOT NULL,
            payload jsonb NOT NULL,
            occurred_at timestamptz NOT NULL,
            correlation_id text NOT NULL,
            causation_id text,
            processed_at timestamptz,
            attempts int NOT NULL DEFAULT 0,
            next_attempt_at timestamptz NOT NULL DEFAULT now(),
            last_error text,
            dead_lettered boolean NOT NULL DEFAULT false
        );

        CREATE TABLE iam.customer (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            display_name varchar(50) NOT NULL,
            tier smallint NOT NULL,
            is_active boolean NOT NULL,
            created_at timestamptz NOT NULL
        );
        CREATE TABLE iam.customer_credential (
            customer_id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            phone_lookup varchar(64) NOT NULL,
            phone_masked varchar(10) NOT NULL,
            password_hash varchar(256) NOT NULL,
            created_at timestamptz NOT NULL,
            CONSTRAINT ux_customer_credential_tenant_phone UNIQUE (tenant_id, phone_lookup)
        );
        CREATE TABLE iam.customer_profile (
            customer_id uuid PRIMARY KEY,
            email_ciphertext text,
            line_linked boolean NOT NULL
        );
        CREATE TABLE iam.staff_account (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            display_name varchar(50) NOT NULL,
            email_lookup varchar(64) NOT NULL,
            email_ciphertext text NOT NULL,
            password_hash varchar(256) NOT NULL,
            role smallint NOT NULL,
            is_active boolean NOT NULL,
            created_at timestamptz NOT NULL,
            CONSTRAINT ux_staff_account_tenant_email UNIQUE (tenant_id, email_lookup)
        );
        CREATE TABLE iam.customer_address (
            id uuid PRIMARY KEY,
            customer_id uuid NOT NULL,
            tenant_id uuid NOT NULL,
            recipient_name_ciphertext text NOT NULL,
            phone_ciphertext text NOT NULL,
            postal_code_ciphertext text NOT NULL,
            city_ciphertext text NOT NULL,
            district_ciphertext text NOT NULL,
            street_address_ciphertext text NOT NULL,
            is_default boolean NOT NULL,
            created_at timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX ux_customer_address_default
            ON iam.customer_address(customer_id, is_default)
            WHERE is_default = true;

        CREATE TABLE catalog.category (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            name varchar(50) NOT NULL,
            image_url varchar(2048),
            sort_order int NOT NULL
        );
        CREATE TABLE catalog.product (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            name varchar(100) NOT NULL,
            description text,
            short_description varchar(100),
            category_id uuid,
            mode smallint NOT NULL,
            is_active boolean NOT NULL,
            created_at timestamptz NOT NULL
        );
        CREATE TABLE catalog.product_image (
            product_id uuid NOT NULL,
            position int NOT NULL,
            url varchar(2048) NOT NULL,
            PRIMARY KEY(product_id, position)
        );
        CREATE TABLE catalog.sku (
            id uuid PRIMARY KEY,
            product_id uuid NOT NULL,
            tenant_id uuid NOT NULL,
            name varchar(100) NOT NULL,
            variant_name varchar(50),
            weight_gram int NOT NULL,
            length_cm int NOT NULL,
            width_cm int NOT NULL,
            height_cm int NOT NULL,
            unit_of_measure varchar(30),
            unit_count int,
            list_price_amount_minor bigint,
            list_price_currency varchar(3),
            is_active boolean NOT NULL,
            created_at timestamptz NOT NULL
        );
        """;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Customer_registration_is_atomic_and_credentials_are_not_plaintext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<ICustomerAccounts>();

        var result = await accounts.RegisterAsync(
            new RegisterCustomerInput(
                "0912345678", "correct-horse", "Grey", "grey@example.test", null),
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PhoneNumberMasked.ShouldBe("0912***678");
        (await ScalarAsync<long>("SELECT count(*) FROM iam.customer", cancellationToken)).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM platform.outbox_message", cancellationToken)).ShouldBe(1);
        var passwordHash = await ScalarAsync<string>(
            "SELECT password_hash FROM iam.customer_credential", cancellationToken);
        passwordHash.ShouldNotContain("correct-horse");
        passwordHash.ShouldStartWith("pbkdf2-sha256-v1$");
        var phoneLookup = await ScalarAsync<string>(
            "SELECT phone_lookup FROM iam.customer_credential", cancellationToken);
        phoneLookup.ShouldNotBe("0912345678");
        var emailCiphertext = await ScalarAsync<string>(
            "SELECT email_ciphertext FROM iam.customer_profile", cancellationToken);
        emailCiphertext.ShouldNotContain("grey@example.test");

        var correct = await accounts.AuthenticateAsync(
            new CustomerLoginInput("0912345678", "correct-horse"), cancellationToken);
        correct.IsSuccess.ShouldBeTrue();

        var missing = await accounts.AuthenticateAsync(
            new CustomerLoginInput("0999999999", "wrong-password"), cancellationToken);
        var wrong = await accounts.AuthenticateAsync(
            new CustomerLoginInput("0912345678", "wrong-password"), cancellationToken);
        missing.Error.Code.ShouldBe("identity.invalid-credentials");
        wrong.Error.Code.ShouldBe(missing.Error.Code);
        wrong.Error.Message.ShouldBe(missing.Error.Message);
    }

    [Fact]
    public async Task Failed_outbox_insert_rolls_back_customer_and_credential()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await ExecuteAsync("DROP TABLE platform.outbox_message", cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();

        await Should.ThrowAsync<Exception>(async () =>
            await scope.ServiceProvider.GetRequiredService<ICustomerAccounts>().RegisterAsync(
                new RegisterCustomerInput(
                    "0912345678", "correct-horse", "Grey", null, null),
                cancellationToken));

        (await ScalarAsync<long>("SELECT count(*) FROM iam.customer", cancellationToken)).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM iam.customer_credential", cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Staff_roles_are_a_branching_policy_and_login_is_generic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IStaffAccounts>();

        var created = await accounts.CreateAsync(
            new CreateStaffInput("Operator", "operator@example.test", "operator-password", StaffRole.Operator),
            cancellationToken);
        created.IsSuccess.ShouldBeTrue();

        var login = await accounts.AuthenticateAsync(
            new StaffLoginInput("OPERATOR@example.test", "operator-password"), cancellationToken);
        login.IsSuccess.ShouldBeTrue();

        var unknown = await accounts.AuthenticateAsync(
            new StaffLoginInput("missing@example.test", "bad-password"), cancellationToken);
        var badPassword = await accounts.AuthenticateAsync(
            new StaffLoginInput("operator@example.test", "bad-password"), cancellationToken);
        unknown.Error.ShouldBe(badPassword.Error);

        var policy = scope.ServiceProvider.GetRequiredService<IStaffRolePolicy>();
        policy.Allows(StaffRole.Owner, StaffRole.Accountant).ShouldBeTrue();
        policy.Allows(StaffRole.Operator, StaffRole.ReadOnly).ShouldBeTrue();
        policy.Allows(StaffRole.Operator, StaffRole.Accountant).ShouldBeFalse();
        policy.Allows(StaffRole.Accountant, StaffRole.Operator).ShouldBeFalse();
    }

    [Fact]
    public async Task Profile_and_address_crud_preserve_customer_ownership_and_default()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<ICustomerAccounts>();
        var registered = await accounts.RegisterAsync(
            new RegisterCustomerInput("0912345678", "correct-horse", "Before", null, null),
            cancellationToken);
        var customerId = registered.Value.Id;

        var profile = await accounts.UpdateProfileAsync(
            customerId,
            new UpdateCustomerProfileInput("After", "after@example.test"),
            cancellationToken);
        profile.Value.DisplayName.ShouldBe("After");
        profile.Value.Email.ShouldBe("after@example.test");

        var addressBook = scope.ServiceProvider.GetRequiredService<ICustomerAddressBook>();
        var first = await addressBook.AddAsync(
            customerId,
            Address("台北市", false),
            cancellationToken);
        first.Value.IsDefault.ShouldBeTrue();
        var second = await addressBook.AddAsync(
            customerId,
            Address("新北市", true),
            cancellationToken);
        second.Value.IsDefault.ShouldBeTrue();
        var all = await addressBook.ListAsync(customerId, cancellationToken);
        all.Value.Count(value => value.IsDefault).ShouldBe(1);
        (await addressBook.DeleteAsync(customerId, first.Value.Id, cancellationToken)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Product_starts_without_skus_and_only_valid_sku_makes_it_storefront_visible()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var storefront = scope.ServiceProvider.GetRequiredService<IStorefrontCatalogQuery>();

        var category = await admin.CreateCategoryAsync(
            new CategoryInput("茶點", null, 1), cancellationToken);
        var product = await admin.CreateProductAsync(
            new AdminProductInput(
                "鳳梨酥", "描述", "短描述", category.Value.Id,
                FulfillmentMode.Stock, ["https://example.test/pineapple-cake.jpg"], true),
            cancellationToken);
        product.IsSuccess.ShouldBeTrue();
        product.Value.Skus.ShouldBeEmpty();
        (await storefront.ListCategoriesAsync(cancellationToken)).ShouldBeEmpty();

        var invalid = await admin.CreateSkuAsync(
            product.Value.Id,
            new AdminSkuInput(
                "盒裝", null, -1, new Dimensions(10, 10, 5), "盒", 1,
                new Money(42_000, Currency.TWD), true),
            cancellationToken);
        invalid.Error.Code.ShouldBe("catalog.invalid-sku");

        var sku = await admin.CreateSkuAsync(
            product.Value.Id,
            new AdminSkuInput(
                "盒裝", "12 入", 500, new Dimensions(20, 15, 8), "顆", 12,
                new Money(42_000, Currency.TWD), true),
            cancellationToken);
        sku.IsSuccess.ShouldBeTrue();
        sku.Value.WeightGram.ShouldBe(500);
        sku.Value.Size.ShouldBe(new Dimensions(20, 15, 8));
        (await storefront.ListCategoriesAsync(cancellationToken)).Single().Id.ShouldBe(category.Value.Id);

        var page = await storefront.ListProductsAsync(
            new ProductSearch(null, category.Value.Id, FulfillmentMode.Stock, false, null, 20),
            cancellationToken);
        page.Value.Items.Single().PriceFrom.ShouldBe(new Money(42_000, Currency.TWD));
        var excludedByMode = await storefront.ListProductsAsync(
            new ProductSearch(null, category.Value.Id, FulfillmentMode.Preorder, false, null, 20),
            cancellationToken);
        excludedByMode.Value.Items.ShouldBeEmpty();

        var preorder = await admin.CreateProductAsync(
            new AdminProductInput(
                "預購茶壺", null, null, category.Value.Id,
                FulfillmentMode.Preorder, [], true),
            cancellationToken);
        await admin.CreateSkuAsync(
            preorder.Value.Id,
            new AdminSkuInput(
                "標準", null, 900, new Dimensions(25, 20, 20), null, null,
                null, true),
            cancellationToken);
        var preorderPage = await storefront.ListProductsAsync(
            new ProductSearch(null, category.Value.Id, FulfillmentMode.Preorder, false, null, 20),
            cancellationToken);
        preorderPage.Value.Items.Single().PriceFrom.ShouldBeNull();
        (await ScalarAsync<long>(
            "SELECT count(*) FROM platform.outbox_message WHERE event_type = 'catalog.SkuPublished.v1'",
            cancellationToken)).ShouldBe(2);
    }

    private ServiceProvider CreateServices()
    {
        var key = Convert.ToBase64String(SHA256.HashData("m1a-test-key"u8.ToArray()));
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:GreyGray_iam"] = _postgres.GetConnectionString(),
            ["ConnectionStrings:GreyGray_catalog"] = _postgres.GetConnectionString(),
            ["Identity:DataProtectionKey"] = key,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection()
            .AddSingleton<IClock>(_clock)
            .AddSingleton<ICorrelationContext>(new FakeCorrelationContext())
            .AddIdentityModule(configuration)
            .AddCatalogModule(configuration)
            .BuildServiceProvider(validateScopes: true);
    }

    private async Task ResetAsync(CancellationToken cancellationToken) =>
        await ExecuteAsync(SchemaSql, cancellationToken);

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<T> ScalarAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static ShippingAddressInput Address(string city, bool isDefault) => new(
        "收件人", "0912345678", "100", city, "中正區", "測試路 1 號", isDefault);
}

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
    public DateOnly TodayInTaipei => DateOnly.FromDateTime(UtcNow.AddHours(8).DateTime);
}

internal sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "0123456789abcdef0123456789abcdef";
    public string? CausationId => null;
    public TenantId TenantId => TenantId.Default;
}
