using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ledger.Core;
using GreyGray.Modules.Ledger.Infra;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.PaymentLedger.Tests;

public sealed class AccountingInvariantTests
{
    [Fact(DisplayName = "同幣別借貸相等的分錄可入帳")]
    public void Balanced_entry_is_accepted()
    {
        var tenant = TenantId.Default;
        var entry = new JournalEntry(
            EntryId.New(),
            tenant,
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 28, 10, 0, 1, TimeSpan.Zero),
            "Payment",
            "payment-1",
            "收款");
        entry.AddLine(
            new LedgerAccount(AccountId.New(), tenant, AccountCodes.InTransitECPay, "綠界在途", AccountType.Asset),
            Direction.Debit,
            new Money(300_000, Currency.TWD));
        entry.AddLine(
            new LedgerAccount(AccountId.New(), tenant, AccountCodes.DeferredGoodsRevenue, "預收貨款", AccountType.Liability),
            Direction.Credit,
            new Money(280_000, Currency.TWD));
        entry.AddLine(
            new LedgerAccount(AccountId.New(), tenant, AccountCodes.DeferredShippingRevenue, "預收運費", AccountType.Liability),
            Direction.Credit,
            new Money(20_000, Currency.TWD));

        Should.NotThrow(entry.AssertBalanced);
    }

    [Fact(DisplayName = "借貸不等的分錄一定被拒絕")]
    public void Unbalanced_entry_is_rejected()
    {
        var tenant = TenantId.Default;
        var entry = new JournalEntry(
            EntryId.New(),
            tenant,
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 28, 10, 0, 1, TimeSpan.Zero),
            "Payment",
            "payment-2",
            "錯誤分錄");
        entry.AddLine(
            new LedgerAccount(AccountId.New(), tenant, AccountCodes.InTransitECPay, "綠界在途", AccountType.Asset),
            Direction.Debit,
            new Money(300_000, Currency.TWD));
        entry.AddLine(
            new LedgerAccount(AccountId.New(), tenant, AccountCodes.DeferredGoodsRevenue, "預收貨款", AccountType.Liability),
            Direction.Credit,
            new Money(299_999, Currency.TWD));

        Should.Throw<InvalidOperationException>(entry.AssertBalanced)
            .Message.ShouldContain("不平衡");
    }

    [Fact(DisplayName = "分錄不可掛入其他租戶的科目")]
    public void Entry_rejects_an_account_from_another_tenant()
    {
        var entry = new JournalEntry(
            EntryId.New(),
            TenantId.Default,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "Payment",
            "payment-cross-tenant",
            "租戶檢查");
        var anotherTenant = new TenantId(Guid.Parse("00000000-0000-0000-0000-000000000099"));

        Should.Throw<InvalidOperationException>(() => entry.AddLine(
                new LedgerAccount(
                    AccountId.New(),
                    anotherTenant,
                    AccountCodes.Cash,
                    "現金",
                    AccountType.Asset),
                Direction.Debit,
                Money.OfMajor(1, Currency.TWD)))
            .Message.ShouldContain("同一租戶");
    }

    [Fact(DisplayName = "整張退款同時沖回預收貨款與預收運費")]
    public void Full_refund_reverses_goods_and_shipping_liabilities()
    {
        var tenant = TenantId.Default;
        var orderId = OrderId.New();
        var refunded = new PaymentRefunded(
            Guid.CreateVersion7(),
            DateTimeOffset.UnixEpoch,
            tenant,
            RefundId.New(),
            GreyGray.Modules.Payment.Contracts.PaymentId.New(),
            orderId,
            null,
            PaymentProvider.ECPay,
            Money.OfMajor(160, Currency.TWD),
            RefundDestination.StoredValue);
        var order = new OrderView(
            orderId,
            GreyGray.Modules.Identity.Contracts.CustomerId.New(),
            SourceChannel.Own,
            OrderStatus.PaidAwaitingClose,
            GreyGray.Modules.Checkout.Contracts.ShippingPolicy.HoldUntilComplete,
            GreyGray.Modules.Pricing.Contracts.PricingSnapshotId.New(),
            Money.OfMajor(100, Currency.TWD),
            Money.OfMajor(60, Currency.TWD),
            Money.OfMajor(160, Currency.TWD),
            [],
            DateTimeOffset.UnixEpoch);

        var lines = PaymentRefundedLedgerHandler.LiabilityLinesFor(refunded, order);

        lines.Count.ShouldBe(2);
        lines.ShouldContain(line =>
            line.AccountCode == AccountCodes.DeferredGoodsRevenue &&
            line.Amount == Money.OfMajor(100, Currency.TWD));
        lines.ShouldContain(line =>
            line.AccountCode == AccountCodes.DeferredShippingRevenue &&
            line.Amount == Money.OfMajor(60, Currency.TWD));
    }

    [Fact(DisplayName = "單一品項退款只沖預收貨款，且品項額大於調整後訂單總額時仍借貸相等")]
    public void Line_refund_is_balanced_against_the_refund_destination()
    {
        var tenant = TenantId.Default;
        var orderId = OrderId.New();
        var lineId = OrderLineId.New();
        var amount = Money.OfMajor(100, Currency.TWD);
        var refunded = new PaymentRefunded(
            Guid.CreateVersion7(),
            DateTimeOffset.UnixEpoch,
            tenant,
            RefundId.New(),
            GreyGray.Modules.Payment.Contracts.PaymentId.New(),
            orderId,
            lineId,
            PaymentProvider.ECPay,
            amount,
            RefundDestination.StoredValue);
        var order = new OrderView(
            orderId,
            GreyGray.Modules.Identity.Contracts.CustomerId.New(),
            SourceChannel.Own,
            OrderStatus.PaidAwaitingClose,
            GreyGray.Modules.Checkout.Contracts.ShippingPolicy.HoldUntilComplete,
            GreyGray.Modules.Pricing.Contracts.PricingSnapshotId.New(),
            Money.OfMajor(40, Currency.TWD),
            Money.OfMajor(20, Currency.TWD),
            Money.OfMajor(60, Currency.TWD),
            [
                new OrderLineView(
                    lineId,
                    GreyGray.Modules.Catalog.Contracts.SkuId.New(),
                    GreyGray.Modules.Catalog.Contracts.FulfillmentMode.Preorder,
                    OrderLineStatus.Unavailable,
                    1,
                    amount,
                    GreyGray.Modules.Campaign.Contracts.CampaignId.New(),
                    null),
            ],
            DateTimeOffset.UnixEpoch);

        var lines = PaymentRefundedLedgerHandler.LiabilityLinesFor(refunded, order).ToList();
        lines.Add(new PostingLine(
            AccountCodes.CustomerStoredValue,
            Direction.Credit,
            amount));

        lines.ShouldContain(line =>
            line.AccountCode == AccountCodes.DeferredGoodsRevenue &&
            line.Direction == Direction.Debit &&
            line.Amount == amount);
        lines.Where(line => line.Direction == Direction.Debit).Sum(line => line.Amount.AmountMinor)
            .ShouldBe(lines.Where(line => line.Direction == Direction.Credit).Sum(line => line.Amount.AmountMinor));
    }

    [Fact(DisplayName = "Ledger EF model 對齊 schema、同租戶複合外鍵與 outbox")]
    public void Ledger_model_contains_business_invariants_and_platform_outbox()
    {
        using var context = new LedgerDbContext(
            new DbContextOptionsBuilder<LedgerDbContext>()
                .UseNpgsql("Host=localhost;Database=greygray;Username=greygray;Password=greygray")
                .Options);

        var entry = context.Model.FindEntityType(typeof(JournalEntry))!;
        var line = context.Model.FindEntityType(typeof(JournalLine))!;
        entry.GetSchema().ShouldBe("ledger");
        line.GetSchema().ShouldBe("ledger");
        context.Model.FindEntityType(typeof(OutboxMessage))!.GetSchema().ShouldBe("platform");
        line.GetForeignKeys().Any(foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(JournalLine.TenantId), nameof(JournalLine.EntryId) }))
            .ShouldBeTrue();
        line.GetForeignKeys().Any(foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(JournalLine.TenantId), nameof(JournalLine.AccountId) }))
            .ShouldBeTrue();
    }

    [Fact(DisplayName = "Payment EF model 對齊 payment schema 並與 outbox 共用 DbContext")]
    public void Payment_model_contains_payment_and_platform_tables()
    {
        using var context = new PaymentDbContext(
            new DbContextOptionsBuilder<PaymentDbContext>()
                .UseNpgsql("Host=localhost;Database=greygray;Username=greygray;Password=greygray")
                .Options);

        context.Model.FindEntityType(typeof(Payment))!.GetSchema().ShouldBe("payment");
        context.Model.FindEntityType(typeof(OutboxMessage))!.GetSchema().ShouldBe("platform");
        var payment = context.Model.FindEntityType(typeof(Payment))!;
        payment.FindProperty(nameof(Payment.Status))!
            .IsConcurrencyToken.ShouldBeTrue();
        payment.GetIndexes().ShouldContain(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[] { nameof(Payment.TenantId), nameof(Payment.OrderId) }) &&
            index.GetFilter() == "status IN (0, 1, 4)");
    }

    [Fact(DisplayName = "Infra 只匯出各自組合根且缺設定時登錄仍為 lazy")]
    public void Infra_exports_only_module_registration_and_registration_is_lazy()
    {
        typeof(PaymentModuleRegistration).Assembly.GetExportedTypes()
            .ShouldBe([typeof(PaymentModuleRegistration)], ignoreOrder: true);
        typeof(LedgerModuleRegistration).Assembly.GetExportedTypes()
            .ShouldBe([typeof(LedgerModuleRegistration)], ignoreOrder: true);

        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddPaymentModule(configuration);
        services.AddLedgerModule(configuration);
        services.Any(descriptor => descriptor.ServiceType ==
            typeof(IIntegrationEventHandler<PaymentRequested>)).ShouldBeTrue();
        services.Any(descriptor => descriptor.ServiceType ==
            typeof(IIntegrationEventHandler<RefundRequested>)).ShouldBeTrue();
        using var provider = services.BuildServiceProvider();
        provider.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Ledger 的 UUID 次排序可由 Npgsql 轉譯")]
    public void Ledger_uuid_tie_breaker_is_translatable()
    {
        using var context = new LedgerDbContext(
            new DbContextOptionsBuilder<LedgerDbContext>()
                .UseNpgsql("Host=localhost;Database=greygray;Username=greygray;Password=greygray")
                .Options);

        var sql = context.Entries.AsNoTracking()
            .Where(entry => entry.PostedAt < DateTimeOffset.UnixEpoch
                || (entry.PostedAt == DateTimeOffset.UnixEpoch
                    && entry.Id < new EntryId(Guid.Parse("0198c3d4-e5f6-7018-9abc-000000000001"))))
            .OrderByDescending(entry => entry.PostedAt)
            .ThenByDescending(entry => entry.Id)
            .Take(10)
            .ToQueryString();
        sql.ShouldContain("ORDER BY");
    }
}
