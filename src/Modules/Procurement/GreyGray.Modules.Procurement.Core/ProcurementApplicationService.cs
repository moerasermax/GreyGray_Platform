using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Abstractions.Saga;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Procurement.Core;

internal sealed class ProcurementApplicationService(
    IProcurementRepository purchaseItems,
    IInquiryRepository inquiries,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    ISagaTimerScheduler timerScheduler,
    IOrderQuery orders,
    ICampaignQuery campaigns,
    IClock clock,
    ICorrelationContext correlationContext)
    : IProcurementApplication,
        IProcurementGoodsReceipt,
        IProcurementCompensation,
        IInquiryReplyReceiver,
        IProcurementQuery
{
    /// <summary>
    /// 現場漲價詢問的逾時期限的技術預設值。ADR-027 已拍板改成每團可設，
    /// 這個常數只在該團的 <see cref="CampaignSummary.PriceInquiryTimeout"/> 是 <c>null</c> 時使用。
    /// </summary>
    private static readonly TimeSpan PriceInquiryTimeout = TimeSpan.FromHours(2);

    internal const string PriceInquirySagaType = "procurement.price-inquiry";

    public async Task<Result<int>> BuildCampaignListAsync(
        CampaignClosed campaignClosed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(campaignClosed);

        var orderResult = await orders.GetByCampaignAsync(
            campaignClosed.CampaignId,
            cancellationToken);
        if (orderResult.IsFailure)
        {
            return Result<int>.Failure(orderResult.Error);
        }

        var existing = await purchaseItems.GetCampaignListAsync(
            campaignClosed.TenantId,
            campaignClosed.CampaignId,
            tracking: true,
            cancellationToken);
        var existingLineIds = existing.Select(item => item.OrderLineId).ToHashSet();

        var candidates = orderResult.Value
            .Where(IsEligibleOrder)
            .SelectMany(order => order.Lines)
            .Where(line => line.Mode == FulfillmentMode.Preorder
                && line.CampaignId == campaignClosed.CampaignId
                && line.Status == OrderLineStatus.Pending
                && !existingLineIds.Contains(line.Id))
            .ToArray();

        var prepared = new List<PurchaseItemAggregate>(candidates.Length);
        foreach (var line in candidates)
        {
            if (line.CampaignOfferId is null)
            {
                return Result<int>.Failure(
                    "procurement.campaign-offer-required",
                    "預購訂單品項缺少開團商品識別，不能建立採購清單。");
            }

            var offer = await campaigns.GetOfferAsync(
                line.CampaignOfferId.Value,
                cancellationToken);
            if (offer.IsFailure)
            {
                return Result<int>.Failure(offer.Error);
            }

            if (offer.Value.CampaignId != campaignClosed.CampaignId
                || offer.Value.SkuId != line.SkuId)
            {
                return Result<int>.Failure(
                    "procurement.campaign-offer-mismatch",
                    "訂單品項與開團商品快照不一致，不能建立採購清單。");
            }

            var created = PurchaseItemAggregate.Create(
                PurchaseItemId.New(),
                campaignClosed.TenantId,
                campaignClosed.CampaignId,
                line.SkuId,
                line.Id,
                line.Quantity,
                offer.Value.TargetPurchasePrice,
                campaignClosed.OccurredAt);
            if (created.IsFailure)
            {
                return Result<int>.Failure(created.Error);
            }

            prepared.Add(created.Value);
        }

        foreach (var item in prepared)
        {
            purchaseItems.Add(item);
        }

        if (prepared.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return prepared.Count;
    }

    public async Task<Result<PurchaseItem>> MarkPurchasedAsync(
        PurchaseItemId id,
        int quantityPurchased,
        MoneyPair actualPaid,
        CancellationToken cancellationToken)
    {
        var item = await purchaseItems.GetAsync(
            correlationContext.TenantId,
            id,
            cancellationToken);
        if (item is null)
        {
            return Result<PurchaseItem>.Failure(
                "procurement.purchase-item-not-found",
                "找不到指定的採購品項。");
        }

        var transitioned = item.MarkPurchased(quantityPurchased, actualPaid, clock.UtcNow);
        if (transitioned.IsFailure)
        {
            return Result<PurchaseItem>.Failure(transitioned.Error);
        }

        if (transitioned.Value == PurchaseTransition.AlreadyRecorded)
        {
            return await ToContractAsync(item, cancellationToken);
        }

        await eventPublisher.PublishAsync(
            new ItemPurchased(
                Guid.CreateVersion7(),
                item.DecidedAt!.Value,
                item.TenantId,
                item.Id,
                item.CampaignId,
                item.SkuId,
                item.OrderLineId,
                item.QuantityPurchased,
                actualPaid),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToContractAsync(item, cancellationToken);
    }

    public async Task<Result<PurchaseItem>> MarkReceivedAsync(
        PurchaseItemId id,
        CancellationToken cancellationToken)
    {
        var item = await purchaseItems.GetAsync(
            correlationContext.TenantId,
            id,
            cancellationToken);
        if (item is null)
        {
            return Result<PurchaseItem>.Failure(
                "procurement.purchase-item-not-found",
                "找不到指定的採購品項。");
        }

        var transitioned = item.MarkReceived(clock.UtcNow);
        if (transitioned.IsFailure)
        {
            return Result<PurchaseItem>.Failure(transitioned.Error);
        }

        if (transitioned.Value == ReceiptTransition.AlreadyRecorded)
        {
            return await ToContractAsync(item, cancellationToken);
        }

        var actualPaid = item.ActualPaid;
        if (actualPaid is null)
        {
            return Result<PurchaseItem>.Failure(
                "procurement.actual-paid-required",
                "採購品項缺少實付成本，不能標記為帶回入庫。");
        }

        await eventPublisher.PublishAsync(
            new GoodsReceived(
                Guid.CreateVersion7(),
                item.ReceivedAt!.Value,
                item.TenantId,
                item.CampaignId,
                item.SkuId,
                item.QuantityPurchased,
                actualPaid.Booking,
                LotSource.OverseasPurchase,
                item.OrderLineId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToContractAsync(item, cancellationToken);
    }

    public async Task<Result<PurchaseItem>> MarkUnavailableAsync(
        PurchaseItemId id,
        string reason,
        CancellationToken cancellationToken)
    {
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 200)
        {
            return Result<PurchaseItem>.Failure(
                "procurement.unavailable-reason-invalid",
                "缺貨原因必須是 1 到 200 個字元。");
        }

        var item = await purchaseItems.GetAsync(
            correlationContext.TenantId,
            id,
            cancellationToken);
        if (item is null)
        {
            return Result<PurchaseItem>.Failure(
                "procurement.purchase-item-not-found",
                "找不到指定的採購品項。");
        }

        var transitioned = item.MarkUnavailable(clock.UtcNow);
        if (transitioned.IsFailure)
        {
            return Result<PurchaseItem>.Failure(transitioned.Error);
        }

        if (transitioned.Value == UnavailableTransition.AlreadyRecorded)
        {
            return await ToContractAsync(item, cancellationToken);
        }

        await eventPublisher.PublishAsync(
            new ItemUnavailable(
                Guid.CreateVersion7(),
                item.DecidedAt!.Value,
                item.TenantId,
                item.Id,
                item.CampaignId,
                item.OrderLineId,
                normalizedReason),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToContractAsync(item, cancellationToken);
    }

    public async Task<Result<Inquiry>> ReportPriceChangedAsync(
        PurchaseItemId id,
        Money newPrice,
        CancellationToken cancellationToken)
    {
        if (newPrice.IsNegative || !Enum.IsDefined(newPrice.Currency))
        {
            return Result<Inquiry>.Failure(
                "procurement.invalid-new-price",
                "回報的新價格不得為負數，且必須使用已知幣別。");
        }

        var item = await purchaseItems.GetAsync(
            correlationContext.TenantId,
            id,
            cancellationToken);
        if (item is null)
        {
            return Result<Inquiry>.Failure(
                "procurement.purchase-item-not-found",
                "找不到指定的採購品項。");
        }

        if (item.TargetPrice is not { } originalPrice)
        {
            return Result<Inquiry>.Failure(
                "procurement.target-price-required",
                "沒有登記目標採購價的品項無法回報現場漲價。");
        }

        if (newPrice.Currency != originalPrice.Currency)
        {
            return Result<Inquiry>.Failure(
                "procurement.price-currency-mismatch",
                "回報的新價格幣別必須與目標採購價一致。");
        }

        var started = item.ReportPriceChanged();
        if (started.IsFailure)
        {
            return Result<Inquiry>.Failure(started.Error);
        }

        var campaign = await campaigns.GetAsync(item.CampaignId, cancellationToken);
        if (campaign.IsFailure)
        {
            return Result<Inquiry>.Failure(campaign.Error);
        }

        var occurredAt = clock.UtcNow;
        var timeoutAt = occurredAt + (campaign.Value.PriceInquiryTimeout ?? PriceInquiryTimeout);
        var inquiry = InquiryAggregate.Open(
            InquiryId.New(),
            item.TenantId,
            item.Id,
            originalPrice,
            newPrice,
            occurredAt,
            timeoutAt);
        inquiries.Add(inquiry);

        await timerScheduler.ScheduleAsync(
            PriceInquirySagaType,
            inquiry.Id.ToString(),
            timeoutAt,
            "{}",
            item.TenantId,
            cancellationToken);

        await eventPublisher.PublishAsync(
            new ItemPriceChanged(
                Guid.CreateVersion7(),
                occurredAt,
                item.TenantId,
                item.Id,
                inquiry.Id,
                item.CampaignId,
                item.OrderLineId,
                item.SkuId,
                originalPrice,
                newPrice,
                timeoutAt),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return inquiry.ToContract();
    }

    public async Task<Result> ReplyAsync(
        InquiryId inquiryId,
        bool accepted,
        string? replyText,
        CancellationToken cancellationToken)
    {
        var normalizedReplyText = string.IsNullOrWhiteSpace(replyText) ? null : replyText.Trim();
        if (normalizedReplyText is { Length: > 200 })
        {
            return Result.Failure(
                "procurement.inquiry-reply-text-invalid",
                "回覆內容不得超過 200 個字元。");
        }

        var inquiry = await inquiries.GetAsync(correlationContext.TenantId, inquiryId, cancellationToken);
        if (inquiry is null)
        {
            return Result.Failure("procurement.inquiry-not-found", "找不到指定的詢價。");
        }

        var outcome = accepted ? InquiryOutcome.ConfirmedByCustomer : InquiryOutcome.DeclinedByCustomer;
        var resolved = await ResolveInquiryAsync(inquiry, outcome, normalizedReplyText, cancellationToken);
        return resolved;
    }

    /// <summary>Saga timer 到期時呼叫；「逾時視為照買」。</summary>
    internal async Task ResolveInquiryTimeoutAsync(
        InquiryId inquiryId,
        CancellationToken cancellationToken)
    {
        var inquiry = await inquiries.GetAsync(correlationContext.TenantId, inquiryId, cancellationToken);
        if (inquiry is null)
        {
            return;
        }

        await ResolveInquiryAsync(
            inquiry,
            InquiryOutcome.AutoApprovedOnTimeout,
            null,
            cancellationToken);
    }

    private async Task<Result> ResolveInquiryAsync(
        InquiryAggregate inquiry,
        InquiryOutcome outcome,
        string? replyText,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;
        var resolved = inquiry.Resolve(outcome, replyText, occurredAt);
        if (resolved == InquiryResolutionTransition.AlreadyResolved)
        {
            return Result.Success();
        }

        var item = await purchaseItems.GetAsync(inquiry.TenantId, inquiry.PurchaseItemId, cancellationToken);
        item?.ResolvePriceChange();

        await eventPublisher.PublishAsync(
            new InquiryResolved(
                Guid.CreateVersion7(),
                occurredAt,
                inquiry.TenantId,
                inquiry.Id,
                inquiry.PurchaseItemId,
                outcome),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PurchaseItem>>> GetCampaignListAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken)
    {
        var items = await purchaseItems.GetCampaignListAsync(
            correlationContext.TenantId,
            campaignId,
            tracking: false,
            cancellationToken);
        if (items.Count == 0)
        {
            return Array.Empty<PurchaseItem>();
        }

        var latestInquiries = await inquiries.GetLatestByPurchaseItemsAsync(
            correlationContext.TenantId,
            items.Select(item => item.Id).ToArray(),
            cancellationToken);
        return items
            .Select(item => item.ToContract() with
            {
                Inquiry = latestInquiries.TryGetValue(item.Id, out var inquiry)
                    ? inquiry.ToContract()
                    : null,
            })
            .ToArray();
    }

    public async Task<Result<Inquiry>> GetInquiryAsync(
        InquiryId id,
        CancellationToken cancellationToken)
    {
        var inquiry = await inquiries.GetAsync(correlationContext.TenantId, id, cancellationToken);
        return inquiry is null
            ? Result<Inquiry>.Failure("procurement.inquiry-not-found", "找不到指定的詢價。")
            : inquiry.ToContract();
    }

    private async Task<PurchaseItem> ToContractAsync(
        PurchaseItemAggregate item,
        CancellationToken cancellationToken)
    {
        var latest = await inquiries.GetLatestByPurchaseItemsAsync(
            item.TenantId,
            [item.Id],
            cancellationToken);
        return item.ToContract() with
        {
            Inquiry = latest.TryGetValue(item.Id, out var inquiry) ? inquiry.ToContract() : null,
        };
    }

    private static bool IsEligibleOrder(OrderView order) =>
        order.Status is OrderStatus.PaidAwaitingClose or OrderStatus.ClosedAwaitingDeparture;
}
