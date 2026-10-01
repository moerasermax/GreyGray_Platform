using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GreyGray.Modules.Audit.Contracts;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Notification.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Reporting.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;

namespace GreyGray.Platform.Tests;

public sealed class EventTypeRegistryTests
{
    [Fact]
    public void Registry_contains_the_exact_45_event_catalog_entries()
    {
        var registry = EventTypeRegistry.FromAssemblies(EventCatalog.ContractAssemblies);

        registry.KnownEventTypes.Count.ShouldBe(45);
        registry.KnownEventTypes
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(EventCatalog.ExpectedEventTypes)
            .ShouldBeTrue();
    }

    [Fact]
    public void Duplicate_event_type_fails_during_registry_construction()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            EventTypeRegistry.FromAssemblies([typeof(DuplicateEventOne).Assembly]));

        exception.Message.ShouldContain("重複的整合事件型別名");
        exception.Message.ShouldContain(DuplicateEventOne.EventType);
    }

    [Fact]
    public void Generated_json_type_info_round_trips_with_the_GreyGray_wire_format()
    {
        var registry = EventTypeRegistry.FromAssemblies(EventCatalog.ContractAssemblies);
        var original = TestEvents.CustomerRegistered(Guid.Parse("0198c3d4-e5f6-7018-9abc-0123456789ab"));

        var json = JsonSerializer.Serialize(
            original,
            registry.JsonTypeInfoOf(typeof(CustomerRegistered)));
        var roundTrip = (CustomerRegistered?)JsonSerializer.Deserialize(
            json,
            registry.JsonTypeInfoOf(typeof(CustomerRegistered)));

        roundTrip.ShouldBe(original);
        json.ShouldContain("\"eventId\":\"0198c3d4e5f670189abc0123456789ab\"");
        json.ShouldContain("灰灰");
        json.ShouldNotContain("0198c3d4-e5f6");
    }
}

internal static class EventCatalog
{
    public static readonly Assembly[] ContractAssemblies =
    [
        typeof(CustomerRegistered).Assembly,
        typeof(SkuPublished).Assembly,
        typeof(FeeRuleSetPublished).Assembly,
        typeof(CampaignPublished).Assembly,
        typeof(LotCreated).Assembly,
        typeof(CheckoutCompleted).Assembly,
        typeof(OrderPlaced).Assembly,
        typeof(ItemPurchased).Assembly,
        typeof(ShipmentDispatched).Assembly,
        typeof(PaymentCaptured).Assembly,
        typeof(JournalPosted).Assembly,
        typeof(NotificationSent).Assembly,
        typeof(AuditRecorded).Assembly,
        typeof(ReportGenerated).Assembly,
    ];

    public static readonly HashSet<string> ExpectedEventTypes = new(StringComparer.Ordinal)
    {
        "iam.CustomerRegistered.v1", "iam.CustomerDeactivated.v1",
        "catalog.SkuPublished.v1", "catalog.SkuArchived.v1", "catalog.SkuAttributesChanged.v1",
        "pricing.FeeRuleSetPublished.v1",
        "campaign.CampaignPublished.v1", "campaign.CampaignClosed.v1",
        "campaign.TripCostRecorded.v1", "campaign.CampaignCancelled.v1", "campaign.CampaignSettled.v1",
        "inventory.LotCreated.v1", "inventory.StockReserved.v1", "inventory.StockReleased.v1",
        "inventory.StockCostAllocated.v1", "checkout.CheckoutCompleted.v1",
        "ordering.OrderPlaced.v1", "ordering.PaymentRequested.v1", "ordering.OrderPaid.v1",
        "ordering.OrderReadyToShip.v1", "ordering.OrderCompleted.v1", "ordering.OrderCancelled.v1",
        "ordering.OrderLineCancelled.v1", "ordering.RefundRequested.v1",
        "procurement.ItemPurchased.v1", "procurement.GoodsReceived.v1", "procurement.ItemUnavailable.v1",
        "procurement.ItemPriceChanged.v1", "procurement.InquiryResolved.v1",
        "fulfillment.ShipmentDispatched.v1", "fulfillment.ShipmentDelivered.v1",
        "fulfillment.ReturnReceived.v1", "fulfillment.ShipmentLost.v1",
        "payment.PaymentCaptured.v1", "payment.PaymentFailed.v1", "payment.PaymentRefunded.v1",
        "payment.PaymentInstructionsIssued.v1",
        "payment.PayoutSettled.v1", "payment.ReconciliationDiscrepancyFound.v1",
        "ledger.JournalPosted.v1", "ledger.LiabilityExceededCash.v1",
        "notify.NotificationSent.v1", "notify.NotificationFailed.v1",
        "audit.AuditRecorded.v1", "reporting.ReportGenerated.v1",
    };
}

internal static class TestEvents
{
    public static readonly DateTimeOffset OccurredAt =
        new(2026, 8, 28, 6, 30, 0, TimeSpan.Zero);

    public static CustomerRegistered CustomerRegistered(Guid eventId) => new(
        eventId,
        OccurredAt,
        TenantId.Default,
        new CustomerId(Guid.CreateVersion7()),
        "灰灰");
}

public sealed record DuplicateEventOne(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "probe.Duplicate.v1";
    public override string AggregateType => "Probe";
    public override string AggregateId => "one";
}

public sealed record DuplicateEventTwo(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "probe.Duplicate.v1";
    public override string AggregateType => "Probe";
    public override string AggregateId => "two";
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DuplicateEventOne))]
[JsonSerializable(typeof(DuplicateEventTwo))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
