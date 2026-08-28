using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Pricing.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct PricingSnapshotId(Guid Value)
{
    public static PricingSnapshotId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

/// <summary>指向<b>具體版本</b>的規則集。訂單快照存這個，不存 code。</summary>
public readonly record struct FeeRuleSetId(Guid Value)
{
    public static FeeRuleSetId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct FeeRuleId(Guid Value)
{
    public static FeeRuleId New() => new(Guid.CreateVersion7());
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

/// <summary>
/// 配送方式。<b>只有國內配送</b>——貨是你親自帶回來的，沒有國際段、沒有集運。
/// </summary>
/// <remarks>
/// 這個列舉放在 Pricing 而不是 Fulfillment，理由是相依方向：
/// 運費就是按配送方式計的，Pricing 是需要它的最底層模組（Contracts DAG 的上游）。
/// Fulfillment.Contracts 反過來參考這裡，避免 Pricing ↔ Fulfillment 的循環參考。
/// </remarks>
public enum DeliveryMethod
{
    /// <summary>超商取貨。M1a 一口價 NT$60（ADR-010）。</summary>
    ConvenienceStore = 1,

    /// <summary>宅配。M1a 一口價 NT$120（ADR-010）。</summary>
    HomeDelivery = 2,

    /// <summary>面交／自取。運費 0。</summary>
    SelfPickup = 3,
}

/// <summary>
/// 運費策略。<b>不要做通用公式 DSL</b>——那是規則引擎最常見的過度設計，
/// 代價是型別安全消失、無法測試、使用者能寫出除以零，六個月後沒人看得懂。
/// 正解是有限的具名策略：加新計價方式＝加一個 record ＋ 一組參數，而不是擴充語法。
/// </summary>
public enum ShippingStrategyKind
{
    /// <summary>一口價。<b>M1a 唯一需要的策略</b>。</summary>
    Flat = 1,

    /// <summary>計費重量級距。M3 才啟用——等發現大件商品在虧運費時。</summary>
    WeightBracket = 2,

    /// <summary>滿額免運。M3。</summary>
    FreeOver = 3,
}

public enum FeeRuleSetStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2,
}

// ── 詢價的輸入與輸出 ─────────────────────────────────────────────────────

public sealed record QuoteLineRequest(
    SkuId SkuId,
    int Quantity,
    int WeightGram,
    Dimensions Size);

public sealed record QuoteRequest(
    DeliveryMethod DeliveryMethod,
    IReadOnlyList<QuoteLineRequest> Lines,
    CustomerId? CustomerId,
    MemberTier MemberTier);

/// <summary>
/// 報價快照。<b>下單瞬間凍結，之後不可變</b>。
/// 之後改運費規則不影響已成立訂單；<see cref="Explain"/> 讓客服能直接回答
/// 「為什麼收這麼多」，不用去翻程式碼。
/// </summary>
/// <remarks>
/// M1a 費率雖然寫死一口價，這個快照<b>仍然要完整記錄</b>——
/// 否則 M3 導入規則引擎時無法回溯比對。
/// </remarks>
public sealed record PricingSnapshot(
    PricingSnapshotId Id,
    DeliveryMethod DeliveryMethod,
    int ActualWeightGram,
    int VolumetricWeightGram,
    int BillableWeightGram,
    Money ShippingFee,
    FeeRuleSetId AppliedRuleSetId,
    FeeRuleId? AppliedRuleId,
    ShippingStrategyKind AppliedStrategy,
    IReadOnlyList<string> Explain,
    DateTimeOffset CreatedAt);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface IPricingQuotation
{
    /// <summary>
    /// 詢價。<b>純函式語意</b>——不寫入任何東西，同樣輸入永遠同樣輸出（給定同一個 ACTIVE 規則集）。
    /// Checkout 顯示金額時呼叫，下單時再呼叫一次並凍結。
    /// </summary>
    Task<Result<PricingSnapshot>> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken);

    /// <summary>把詢價結果凍結成不可變快照，回傳的 Id 由 Ordering 存進訂單。</summary>
    Task<Result<PricingSnapshotId>> FreezeAsync(PricingSnapshot snapshot, CancellationToken cancellationToken);

    Task<Result<PricingSnapshot>> GetSnapshotAsync(PricingSnapshotId id, CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

public sealed record FeeRuleSetPublished(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    FeeRuleSetId RuleSetId,
    string Code,
    int Version,
    DateTimeOffset EffectiveFrom)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "pricing.FeeRuleSetPublished.v1";
}
