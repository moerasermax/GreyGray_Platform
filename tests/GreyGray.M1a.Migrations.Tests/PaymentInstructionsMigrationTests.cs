using System.Globalization;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;
using PaymentEntity = GreyGray.Modules.Payment.Core.Payment;

namespace GreyGray.M1a.Migrations.Tests;

public sealed partial class PaymentLedgerMigrationTests
{
    private const string Be62MerchantId = "BE62MERCHANT";
    private const string Be62HashKey = "BE62HASHKEY00001";
    private const string Be62HashIv = "BE62HASHIV000001";

    [Fact(DisplayName = "M1 0001～0024 重放時，既有合法 status=5 不被舊 CHECK 擋下")]
    public async Task M1_full_migration_chain_replays_with_instruction_row()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);
        await ExecuteSqlAsync(connectionString, LegalInstructionInsert(), cancellationToken);

        for (var migration = 1; migration <= 24; migration++)
        {
            await ExecuteMigrationAsync(connectionString, migration, cancellationToken);
        }

        (await ScalarAsync<int>(connectionString,
            "SELECT count(*)::int FROM payment.payment WHERE status = 5;",
            cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "M2 status=5 且 ATM 欄位齊全可以寫入")]
    public async Task M2_valid_instruction_row_is_accepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);

        await ExecuteSqlAsync(connectionString, LegalInstructionInsert(), cancellationToken);

        (await ScalarAsync<int>(connectionString,
            "SELECT count(*)::int FROM payment.payment WHERE status = 5;",
            cancellationToken)).ShouldBe(1);
    }

    [Theory(DisplayName = "M3 status=5 缺期限、空白必要欄位或混入其他方式欄位都被 CHECK 擋下")]
    [InlineData("provider_expires_at", "NULL", "payment_instructions_consistent")]
    [InlineData("virtual_account", "' '", "payment_instructions_consistent")]
    [InlineData("payment_no", "'EXTRA-CVS'", "payment_instructions_consistent")]
    [InlineData("bank_code", "'123456789012345678901234567890123'", "payment_instruction_lengths")]
    public async Task M3_invalid_instruction_rows_are_rejected(
        string column,
        string value,
        string constraintName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);
        var failure = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            LegalInstructionInsert(column, value),
            cancellationToken));

        failure.SqlState.ShouldBe("23514");
        failure.ConstraintName.ShouldBe(constraintName);
    }

    [Fact(DisplayName = "M4 status=5 與同訂單 Pending 互斥")]
    public async Task M4_instruction_row_participates_in_active_unique_index()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);
        var orderId = Guid.CreateVersion7();
        await ExecuteSqlAsync(connectionString, LegalInstructionInsert(orderId: orderId), cancellationToken);

        var failure = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            BasePaymentInsert(orderId, 0, "BE62PENDING000000001"),
            cancellationToken));

        failure.SqlState.ShouldBe("23505");
        failure.ConstraintName.ShouldBe("ux_payment_tenant_order_active");
    }

    [Fact(DisplayName = "M5 status=5 不可帶退款金額")]
    public async Task M5_instruction_row_rejects_refunded_amount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);

        var failure = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            LegalInstructionInsert("refunded_amount_minor", "1"),
            cancellationToken));

        failure.SqlState.ShouldBe("23514");
        failure.ConstraintName.ShouldBe("payment_refund_status_consistent");
    }

    [Theory(DisplayName = "M6 晚到付款欄位只允許成對出現在 Failed")]
    [InlineData(0, "'LATE-TRADE'", "now()")]
    [InlineData(2, "'LATE-TRADE'", "NULL")]
    public async Task M6_late_capture_columns_are_consistent(
        int status,
        string tradeNo,
        string capturedAt)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);
        var sql = BasePaymentInsert(Guid.CreateVersion7(), status, $"BE62LATE{Guid.NewGuid():N}"[..20],
            $", late_capture_trade_no, late_captured_at",
            $", {tradeNo}, {capturedAt}");

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteSqlAsync(connectionString, sql, cancellationToken));

        failure.SqlState.ShouldBe("23514");
        failure.ConstraintName.ShouldBe("payment_late_capture_consistent");
    }

    [Fact(DisplayName = "M7 EF 對真 Postgres 往返保存取號欄位與 UTC 時間")]
    public async Task M7_ef_round_trips_instruction_fields_in_utc()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);
        var issuedAt = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
        var expiresAt = new DateTimeOffset(2026, 10, 4, 15, 59, 59, TimeSpan.Zero);
        var payment = PaymentEntity.Start(
            PaymentId.New(),
            GreyGray.Shared.Kernel.TenantId.Default,
            OrderId.New(),
            Money.OfMajor(100, Currency.TWD),
            Money.OfMajor(60, Currency.TWD),
            $"BE62EF{Guid.NewGuid():N}"[..20],
            issuedAt,
            issuedAt.AddMinutes(30),
            PaymentMethod.Atm);
        payment.IssueInstructions(
            "BE62-EF-TRADE",
            new PaymentInstructionData("812", "1234567890123456", null, null, null, null),
            expiresAt,
            issuedAt).IsSuccess.ShouldBeTrue();

        await using (var write = CreatePaymentDbContext(connectionString))
        {
            write.Payments.Add(payment);
            await write.SaveChangesAsync(cancellationToken);
        }

        await using (var read = CreatePaymentDbContext(connectionString))
        {
            var actual = await read.Payments.SingleAsync(candidate => candidate.Id == payment.Id, cancellationToken);
            actual.Status.ShouldBe(PaymentStatus.InstructionsIssued);
            actual.Method.ShouldBe(PaymentMethod.Atm);
            actual.VirtualAccount.ShouldBe("1234567890123456");
            actual.ProviderExpiresAt.ShouldBe(expiresAt);
            actual.ProviderExpiresAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
            actual.InstructionsIssuedAt.ShouldBe(issuedAt);
        }
    }

    [Fact(DisplayName = "K1 取號與付款結果併發時只有一方成功且 outbox 只有一筆事件")]
    public async Task K1_concurrent_instruction_and_capture_have_one_committed_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);
        var now = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
        var payment = PaymentEntity.Start(
            PaymentId.New(),
            GreyGray.Shared.Kernel.TenantId.Default,
            OrderId.New(),
            Money.OfMajor(100, Currency.TWD),
            Money.OfMajor(60, Currency.TWD),
            $"BE62K1{Guid.NewGuid():N}"[..20],
            now,
            now.AddMinutes(30),
            PaymentMethod.Atm);
        await using (var seed = CreatePaymentDbContext(connectionString))
        {
            seed.Payments.Add(payment);
            await seed.SaveChangesAsync(cancellationToken);
        }

        var settings = Be62Settings();
        using var httpClient = new HttpClient();
        var gateway = new EcpayGateway(settings, Be62HashKey, Be62HashIv, httpClient);
        var info = SignedInfo(payment.MerchantTradeNo, now);
        var paid = SignedPayment(payment.MerchantTradeNo, now);
        var gate = new SaveGate(2);
        var registry = EventTypeRegistry.FromAssemblies([typeof(PaymentCaptured).Assembly]);
        var correlation = new StubCorrelationContext();

        await using var infoContext = CreatePaymentDbContext(connectionString);
        await using var paidContext = CreatePaymentDbContext(connectionString);
        var infoService = CreateConcurrentService(infoContext, gate, gateway, settings, now, registry, correlation);
        var paidService = CreateConcurrentService(paidContext, gate, gateway, settings, now, registry, correlation);

        var results = await Task.WhenAll(
            infoService.HandleEcpayPaymentInfoAsync(info, cancellationToken),
            paidService.HandleEcpayCallbackAsync(paid, cancellationToken));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Count(result => result.IsFailure && result.Error.Code == "payment.concurrent-update").ShouldBe(1);
        (await ScalarAsync<int>(connectionString, $"""
            SELECT count(*)::int
            FROM platform.outbox_message
            WHERE aggregate_type = 'Payment'
              AND aggregate_id = '{payment.Id}';
            """, cancellationToken)).ShouldBe(1);
        var status = await ScalarAsync<short>(connectionString, $"""
            SELECT status FROM payment.payment WHERE id = '{payment.Id.Value}'::uuid;
            """, cancellationToken);
        status.ShouldBeOneOf((short)PaymentStatus.Captured, (short)PaymentStatus.InstructionsIssued);
    }

    private static PaymentApplicationService CreateConcurrentService(
        PaymentDbContext context,
        SaveGate gate,
        IEcpayGateway gateway,
        EcpaySettings settings,
        DateTimeOffset now,
        EventTypeRegistry registry,
        ICorrelationContext correlation) =>
        new(
            new PaymentRepository(context),
            new GatedUnitOfWork(context, gate),
            new OutboxEventPublisher<PaymentDbContext>(context, correlation, registry),
            gateway,
            settings,
            new StubClock(now),
            correlation);

    private static string LegalInstructionInsert(
        string? overrideColumn = null,
        string? overrideValue = null,
        Guid? orderId = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["refunded_amount_minor"] = "0",
            ["provider_expires_at"] = "'2026-10-04T15:59:59Z'::timestamptz",
            ["bank_code"] = "'812'",
            ["virtual_account"] = "'1234567890123456'",
            ["payment_no"] = "NULL",
        };
        if (overrideColumn is not null)
        {
            values[overrideColumn] = overrideValue!;
        }

        return $"""
            INSERT INTO payment.payment (
                id, tenant_id, provider, order_id, status,
                goods_amount_minor, shipping_amount_minor, refunded_amount_minor,
                merchant_trade_no, provider_transaction_id, created_at, expires_at,
                method, bank_code, virtual_account, payment_no,
                provider_expires_at, instructions_issued_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, 1,
                '{orderId ?? Guid.CreateVersion7()}'::uuid, 5,
                10000, 6000, {values["refunded_amount_minor"]},
                '{$"BE62I{Guid.NewGuid():N}"[..20]}', 'BE62-INFO-TRADE',
                '2026-10-01T04:00:00Z'::timestamptz, '2026-10-01T04:30:00Z'::timestamptz,
                1, {values["bank_code"]}, {values["virtual_account"]}, {values["payment_no"]},
                {values["provider_expires_at"]}, '2026-10-01T04:00:00Z'::timestamptz);
            """;
    }

    private static string BasePaymentInsert(
        Guid orderId,
        int status,
        string merchantTradeNo,
        string extraColumns = "",
        string extraValues = "") => $"""
        INSERT INTO payment.payment (
            id, tenant_id, provider, order_id, status,
            goods_amount_minor, shipping_amount_minor, refunded_amount_minor,
            merchant_trade_no, created_at, expires_at{extraColumns})
        VALUES (
            '{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, 1, '{orderId}'::uuid, {status},
            10000, 6000, 0, '{merchantTradeNo}',
            '2026-10-01T04:00:00Z'::timestamptz,
            '2026-10-01T04:30:00Z'::timestamptz{extraValues});
        """;

    private static EcpaySettings Be62Settings() => new(
        Be62MerchantId,
        new Uri("https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5"),
        new Uri("https://payment-stage.ecpay.com.tw/CreditDetail/DoAction"),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(20),
        false);

    private static Dictionary<string, string> SignedInfo(string merchantTradeNo, DateTimeOffset now)
    {
        var fields = CommonNotification(merchantTradeNo, 2, now);
        fields["BankCode"] = "812";
        fields["vAccount"] = "1234567890123456";
        fields["ExpireDate"] = "2026/10/04";
        Sign(fields);
        return fields;
    }

    private static Dictionary<string, string> SignedPayment(string merchantTradeNo, DateTimeOffset now)
    {
        var fields = CommonNotification(merchantTradeNo, 1, now);
        fields["PaymentDate"] = Taipei(now);
        fields["PaymentTypeChargeFee"] = "0";
        Sign(fields);
        return fields;
    }

    private static Dictionary<string, string> CommonNotification(
        string merchantTradeNo,
        int rtnCode,
        DateTimeOffset now) => new(StringComparer.Ordinal)
    {
        ["MerchantID"] = Be62MerchantId,
        ["MerchantTradeNo"] = merchantTradeNo,
        ["TradeNo"] = "BE62-CONCURRENT-TRADE",
        ["RtnCode"] = rtnCode.ToString(CultureInfo.InvariantCulture),
        ["RtnMsg"] = "成功",
        ["TradeAmt"] = "160",
        ["PaymentType"] = "ATM_BOT",
        ["TradeDate"] = Taipei(now),
        ["SimulatePaid"] = "0",
    };

    private static string Taipei(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TaipeiTime.Zone)
            .ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static void Sign(Dictionary<string, string> fields)
    {
        fields["CheckMacValue"] = EcpayGateway.ComputeCheckMacValue(fields, Be62HashKey, Be62HashIv);
    }

    private sealed class SaveGate(int participants)
    {
        private int _remaining = participants;
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                _ready.TrySetResult();
            }

            return _ready.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class GatedUnitOfWork(PaymentDbContext context, SaveGate gate) : IUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            await gate.ArriveAsync(cancellationToken);
            return await context.SaveChangesAsync(cancellationToken);
        }
    }
}
