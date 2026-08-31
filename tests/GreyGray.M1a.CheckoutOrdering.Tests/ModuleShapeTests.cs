using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Infra;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class ModuleShapeTests
{
    [Fact(DisplayName = "Checkout / Ordering Infra 各自只暴露唯一組合根")]
    public void Infra_exposes_only_registration()
    {
        typeof(CheckoutModuleRegistration).Assembly.GetExportedTypes()
            .ShouldBe([typeof(CheckoutModuleRegistration)], ignoreOrder: true);
        typeof(OrderingModuleRegistration).Assembly.GetExportedTypes()
            .ShouldBe([typeof(OrderingModuleRegistration)], ignoreOrder: true);
    }

    [Fact(DisplayName = "兩個 DbContext map 自有 schema 並包含同交易 platform outbox")]
    public void Models_include_business_and_platform_tables()
    {
        using var checkout = new CheckoutDbContext(
            new DbContextOptionsBuilder<CheckoutDbContext>()
                .UseNpgsql("Host=127.0.0.1;Database=model;Username=model;Password=model")
                .Options);
        using var ordering = new OrderingDbContext(
            new DbContextOptionsBuilder<OrderingDbContext>()
                .UseNpgsql("Host=127.0.0.1;Database=model;Username=model;Password=model")
                .Options);

        AssertModel(checkout, "checkout", ["cart", "cart_line"]);
        AssertModel(ordering, "ordering", ["order_line", "orders"]);
    }

    [Fact(DisplayName = "公開 input ports 覆蓋 M1a 單品取消與 M1b-1 operations")]
    public void Public_ports_cover_current_operations()
    {
        typeof(ICheckoutApplication).GetMethods().Select(method => method.Name).ShouldBe(
            ["AddLineAsync", "CompleteAsync", "GetCartAsync", "QuoteAsync", "RemoveLineAsync", "UpdateLineAsync"],
            ignoreOrder: true);
        typeof(IOrderingApplication).GetMethods().Select(method => method.Name).ShouldBe(
            [
                "CancelAdminAsync",
                "CancelCustomerAsync",
                "CancelLineAsync",
                "CreateFromCheckoutAsync",
                "GetAdminAsync",
                "GetCustomerAsync",
                "ListAdminAsync",
                "ListCustomerAsync",
                "RecordPaymentCapturedAsync",
                "RecordPaymentFailedAsync",
                "RecordPaymentRefundedAsync",
                "RecordItemPurchasedAsync",
                "RefundLineShortfallAsync",
            ],
            ignoreOrder: true);
    }

    private static void AssertModel(
        DbContext context,
        string schema,
        IReadOnlyCollection<string> businessTables)
    {
        context.Model.GetDefaultSchema().ShouldBe(schema);
        context.Model.GetEntityTypes()
            .Where(entity => entity.GetSchema() == schema)
            .Select(entity => entity.GetTableName()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(businessTables, ignoreOrder: false);
        context.Model.GetEntityTypes()
            .Where(entity => entity.GetSchema() == "platform")
            .Select(entity => entity.GetTableName()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(
                ["idempotency_key", "outbox_message", "processed_message", "saga_timer"],
                ignoreOrder: false);
    }
}
