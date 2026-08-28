using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Procurement.Core;

internal sealed class ProcurementApplicationService(
    IProcurementRepository purchaseItems,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    IOrderQuery orders,
    ICampaignQuery campaigns,
    IClock clock,
    ICorrelationContext correlationContext) : IProcurementApplication, IProcurementQuery
{
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
            return item.ToContract();
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
        return item.ToContract();
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
        return items.Select(item => item.ToContract()).ToArray();
    }

    public Task<Result<Inquiry>> GetInquiryAsync(
        InquiryId id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result<Inquiry>.Failure(
            "procurement.inquiry-not-found",
            "詢價功能將於 M1b-2 啟用。"));
    }

    private static bool IsEligibleOrder(OrderView order) =>
        order.Status is OrderStatus.PaidAwaitingClose or OrderStatus.ClosedAwaitingDeparture;
}
