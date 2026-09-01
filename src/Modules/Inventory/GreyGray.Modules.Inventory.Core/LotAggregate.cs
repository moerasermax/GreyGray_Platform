using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Inventory.Core;

/// <summary>
/// 批號聚合。目前服務兩條建立路徑：M1b-3 帶回入庫（<see cref="LotSource.OverseasPurchase"/>）
/// 與 M2 批發進貨（<see cref="LotSource.LocalWholesale"/>）。
/// <b>拒收退回轉現貨（<see cref="LotSource.CustomerReturn"/>）仍然沒有實作</b>，
/// 成本要沿用原採購成本，規則留給那一包自己補，不在這裡預先假設。
/// </summary>
internal sealed class LotAggregate
{
    private LotAggregate()
    {
    }

    private LotAggregate(
        Guid id,
        Guid tenantId,
        Guid skuId,
        LotSource source,
        Money unitCost,
        int quantityOnHand,
        Guid? fromCampaignId,
        string? batchCode,
        string? creationIdempotencyKey,
        DateTimeOffset receivedAt)
    {
        Id = id;
        TenantId = tenantId;
        SkuId = skuId;
        Source = source;
        UnitCostAmountMinor = unitCost.AmountMinor;
        UnitCostCurrency = unitCost.Currency;
        QuantityOnHand = quantityOnHand;
        QuantityReserved = 0;
        QuantityChannelAllocated = 0;
        FromCampaignId = fromCampaignId;
        BatchCode = batchCode;
        CreationIdempotencyKey = creationIdempotencyKey;
        ReceivedAt = receivedAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid SkuId { get; private set; }

    public LotSource? Source { get; private set; }

    public long? UnitCostAmountMinor { get; private set; }

    public Currency? UnitCostCurrency { get; private set; }

    public int QuantityOnHand { get; private set; }

    public int QuantityReserved { get; private set; }

    public int QuantityChannelAllocated { get; private set; }

    public int QuantityAvailable { get; private set; }

    public Guid? FromCampaignId { get; private set; }

    public string? BatchCode { get; private set; }

    /// <summary>
    /// 批發進貨建立這個批號時呼叫端帶進來的冪等鍵。<b>可為 null</b>——
    /// 帶回入庫那條線的冪等由 <c>platform.processed_message</c> 保證（同一個
    /// <c>GoodsReceived</c> 只會被交付一次），不需要也不會有這把鍵；
    /// 這個欄位出現之前建立的批號同樣沒有。所以資料庫那條唯一索引是過濾式的。
    /// </summary>
    public string? CreationIdempotencyKey { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    /// <summary>
    /// 現場帶回入庫建立新批號。<b>冪等由呼叫端的 processed-message 包裝保證</b>，
    /// 這裡不再另外去重——同一個 <c>GoodsReceived</c> 事件只會被交付一次。
    /// </summary>
    public static Result<LotAggregate> CreateFromGoodsReceived(
        LotId id,
        TenantId tenantId,
        SkuId skuId,
        int quantity,
        Money unitCost,
        LotSource source,
        CampaignId fromCampaignId,
        DateTimeOffset receivedAt)
    {
        if (quantity <= 0)
        {
            return Result<LotAggregate>.Failure(
                "inventory.invalid-goods-received-quantity",
                "帶回入庫的數量必須大於零。");
        }

        if (unitCost.IsNegative || !Enum.IsDefined(unitCost.Currency))
        {
            return Result<LotAggregate>.Failure(
                "inventory.invalid-goods-received-unit-cost",
                "帶回入庫的批號成本不得為負數，且必須使用已知幣別。");
        }

        if (!Enum.IsDefined(source))
        {
            return Result<LotAggregate>.Failure(
                "inventory.invalid-lot-source",
                "批號來源必須是已知的來源類型。");
        }

        return new LotAggregate(
            id.Value,
            tenantId.Value,
            skuId.Value,
            source,
            unitCost,
            quantity,
            fromCampaignId.Value,
            batchCode: null,
            creationIdempotencyKey: null,
            receivedAt);
    }

    /// <summary>
    /// M2 台灣本地批發進貨建立新批號。<b>沒有團</b>（STOCK 模式，賣之前就進來了），
    /// 所以 <c>FromCampaignId</c> 一律是 null——<c>inventory.lot</c> 的
    /// <c>from_campaign_id</c> 本來就可為 null，不必為此改 schema。
    /// <para>
    /// <paramref name="creationIdempotencyKey"/> 是呼叫端帶進來的 <c>Idempotency-Key</c>，
    /// 用來擋掉「同一次進貨動作重送建出第二個批號」；<b>不是</b>用來擋
    /// 「同一個 SKU 分多批進貨」——那正是批號存在的理由。
    /// </para>
    /// </summary>
    public static Result<LotAggregate> CreateFromWholesale(
        LotId id,
        TenantId tenantId,
        SkuId skuId,
        int quantity,
        Money unitCost,
        string? batchCode,
        string creationIdempotencyKey,
        DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(creationIdempotencyKey) || creationIdempotencyKey.Length > 255)
        {
            return Result<LotAggregate>.Failure(
                "platform.idempotency-key-required",
                "批發進貨必須提供 1 到 255 字元的 Idempotency-Key。");
        }

        if (quantity <= 0)
        {
            return Result<LotAggregate>.Failure(
                "inventory.invalid-wholesale-quantity",
                "批發進貨的數量必須大於零。");
        }

        if (unitCost.IsNegative || !Enum.IsDefined(unitCost.Currency))
        {
            return Result<LotAggregate>.Failure(
                "inventory.invalid-wholesale-unit-cost",
                "批發進貨的批號成本不得為負數，且必須使用已知幣別。");
        }

        // batch_code 是 varchar(64)。超長是可預期的使用者輸入，要回業務失敗，
        // 不能讓它一路走到資料庫才變成例外（六條鐵則第 5 條）。
        if (batchCode is { Length: > 64 })
        {
            return Result<LotAggregate>.Failure(
                "inventory.invalid-batch-code",
                "批號代碼最多 64 個字元。");
        }

        return new LotAggregate(
            id.Value,
            tenantId.Value,
            skuId.Value,
            LotSource.LocalWholesale,
            unitCost,
            quantity,
            fromCampaignId: null,
            string.IsNullOrWhiteSpace(batchCode) ? null : batchCode.Trim(),
            creationIdempotencyKey.Trim(),
            receivedAt);
    }

    /// <summary>
    /// 轉成對外契約。<b>成本／來源／入庫時間缺任何一項就直接炸</b>——
    /// <c>inventory.lot</c> 的 <c>lot_receipt_fields_consistent</c> 保證這四個欄位
    /// 要嘛全有要嘛全無，而全無的列只可能來自 0010 之前；程式裡沒有任何路徑建得出來。
    /// 真的遇到就是「不該發生」，要吵出來，不是安靜編一個 0 元成本或猜一個來源
    /// （六條鐵則第 5 條）。
    /// </summary>
    public Contracts.Lot ToContract()
    {
        if (Source is not { } source
            || UnitCostAmountMinor is not { } amountMinor
            || UnitCostCurrency is not { } currency
            || ReceivedAt is not { } receivedAt)
        {
            throw new InvalidOperationException(
                $"批號 {Id:N} 缺少成本／來源／入庫時間，無法轉成對外契約——" +
                "這種列只可能建立於 db/migrations/0010 之前。");
        }

        return new Contracts.Lot(
            new LotId(Id),
            new SkuId(SkuId),
            source,
            new Money(amountMinor, currency),
            QuantityOnHand,
            QuantityReserved,
            FromCampaignId is { } campaignId ? new CampaignId(campaignId) : null,
            BatchCode,
            receivedAt)
        {
            QuantityChannelAllocated = QuantityChannelAllocated,
        };
    }
}

/// <summary>批號聚合的持久層接縫。</summary>
internal interface IInventoryLotRepository
{
    void Add(LotAggregate lot);

    /// <summary>依冪等鍵讀回批發進貨建立的批號；沒有就是 null（＝這是新的一批）。</summary>
    Task<LotAggregate?> GetByCreationKeyAsync(
        TenantId tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<LotQueryPage> ListAsync(
        TenantId tenantId,
        AdminLotListRequest request,
        CancellationToken cancellationToken);
}

/// <summary>持久層回給應用服務的一頁批號；<c>NextCursor</c> 為 null 表示沒有下一頁。</summary>
internal sealed record LotQueryPage(IReadOnlyList<LotAggregate> Items, LotId? NextCursor);
