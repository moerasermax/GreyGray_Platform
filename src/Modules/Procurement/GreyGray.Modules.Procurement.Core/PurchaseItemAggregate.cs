using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Procurement.Core;

internal sealed class PurchaseItemAggregate
{
    private PurchaseItemAggregate()
    {
    }

    private PurchaseItemAggregate(
        PurchaseItemId id,
        TenantId tenantId,
        CampaignId campaignId,
        SkuId skuId,
        OrderLineId orderLineId,
        int quantityRequested,
        Money? targetPrice,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        CampaignId = campaignId;
        SkuId = skuId;
        OrderLineId = orderLineId;
        QuantityRequested = quantityRequested;
        TargetPriceAmountMinor = targetPrice?.AmountMinor;
        TargetPriceCurrency = targetPrice?.Currency;
        Status = PurchaseItemStatus.Pending;
        CreatedAt = createdAt;
    }

    public PurchaseItemId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public CampaignId CampaignId { get; private set; }

    public SkuId SkuId { get; private set; }

    public OrderLineId OrderLineId { get; private set; }

    public int QuantityRequested { get; private set; }

    public int QuantityPurchased { get; private set; }

    public long? TargetPriceAmountMinor { get; private set; }

    public Currency? TargetPriceCurrency { get; private set; }

    public long? ActualPaidOriginalAmountMinor { get; private set; }

    public Currency? ActualPaidOriginalCurrency { get; private set; }

    public long? ActualPaidBookingAmountMinor { get; private set; }

    public Currency? ActualPaidBookingCurrency { get; private set; }

    public FxSnapshotId? ActualPaidFxSnapshotId { get; private set; }

    public PurchaseItemStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public Money? TargetPrice => TargetPriceAmountMinor is { } amount
        && TargetPriceCurrency is { } currency
            ? new Money(amount, currency)
            : null;

    public MoneyPair? ActualPaid => ActualPaidOriginalAmountMinor is { } originalAmount
        && ActualPaidOriginalCurrency is { } originalCurrency
        && ActualPaidBookingAmountMinor is { } bookingAmount
        && ActualPaidBookingCurrency is { } bookingCurrency
            ? new MoneyPair(
                new Money(originalAmount, originalCurrency),
                new Money(bookingAmount, bookingCurrency),
                ActualPaidFxSnapshotId)
            : null;

    public static Result<PurchaseItemAggregate> Create(
        PurchaseItemId id,
        TenantId tenantId,
        CampaignId campaignId,
        SkuId skuId,
        OrderLineId orderLineId,
        int quantityRequested,
        Money? targetPrice,
        DateTimeOffset createdAt)
    {
        if (quantityRequested is < 1 or > 999)
        {
            return Result<PurchaseItemAggregate>.Failure(
                "procurement.invalid-quantity-requested",
                "採購需求數量必須介於 1 到 999。");
        }

        if (targetPrice is { } target
            && (target.IsNegative || !Enum.IsDefined(target.Currency)))
        {
            return Result<PurchaseItemAggregate>.Failure(
                "procurement.invalid-target-price",
                "目標採購價不得為負數，且必須使用已知幣別。");
        }

        return new PurchaseItemAggregate(
            id,
            tenantId,
            campaignId,
            skuId,
            orderLineId,
            quantityRequested,
            targetPrice,
            createdAt);
    }

    public Result<PurchaseTransition> MarkPurchased(
        int quantityPurchased,
        MoneyPair actualPaid,
        DateTimeOffset decidedAt)
    {
        ArgumentNullException.ThrowIfNull(actualPaid);

        if (quantityPurchased < 1 || quantityPurchased > QuantityRequested)
        {
            return Result<PurchaseTransition>.Failure(
                "procurement.invalid-quantity-purchased",
                "實際買到數量必須介於 1 與需求數量之間。");
        }

        if (quantityPurchased != QuantityRequested)
        {
            return Result<PurchaseTransition>.Failure(
                "procurement.partial-purchase-not-supported",
                "部分買到仍缺少短缺數量的退款契約；目前只能記錄全數買到，避免遺漏退款。");
        }

        if (actualPaid.Original.IsNegative
            || actualPaid.Booking.IsNegative
            || !Enum.IsDefined(actualPaid.Original.Currency)
            || !Enum.IsDefined(actualPaid.Booking.Currency))
        {
            return Result<PurchaseTransition>.Failure(
                "procurement.invalid-actual-paid",
                "實付金額不得為負數，且必須使用已知幣別。");
        }

        if (actualPaid.Booking.Currency != Currency.TWD)
        {
            return Result<PurchaseTransition>.Failure(
                "procurement.booking-currency-must-be-twd",
                "記帳幣實付金額必須使用 TWD。");
        }

        if (Status == PurchaseItemStatus.Purchased)
        {
            return QuantityPurchased == quantityPurchased && ActualPaid == actualPaid
                ? PurchaseTransition.AlreadyRecorded
                : Result<PurchaseTransition>.Failure(
                    "procurement.purchase-already-recorded",
                    "這個採購品項已用不同內容記錄買到結果。");
        }

        if (Status == PurchaseItemStatus.Unavailable)
        {
            return Result<PurchaseTransition>.Failure(
                "procurement.purchase-item-unavailable",
                "缺貨品項不能再標記為買到。");
        }

        QuantityPurchased = quantityPurchased;
        ActualPaidOriginalAmountMinor = actualPaid.Original.AmountMinor;
        ActualPaidOriginalCurrency = actualPaid.Original.Currency;
        ActualPaidBookingAmountMinor = actualPaid.Booking.AmountMinor;
        ActualPaidBookingCurrency = actualPaid.Booking.Currency;
        ActualPaidFxSnapshotId = actualPaid.Fx;
        Status = PurchaseItemStatus.Purchased;
        DecidedAt = decidedAt;
        return PurchaseTransition.Recorded;
    }

    public PurchaseItem ToContract() =>
        new(
            Id,
            CampaignId,
            SkuId,
            OrderLineId,
            QuantityRequested,
            QuantityPurchased,
            TargetPrice,
            ActualPaid,
            Status,
            DecidedAt);
}

internal enum PurchaseTransition
{
    AlreadyRecorded = 0,
    Recorded = 1,
}

internal interface IProcurementRepository
{
    Task<PurchaseItemAggregate?> GetAsync(
        TenantId tenantId,
        PurchaseItemId id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseItemAggregate>> GetCampaignListAsync(
        TenantId tenantId,
        CampaignId campaignId,
        bool tracking,
        CancellationToken cancellationToken);

    void Add(PurchaseItemAggregate item);
}
