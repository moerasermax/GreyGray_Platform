using System.Text.Json;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using GreyGray.Shared.Kernel.Json;
using Shouldly;
using Xunit;

namespace GreyGray.Contracts.Tests;

/// <summary>
/// 線上格式的斷言（ADR-018）。
/// </summary>
/// <remarks>
/// <b>這些不是「順便測一下」的測試。</b>outbox 裡的 JSON 一旦有正式資料就改不動了——
/// 改形狀等於讓所有未派送的訊息反序列化失敗。而前端的 <c>@greygray/api-client</c>
/// 也是照同一份形狀寫的。這一組測試紅掉，代表前後端的契約已經破了。
/// <para>
/// 要驗證這些測試真的會失敗：把 <c>GreyGrayJson</c> 裡任何一個 converter 拿掉再跑一次。
/// </para>
/// </remarks>
public sealed class WireFormatTests
{
    private static readonly JsonSerializerOptions Options = GreyGrayJson.Options;

    [Fact(DisplayName = "Money 序列化成 {amountMinor, currency}，幣別是字串")]
    public void Money_uses_minor_units_and_string_currency()
    {
        JsonSerializer.Serialize(new Money(18000, Currency.TWD), Options)
            .ShouldBe("""{"amountMinor":18000,"currency":"TWD"}""");

        // JPY 沒有小數位。把它當成兩位小數，¥1000 會被顯示成 ¥10。
        JsonSerializer.Serialize(new Money(1000, Currency.JPY), Options)
            .ShouldBe("""{"amountMinor":1000,"currency":"JPY"}""");
    }

    [Fact(DisplayName = "強型別 ID 序列化成純字串，不是 {\"value\":…}")]
    public void Strongly_typed_ids_serialize_as_bare_strings()
    {
        var id = new CustomerId(Guid.Parse("0198c3d4-e5f6-7018-9abc-0123456789ab"));

        JsonSerializer.Serialize(id, Options)
            .ShouldBe("\"0198c3d4e5f670189abc0123456789ab\"");

        JsonSerializer.Deserialize<CustomerId>("\"0198c3d4e5f670189abc0123456789ab\"", Options)
            .ShouldBe(id);
    }

    [Fact(DisplayName = "裸 Guid 與強型別 ID 用同一種格式")]
    public void Raw_guid_uses_the_same_format_as_typed_ids()
    {
        // 少了這條，同一個 payload 裡的 eventId 會帶連字號而 orderId 不會。
        // 而 eventId 正是消費端去重的 key——一邊 Guid.Parse 比、一邊字串比，
        // 就會出現「同一則訊息被處理兩次」，且只在正式環境的重送路徑上出現。
        var guid = Guid.Parse("0198c3d4-0000-7000-8000-000000000001");

        JsonSerializer.Serialize(guid, Options)
            .ShouldBe("\"0198c3d4000070008000000000000001\"");

        // 讀取端寬鬆：先前若已有帶連字號的資料仍讀得回來。
        JsonSerializer.Deserialize<Guid>("\"0198c3d4-0000-7000-8000-000000000001\"", Options)
            .ShouldBe(guid);
    }

    [Fact(DisplayName = "裸 Guid? 序列化不丟例外，格式跟裸 Guid 一致（#59 迴歸）")]
    public void Nullable_raw_guid_does_not_throw()
    {
        // Nullable<Guid> 滿足 GuidIdJsonConverterFactory.CanConvert 原本的五個條件
        // （value type、非 primitive、非 enum、Value 屬性型別是 Guid、有吃 Guid 的建構式），
        // 但 GuidIdJsonConverter<TId> 要求 TId : struct，Nullable<T> 不滿足這個限制，
        // 修之前這裡會丟 TypeLoadException。
        var sample = Guid.Parse("0198c3d4-0000-7000-8000-000000000001");

        JsonSerializer.Serialize((Guid?)sample, Options)
            .ShouldBe("\"0198c3d4000070008000000000000001\"");
        JsonSerializer.Serialize((Guid?)null, Options)
            .ShouldBe("null");

        JsonSerializer.Deserialize<Guid?>("\"0198c3d4000070008000000000000001\"", Options)
            .ShouldBe(sample);
        JsonSerializer.Deserialize<Guid?>("null", Options)
            .ShouldBeNull();
    }

    [Fact(DisplayName = "可為 null 的強型別 ID：null 寫成 null、有值寫成無連字號 32 碼字串")]
    public void Nullable_typed_id_serializes_correctly()
    {
        var id = new CustomerId(Guid.Parse("0198c3d4-e5f6-7018-9abc-0123456789ab"));

        JsonSerializer.Serialize((CustomerId?)id, Options)
            .ShouldBe("\"0198c3d4e5f670189abc0123456789ab\"");
        JsonSerializer.Serialize((CustomerId?)null, Options)
            .ShouldBe("null");

        JsonSerializer.Deserialize<CustomerId?>("\"0198c3d4e5f670189abc0123456789ab\"", Options)
            .ShouldBe(id);
        JsonSerializer.Deserialize<CustomerId?>("null", Options)
            .ShouldBeNull();
    }

    [Theory(DisplayName = "enum 一律寫字串，不是數字")]
    [InlineData(DeliveryMethod.ConvenienceStore, "\"ConvenienceStore\"")]
    [InlineData(DeliveryMethod.HomeDelivery, "\"HomeDelivery\"")]
    public void Enums_serialize_as_strings(DeliveryMethod method, string expected)
        => JsonSerializer.Serialize(method, Options).ShouldBe(expected);

    [Fact(DisplayName = "訂單狀態的九個成員名稱不可改——改名等於破壞契約")]
    public void Order_status_names_are_frozen()
    {
        // 這些字串會出現在 outbox payload、API 回應與前端的 union type 裡。
        Enum.GetNames<OrderStatus>().ShouldBe(
            [
                "AwaitingPayment", "PaidAwaitingClose", "ClosedAwaitingDeparture",
                "Purchasing", "GoodsReceived", "ReadyToShip", "Shipped",
                "Completed", "Cancelled",
            ],
            ignoreOrder: true);
    }

    [Fact(DisplayName = "中日韓文字不被轉義成 \\uXXXX")]
    public void Cjk_text_is_not_escaped()
    {
        // 轉義掉的話備註與姓名在 DB 裡會變成一串 \uXXXX，用 SQL 根本查不動。
        JsonSerializer.Serialize(new { memo = "首爾藥妝團・機票" }, Options)
            .ShouldContain("首爾藥妝團");
    }

    [Fact(DisplayName = "null 照寫不省略")]
    public void Nulls_are_written_explicitly()
    {
        // 省掉之後看不出是「沒有值」還是「當時沒有這個欄位」。
        JsonSerializer.Serialize(new { campaignOfferId = (string?)null }, Options)
            .ShouldBe("""{"campaignOfferId":null}""");
    }

    [Fact(DisplayName = "整合事件 round-trip 無失真")]
    public void Integration_event_round_trips_without_loss()
    {
        var original = SampleOrderPlaced();

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<OrderPlaced>(json, Options);

        restored.ShouldNotBeNull();
        restored.OrderId.ShouldBe(original.OrderId);
        restored.GoodsTotal.ShouldBe(original.GoodsTotal);
        restored.OccurredAt.ShouldBe(original.OccurredAt);
        restored.Lines.Count.ShouldBe(1);
        restored.Lines[0].UnitPrice.ShouldBe(original.Lines[0].UnitPrice);
        restored.Lines[0].CampaignOfferId.ShouldBeNull();
    }

    [Fact(DisplayName = "事件 payload 的欄位名是 camelCase，且帶聚合識別")]
    public void Event_payload_shape_is_stable()
    {
        var json = JsonSerializer.Serialize(SampleOrderPlaced(), Options);

        json.ShouldContain("\"eventId\":\"0198c3d4000070008000000000000001\"");
        json.ShouldContain("\"tenantId\":\"00000000000000000000000000000001\"");
        json.ShouldContain("\"source\":\"Own\"");
        json.ShouldContain("""goodsTotal":{"amountMinor":280000,"currency":"TWD"}""");
        json.ShouldContain("\"campaignOfferId\":null");
        json.ShouldContain("\"aggregateType\":\"Order\"");
        json.ShouldContain("\"aggregateId\":\"0198c3d4111170008000000000000002\"");
    }

    private static OrderPlaced SampleOrderPlaced() => new(
        EventId: Guid.Parse("0198c3d4-0000-7000-8000-000000000001"),
        OccurredAt: new DateTimeOffset(2026, 8, 28, 14, 30, 0, TimeSpan.FromHours(8)),
        TenantId: TenantId.Default,
        OrderId: new OrderId(Guid.Parse("0198c3d4-1111-7000-8000-000000000002")),
        CustomerId: new CustomerId(Guid.Parse("0198c3d4-e5f6-7018-9abc-0123456789ab")),
        Source: SourceChannel.Own,
        ShippingPolicy: ShippingPolicy.HoldUntilComplete,
        PricingSnapshotId: new PricingSnapshotId(Guid.Parse("0198c3d4-2222-7000-8000-000000000003")),
        GoodsTotal: new Money(280000, Currency.TWD),
        ShippingFee: new Money(6000, Currency.TWD),
        Lines:
        [
            new OrderPlacedLine(
                LineId: new OrderLineId(Guid.Parse("0198c3d4-3333-7000-8000-000000000004")),
                SkuId: new SkuId(Guid.Parse("0198c3d4-4444-7000-8000-000000000005")),
                Mode: FulfillmentMode.Preorder,
                CampaignId: new CampaignId(Guid.Parse("0198c3d4-5555-7000-8000-000000000006")),
                CampaignOfferId: null,
                Quantity: 2,
                UnitPrice: new Money(78000, Currency.TWD)),
        ]);
}
