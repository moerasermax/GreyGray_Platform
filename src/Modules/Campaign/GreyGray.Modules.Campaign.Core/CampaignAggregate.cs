using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Campaign.Core;

internal sealed class CampaignAggregate
{
    private readonly List<CampaignOfferEntity> _offers = [];
    private readonly List<TripCostEntity> _tripCosts = [];

    private CampaignAggregate()
    {
    }

    private CampaignAggregate(
        CampaignId id,
        TenantId tenantId,
        CampaignDraftInput input,
        DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        Status = CampaignStatus.Draft;
        CreatedAt = now;
        ApplyDraft(input, now);
    }

    public CampaignId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Destination { get; private set; } = string.Empty;

    public DateOnly DepartAt { get; private set; }

    public DateOnly ReturnAt { get; private set; }

    public DateTimeOffset ClosesAt { get; private set; }

    /// <summary>現場漲價詢問的逾時分鐘數（ADR-027）。<c>null</c> 表示這個團沒指定，讀取端要自己套用技術預設值。</summary>
    public int? PriceInquiryTimeoutMinutes { get; private set; }

    public CampaignStatus Status { get; private set; }

    public string? Description { get; private set; }

    public string? CoverImageUrl { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<CampaignOfferEntity> Offers => _offers;

    public IReadOnlyCollection<TripCostEntity> TripCosts => _tripCosts;

    public Money TripCostTotal
    {
        get
        {
            if (_tripCosts.Count == 0)
            {
                return Money.Zero(Currency.TWD);
            }

            var currency = _tripCosts[0].Currency;
            var total = _tripCosts.Aggregate(
                0L,
                (current, cost) => checked(current + cost.AmountMinor));
            return new Money(total, currency);
        }
    }

    public static Result<CampaignAggregate> CreateDraft(
        CampaignId id,
        TenantId tenantId,
        CampaignDraftInput input,
        DateTimeOffset now)
    {
        var validation = ValidateDraft(input);
        return validation.IsFailure
            ? Result<CampaignAggregate>.Failure(validation.Error)
            : new CampaignAggregate(id, tenantId, Normalize(input), now);
    }

    public Result UpdateDraft(CampaignDraftInput input, DateTimeOffset now)
    {
        if (Status != CampaignStatus.Draft)
        {
            return Result.Failure(
                "campaign.cannot-edit-after-publish",
                "開團發布後不可再修改草稿內容。");
        }

        var validation = ValidateDraft(input);
        if (validation.IsFailure)
        {
            return validation;
        }

        ApplyDraft(Normalize(input), now);
        return Result.Success();
    }

    public Result<CampaignPublished> Publish(DateTimeOffset now)
    {
        if (Status != CampaignStatus.Draft)
        {
            return Result<CampaignPublished>.Failure(
                "campaign.invalid-transition",
                "只有草稿中的開團可以發布。");
        }

        if (_offers.All(offer => !offer.IsActive))
        {
            return Result<CampaignPublished>.Failure(
                "campaign.offer-required",
                "發布開團前至少需要一個可下單商品。");
        }

        if (ClosesAt <= now)
        {
            return Result<CampaignPublished>.Failure(
                "campaign.closes-at-passed",
                "截團時間必須晚於目前時間。");
        }

        Status = CampaignStatus.Open;
        UpdatedAt = now;
        return new CampaignPublished(
            Guid.CreateVersion7(),
            now,
            TenantId,
            Id,
            Destination,
            ClosesAt);
    }

    public Result<CampaignClosed> Close(DateTimeOffset now)
    {
        if (Status != CampaignStatus.Open)
        {
            return Result<CampaignClosed>.Failure(
                "campaign.invalid-transition",
                "只有收單中的開團可以截團。");
        }

        Status = CampaignStatus.Closed;
        UpdatedAt = now;
        return new CampaignClosed(Guid.CreateVersion7(), now, TenantId, Id);
    }

    public Result<CampaignCancelled> Cancel(string reason, DateTimeOffset now)
    {
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
        {
            return Result<CampaignCancelled>.Failure(
                "campaign.cancel-reason-required",
                "取消開團時必須填寫原因。");
        }

        if (normalizedReason.Length > 200)
        {
            return Result<CampaignCancelled>.Failure(
                "campaign.cancel-reason-too-long",
                "取消原因不得超過 200 個字元。");
        }

        if (Status is not (CampaignStatus.Open or CampaignStatus.Closed))
        {
            return Result<CampaignCancelled>.Failure(
                "campaign.invalid-transition",
                "只有收單中或已截團的開團可以取消。");
        }

        Status = CampaignStatus.Cancelled;
        UpdatedAt = now;
        return new CampaignCancelled(
            Guid.CreateVersion7(),
            now,
            TenantId,
            Id,
            normalizedReason);
    }

    public Result<CampaignSettled> Settle(bool allOrdersShipped, DateTimeOffset now)
    {
        if (Status != CampaignStatus.Returned)
        {
            return Result<CampaignSettled>.Failure(
                "campaign.invalid-transition",
                "只有已返國的開團可以結團。");
        }

        if (!allOrdersShipped)
        {
            return Result<CampaignSettled>.Failure(
                "campaign.orders-not-all-shipped",
                "該團仍有訂單尚未出貨，不能結團。");
        }

        Status = CampaignStatus.Settled;
        UpdatedAt = now;
        return new CampaignSettled(Guid.CreateVersion7(), now, TenantId, Id);
    }

    public Result<CampaignOfferEntity> AddOffer(
        CampaignOfferInput input,
        DateTimeOffset now)
    {
        if (Status != CampaignStatus.Draft)
        {
            return Result<CampaignOfferEntity>.Failure(
                "campaign.cannot-add-offer-after-publish",
                "開團發布後不可加入新的商品。");
        }

        if (input.SellingPrice.IsNegative)
        {
            return Result<CampaignOfferEntity>.Failure(
                "campaign.invalid-selling-price",
                "開團售價不得為負數。");
        }

        if (input.TargetPurchasePrice is { IsNegative: true })
        {
            return Result<CampaignOfferEntity>.Failure(
                "campaign.invalid-target-purchase-price",
                "目標採購價不得為負數。");
        }

        if (input.TargetPurchasePrice is { } target
            && target.Currency != input.SellingPrice.Currency)
        {
            return Result<CampaignOfferEntity>.Failure(
                "campaign.offer-currency-mismatch",
                "售價與目標採購價必須使用相同幣別。");
        }

        var existing = _offers.SingleOrDefault(offer => offer.SkuId == input.SkuId);
        if (existing is { IsActive: true })
        {
            return Result<CampaignOfferEntity>.Failure(
                "campaign.offer-already-exists",
                "這個 SKU 已在開團商品清單中。");
        }

        if (existing is not null)
        {
            existing.Reactivate(input.SellingPrice, input.TargetPurchasePrice);
            UpdatedAt = now;
            return existing;
        }

        var offer = CampaignOfferEntity.Create(
            CampaignOfferId.New(),
            Id,
            input.SkuId,
            input.SellingPrice,
            input.TargetPurchasePrice,
            now);
        _offers.Add(offer);
        UpdatedAt = now;
        return offer;
    }

    public Result RemoveOffer(CampaignOfferId offerId, bool hasOrders, DateTimeOffset now)
    {
        var offer = _offers.SingleOrDefault(candidate => candidate.Id == offerId);
        if (offer is null)
        {
            return Result.Failure("campaign.offer-not-found", "找不到指定的開團商品。");
        }

        if (hasOrders)
        {
            return Result.Failure(
                "campaign.offer-has-orders",
                "這個開團商品已有訂單，不能移除。");
        }

        offer.Deactivate();
        UpdatedAt = now;
        return Result.Success();
    }

    public Result<TripCostTransition> RecordTripCost(
        TripCostInput input,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Id.Value == Guid.Empty)
        {
            return Result<TripCostTransition>.Failure(
                "campaign.trip-cost-id-required",
                "旅程成本識別碼不可為空。");
        }

        if (!Enum.IsDefined(input.Kind))
        {
            return Result<TripCostTransition>.Failure(
                "campaign.trip-cost-kind-invalid",
                "旅程成本科目不正確。");
        }

        if (input.Amount.IsNegative || !Enum.IsDefined(input.Amount.Currency))
        {
            return Result<TripCostTransition>.Failure(
                "campaign.trip-cost-amount-invalid",
                "旅程成本不得為負數，且必須使用已知幣別。");
        }

        var memo = string.IsNullOrWhiteSpace(input.Memo) ? string.Empty : input.Memo.Trim();
        if (memo.Length > 200)
        {
            return Result<TripCostTransition>.Failure(
                "campaign.trip-cost-memo-too-long",
                "旅程成本備註不得超過 200 個字元。");
        }

        var existing = _tripCosts.SingleOrDefault(cost => cost.Id == input.Id);
        if (existing is not null)
        {
            return existing.Kind == input.Kind
                && existing.Amount == input.Amount
                && StringComparer.Ordinal.Equals(existing.Memo, memo)
                    ? TripCostTransition.AlreadyRecorded
                    : Result<TripCostTransition>.Failure(
                        "campaign.trip-cost-already-recorded",
                        "這筆旅程成本已用不同內容登錄。");
        }

        if (_tripCosts.FirstOrDefault() is { } first
            && first.Currency != input.Amount.Currency)
        {
            return Result<TripCostTransition>.Failure(
                "campaign.trip-cost-currency-mismatch",
                "同一個開團的旅程成本必須使用相同幣別。");
        }

        _tripCosts.Add(TripCostEntity.Create(
            input.Id,
            TenantId,
            Id,
            input.Kind,
            input.Amount,
            memo,
            recordedAt));
        UpdatedAt = recordedAt;
        return TripCostTransition.Recorded;
    }

    public bool IsAcceptingOrders(DateTimeOffset now) =>
        Status == CampaignStatus.Open && now < ClosesAt;

    public CampaignSummary ToSummary() =>
        new(Id, Title, Destination, DepartAt, ReturnAt, ClosesAt, Status)
        {
            TripCostTotal = TripCostTotal,
            PriceInquiryTimeout = PriceInquiryTimeoutMinutes is { } minutes
                ? TimeSpan.FromMinutes(minutes)
                : null,
        };

    private static Result ValidateDraft(CampaignDraftInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            return Result.Failure("campaign.title-required", "請輸入開團標題。");
        }

        if (input.Title.Trim().Length > 100)
        {
            return Result.Failure("campaign.title-too-long", "開團標題不得超過 100 個字元。");
        }

        if (string.IsNullOrWhiteSpace(input.Destination))
        {
            return Result.Failure("campaign.destination-required", "請輸入目的地。");
        }

        if (input.Destination.Trim().Length > 50)
        {
            return Result.Failure("campaign.destination-too-long", "目的地不得超過 50 個字元。");
        }

        if (input.ReturnAt < input.DepartAt)
        {
            return Result.Failure(
                "campaign.invalid-travel-dates",
                "返國日不可早於出發日。");
        }

        if (input.PriceInquiryTimeoutMinutes is { } timeoutMinutes && timeoutMinutes <= 0)
        {
            return Result.Failure(
                "campaign.invalid-price-inquiry-timeout",
                "現場漲價詢問的逾時分鐘數必須大於 0。");
        }

        return Result.Success();
    }

    private static CampaignDraftInput Normalize(CampaignDraftInput input) =>
        input with
        {
            Title = input.Title.Trim(),
            Destination = input.Destination.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            CoverImageUrl = string.IsNullOrWhiteSpace(input.CoverImageUrl) ? null : input.CoverImageUrl.Trim(),
        };

    private void ApplyDraft(CampaignDraftInput input, DateTimeOffset now)
    {
        Title = input.Title;
        Destination = input.Destination;
        DepartAt = input.DepartAt;
        ReturnAt = input.ReturnAt;
        ClosesAt = input.ClosesAt;
        PriceInquiryTimeoutMinutes = input.PriceInquiryTimeoutMinutes;
        Description = input.Description;
        CoverImageUrl = input.CoverImageUrl;
        UpdatedAt = now;
    }
}

internal enum TripCostTransition
{
    AlreadyRecorded = 0,
    Recorded = 1,
}

internal sealed class TripCostEntity
{
    private TripCostEntity()
    {
    }

    private TripCostEntity(
        TripCostId id,
        TenantId tenantId,
        CampaignId campaignId,
        TripCostKind kind,
        Money amount,
        string memo,
        DateTimeOffset recordedAt)
    {
        Id = id;
        TenantId = tenantId;
        CampaignId = campaignId;
        Kind = kind;
        AmountMinor = amount.AmountMinor;
        Currency = amount.Currency;
        Memo = memo;
        RecordedAt = recordedAt;
    }

    public TripCostId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public CampaignId CampaignId { get; private set; }

    public TripCostKind Kind { get; private set; }

    public long AmountMinor { get; private set; }

    public Currency Currency { get; private set; }

    public string Memo { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; private set; }

    public Money Amount => new(AmountMinor, Currency);

    public static TripCostEntity Create(
        TripCostId id,
        TenantId tenantId,
        CampaignId campaignId,
        TripCostKind kind,
        Money amount,
        string memo,
        DateTimeOffset recordedAt) =>
        new(id, tenantId, campaignId, kind, amount, memo, recordedAt);
}

internal sealed class CampaignOfferEntity
{
    private CampaignOfferEntity()
    {
    }

    private CampaignOfferEntity(
        CampaignOfferId id,
        CampaignId campaignId,
        SkuId skuId,
        Money sellingPrice,
        Money? targetPurchasePrice,
        DateTimeOffset createdAt)
    {
        Id = id;
        CampaignId = campaignId;
        SkuId = skuId;
        SellingPriceAmountMinor = sellingPrice.AmountMinor;
        SellingPriceCurrency = sellingPrice.Currency;
        TargetPurchasePriceAmountMinor = targetPurchasePrice?.AmountMinor;
        TargetPurchasePriceCurrency = targetPurchasePrice?.Currency;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public CampaignOfferId Id { get; private set; }

    public CampaignId CampaignId { get; private set; }

    public SkuId SkuId { get; private set; }

    public long SellingPriceAmountMinor { get; private set; }

    public Currency SellingPriceCurrency { get; private set; }

    public long? TargetPurchasePriceAmountMinor { get; private set; }

    public Currency? TargetPurchasePriceCurrency { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Money SellingPrice => new(SellingPriceAmountMinor, SellingPriceCurrency);

    public Money? TargetPurchasePrice => TargetPurchasePriceAmountMinor is { } amount
        && TargetPurchasePriceCurrency is { } currency
            ? new Money(amount, currency)
            : null;

    public static CampaignOfferEntity Create(
        CampaignOfferId id,
        CampaignId campaignId,
        SkuId skuId,
        Money sellingPrice,
        Money? targetPurchasePrice,
        DateTimeOffset createdAt) =>
        new(id, campaignId, skuId, sellingPrice, targetPurchasePrice, createdAt);

    public void Deactivate() => IsActive = false;

    public void Reactivate(Money sellingPrice, Money? targetPurchasePrice)
    {
        SellingPriceAmountMinor = sellingPrice.AmountMinor;
        SellingPriceCurrency = sellingPrice.Currency;
        TargetPurchasePriceAmountMinor = targetPurchasePrice?.AmountMinor;
        TargetPurchasePriceCurrency = targetPurchasePrice?.Currency;
        IsActive = true;
    }

    public CampaignOffer ToContract() =>
        new(Id, CampaignId, SkuId, SellingPrice, TargetPurchasePrice, IsActive);
}
