using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Inventory.Core;

/// <summary>
/// 批號聚合。<b>目前只服務 M1b-3 帶回入庫（<see cref="LotSource.OverseasPurchase"/>）</b>——
/// M2 批發進貨與拒收退回轉現貨建立批號的規則留給那兩包自己補，不在這裡預先假設。
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
        Guid fromCampaignId,
        string? batchCode,
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
            receivedAt);
    }
}

/// <summary>批號聚合的持久層接縫；只需要新增，帶回入庫不需要讀回既有批號。</summary>
internal interface IInventoryLotRepository
{
    void Add(LotAggregate lot);
}
