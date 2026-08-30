using System.Globalization;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Campaign.Core;

internal sealed record CampaignPageSlice(
    IReadOnlyList<CampaignAggregate> Items,
    string? NextCursor);

internal interface ICampaignRepository
{
    Task<CampaignAggregate?> GetAsync(
        CampaignId id,
        TenantId tenantId,
        CancellationToken cancellationToken);

    Task<CampaignOfferEntity?> GetOfferAsync(
        CampaignOfferId id,
        TenantId tenantId,
        CancellationToken cancellationToken);

    Task<CampaignPageSlice> ListAsync(
        TenantId tenantId,
        CampaignStatus? status,
        long? beforeUtcTicks,
        int limit,
        CancellationToken cancellationToken);

    void Add(CampaignAggregate campaign);
}

internal sealed class CampaignService(
    ICampaignRepository campaigns,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    ICatalogQuery catalogQuery,
    ICampaignOrderQuery orderQuery,
    IClock clock,
    ICorrelationContext correlationContext)
    : ICampaignQuery, ICampaignStorefront, ICampaignAdministration, ICampaignTripCostAdministration
{
    public async Task<Result<CampaignSummary>> GetAsync(
        CampaignId id,
        CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetAsync(
            id,
            correlationContext.TenantId,
            cancellationToken);
        return campaign is null
            ? Result<CampaignSummary>.Failure("campaign.not-found", "找不到指定的開團。")
            : campaign.ToSummary();
    }

    public async Task<Result<CampaignOffer>> GetOfferAsync(
        CampaignOfferId id,
        CancellationToken cancellationToken)
    {
        var offer = await campaigns.GetOfferAsync(
            id,
            correlationContext.TenantId,
            cancellationToken);
        return offer is null
            ? Result<CampaignOffer>.Failure("campaign.offer-not-found", "找不到指定的開團商品。")
            : offer.ToContract();
    }

    public async Task<Result<bool>> IsAcceptingOrdersAsync(
        CampaignId id,
        CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetAsync(
            id,
            correlationContext.TenantId,
            cancellationToken);
        return campaign is null
            ? Result<bool>.Failure("campaign.not-found", "找不到指定的開團。")
            : campaign.IsAcceptingOrders(clock.UtcNow);
    }

    async Task<Result<CampaignPage<StorefrontCampaignListItem>>> ICampaignStorefront.ListAsync(
        CampaignPageRequest request,
        CancellationToken cancellationToken)
    {
        var pageRequest = ValidatePage(request);
        if (pageRequest.IsFailure)
        {
            return Result<CampaignPage<StorefrontCampaignListItem>>.Failure(pageRequest.Error);
        }

        var (before, limit) = pageRequest.Value;
        var slice = await campaigns.ListAsync(
            correlationContext.TenantId,
            request.Status ?? CampaignStatus.Open,
            before,
            limit,
            cancellationToken);
        var now = clock.UtcNow;
        return new CampaignPage<StorefrontCampaignListItem>(
            slice.Items.Select(campaign => ToStorefrontListItem(campaign, now)).ToArray(),
            slice.NextCursor);
    }

    public async Task<Result<StorefrontCampaignDetail>> GetDetailAsync(
        CampaignId id,
        CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetAsync(
            id,
            correlationContext.TenantId,
            cancellationToken);
        if (campaign is null)
        {
            return Result<StorefrontCampaignDetail>.Failure(
                "campaign.not-found",
                "找不到指定的開團。");
        }

        var offers = new List<StorefrontCampaignOffer>();
        foreach (var offer in campaign.Offers.Where(candidate => candidate.IsActive))
        {
            var skuResult = await catalogQuery.GetSkuAsync(offer.SkuId, cancellationToken);
            if (skuResult.IsFailure)
            {
                return Result<StorefrontCampaignDetail>.Failure(skuResult.Error);
            }

            var sku = skuResult.Value;
            offers.Add(new StorefrontCampaignOffer(
                offer.Id,
                offer.SkuId,
                sku.ProductId,
                sku.Name,
                sku.VariantName,
                null,
                offer.SellingPrice,
                null,
                offer.IsActive));
        }

        return new StorefrontCampaignDetail(
            ToStorefrontListItem(campaign, clock.UtcNow),
            campaign.Description,
            offers);
    }

    async Task<Result<CampaignPage<AdminCampaignView>>> ICampaignAdministration.ListAsync(
        CampaignPageRequest request,
        CancellationToken cancellationToken)
    {
        var pageRequest = ValidatePage(request);
        if (pageRequest.IsFailure)
        {
            return Result<CampaignPage<AdminCampaignView>>.Failure(pageRequest.Error);
        }

        var (before, limit) = pageRequest.Value;
        var slice = await campaigns.ListAsync(
            correlationContext.TenantId,
            request.Status,
            before,
            limit,
            cancellationToken);
        var items = new List<AdminCampaignView>(slice.Items.Count);
        foreach (var campaign in slice.Items)
        {
            var view = await ToAdminViewAsync(campaign, cancellationToken);
            if (view.IsFailure)
            {
                return Result<CampaignPage<AdminCampaignView>>.Failure(view.Error);
            }

            items.Add(view.Value);
        }

        return new CampaignPage<AdminCampaignView>(items, slice.NextCursor);
    }

    async Task<Result<AdminCampaignDetail>> ICampaignAdministration.GetDetailAsync(
        CampaignId id,
        CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetAsync(
            id,
            correlationContext.TenantId,
            cancellationToken);
        if (campaign is null)
        {
            return Result<AdminCampaignDetail>.Failure("campaign.not-found", "找不到指定的開團。");
        }

        var campaignView = await ToAdminViewAsync(campaign, cancellationToken);
        if (campaignView.IsFailure)
        {
            return Result<AdminCampaignDetail>.Failure(campaignView.Error);
        }

        var offers = await ToAdminOffersAsync(campaign, cancellationToken);
        return offers.IsFailure
            ? Result<AdminCampaignDetail>.Failure(offers.Error)
            : new AdminCampaignDetail(campaignView.Value, offers.Value);
    }

    public async Task<Result<AdminCampaignView>> CreateDraftAsync(
        CampaignDraftInput input,
        CancellationToken cancellationToken)
    {
        var created = CampaignAggregate.CreateDraft(
            CampaignId.New(),
            correlationContext.TenantId,
            input,
            clock.UtcNow);
        if (created.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(created.Error);
        }

        campaigns.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToAdminView(created.Value, 0);
    }

    public async Task<Result<AdminCampaignView>> UpdateDraftAsync(
        CampaignId id,
        CampaignDraftInput input,
        CancellationToken cancellationToken)
    {
        var campaignResult = await FindAsync(id, cancellationToken);
        if (campaignResult.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(campaignResult.Error);
        }

        var updated = campaignResult.Value.UpdateDraft(input, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(updated.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToAdminViewAsync(campaignResult.Value, cancellationToken);
    }

    public async Task<Result<AdminCampaignView>> PublishAsync(
        CampaignId id,
        CancellationToken cancellationToken) =>
        await TransitionAsync(
            id,
            campaign => campaign.Publish(clock.UtcNow),
            cancellationToken);

    public async Task<Result<AdminCampaignView>> CloseAsync(
        CampaignId id,
        CancellationToken cancellationToken) =>
        await TransitionAsync(
            id,
            campaign => campaign.Close(clock.UtcNow),
            cancellationToken);

    public async Task<Result<AdminCampaignView>> CancelAsync(
        CampaignId id,
        string reason,
        CancellationToken cancellationToken) =>
        await TransitionAsync(
            id,
            campaign => campaign.Cancel(reason, clock.UtcNow),
            cancellationToken);

    public async Task<Result<AdminCampaignView>> SettleAsync(
        CampaignId id,
        CancellationToken cancellationToken)
    {
        var campaignResult = await FindAsync(id, cancellationToken);
        if (campaignResult.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(campaignResult.Error);
        }

        var orderResult = await orderQuery.GetAsync(id, cancellationToken);
        if (orderResult.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(orderResult.Error);
        }

        var transition = campaignResult.Value.Settle(
            orderResult.Value.AllOrdersShipped,
            clock.UtcNow);
        if (transition.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(transition.Error);
        }

        await eventPublisher.PublishAsync(transition.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToAdminView(campaignResult.Value, orderResult.Value.OrderCount);
    }

    public async Task<Result<IReadOnlyList<AdminCampaignOfferView>>> GetOffersAsync(
        CampaignId id,
        CancellationToken cancellationToken)
    {
        var campaignResult = await FindAsync(id, cancellationToken);
        return campaignResult.IsFailure
            ? Result<IReadOnlyList<AdminCampaignOfferView>>.Failure(campaignResult.Error)
            : await ToAdminOffersAsync(campaignResult.Value, cancellationToken);
    }

    public async Task<Result<AdminCampaignOfferView>> AddOfferAsync(
        CampaignId id,
        CampaignOfferInput input,
        CancellationToken cancellationToken)
    {
        var campaignResult = await FindAsync(id, cancellationToken);
        if (campaignResult.IsFailure)
        {
            return Result<AdminCampaignOfferView>.Failure(campaignResult.Error);
        }

        var skuResult = await catalogQuery.GetSkuAsync(input.SkuId, cancellationToken);
        if (skuResult.IsFailure)
        {
            return Result<AdminCampaignOfferView>.Failure(skuResult.Error);
        }

        if (!skuResult.Value.IsActive)
        {
            return Result<AdminCampaignOfferView>.Failure(
                "catalog.sku-archived",
                "已封存的 SKU 不可加入開團。");
        }

        var added = campaignResult.Value.AddOffer(input, clock.UtcNow);
        if (added.IsFailure)
        {
            return Result<AdminCampaignOfferView>.Failure(added.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AdminCampaignOfferView(
            added.Value.Id,
            added.Value.SkuId,
            skuResult.Value.Name,
            skuResult.Value.VariantName,
            added.Value.SellingPrice,
            added.Value.TargetPurchasePrice,
            added.Value.IsActive,
            0);
    }

    public async Task<Result> RemoveOfferAsync(
        CampaignId campaignId,
        CampaignOfferId offerId,
        CancellationToken cancellationToken)
    {
        var campaignResult = await FindAsync(campaignId, cancellationToken);
        if (campaignResult.IsFailure)
        {
            return Result.Failure(campaignResult.Error);
        }

        var offer = campaignResult.Value.Offers.SingleOrDefault(candidate => candidate.Id == offerId);
        if (offer is null)
        {
            return Result.Failure("campaign.offer-not-found", "找不到指定的開團商品。");
        }

        var ordersResult = await orderQuery.GetAsync(campaignId, cancellationToken);
        if (ordersResult.IsFailure)
        {
            return Result.Failure(ordersResult.Error);
        }

        var hasOrders = ordersResult.Value.OrderedQuantityBySku
            .GetValueOrDefault(offer.SkuId) > 0;
        var removed = campaignResult.Value.RemoveOffer(offerId, hasOrders, clock.UtcNow);
        if (removed.IsFailure)
        {
            return removed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<AdminCampaignView>> RecordTripCostAsync(
        CampaignId campaignId,
        TripCostInput input,
        CancellationToken cancellationToken)
    {
        var campaignResult = await FindAsync(campaignId, cancellationToken);
        if (campaignResult.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(campaignResult.Error);
        }

        var campaign = campaignResult.Value;
        var recorded = campaign.RecordTripCost(input, clock.UtcNow);
        if (recorded.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(recorded.Error);
        }

        if (recorded.Value == TripCostTransition.Recorded)
        {
            var tripCost = campaign.TripCosts.Single(cost => cost.Id == input.Id);
            await eventPublisher.PublishAsync(
                new TripCostRecorded(
                    Guid.CreateVersion7(),
                    tripCost.RecordedAt,
                    campaign.TenantId,
                    campaign.Id,
                    tripCost.Id,
                    tripCost.Kind,
                    tripCost.Amount,
                    tripCost.Memo),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await ToAdminViewAsync(campaign, cancellationToken);
    }

    private async Task<Result<AdminCampaignView>> TransitionAsync<TEvent>(
        CampaignId id,
        Func<CampaignAggregate, Result<TEvent>> transition,
        CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var campaignResult = await FindAsync(id, cancellationToken);
        if (campaignResult.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(campaignResult.Error);
        }

        var eventResult = transition(campaignResult.Value);
        if (eventResult.IsFailure)
        {
            return Result<AdminCampaignView>.Failure(eventResult.Error);
        }

        await eventPublisher.PublishAsync(eventResult.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToAdminViewAsync(campaignResult.Value, cancellationToken);
    }

    private async Task<Result<CampaignAggregate>> FindAsync(
        CampaignId id,
        CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetAsync(
            id,
            correlationContext.TenantId,
            cancellationToken);
        return campaign is null
            ? Result<CampaignAggregate>.Failure("campaign.not-found", "找不到指定的開團。")
            : campaign;
    }

    private async Task<Result<AdminCampaignView>> ToAdminViewAsync(
        CampaignAggregate campaign,
        CancellationToken cancellationToken)
    {
        var orders = await orderQuery.GetAsync(campaign.Id, cancellationToken);
        return orders.IsFailure
            ? Result<AdminCampaignView>.Failure(orders.Error)
            : ToAdminView(campaign, orders.Value.OrderCount);
    }

    private async Task<Result<IReadOnlyList<AdminCampaignOfferView>>> ToAdminOffersAsync(
        CampaignAggregate campaign,
        CancellationToken cancellationToken)
    {
        var ordersResult = await orderQuery.GetAsync(campaign.Id, cancellationToken);
        if (ordersResult.IsFailure)
        {
            return Result<IReadOnlyList<AdminCampaignOfferView>>.Failure(ordersResult.Error);
        }

        var orderedBySku = ordersResult.Value.OrderedQuantityBySku;
        var result = new List<AdminCampaignOfferView>(campaign.Offers.Count);
        foreach (var offer in campaign.Offers)
        {
            var skuResult = await catalogQuery.GetSkuAsync(offer.SkuId, cancellationToken);
            if (skuResult.IsFailure)
            {
                return Result<IReadOnlyList<AdminCampaignOfferView>>.Failure(skuResult.Error);
            }

            result.Add(new AdminCampaignOfferView(
                offer.Id,
                offer.SkuId,
                skuResult.Value.Name,
                skuResult.Value.VariantName,
                offer.SellingPrice,
                offer.TargetPurchasePrice,
                offer.IsActive,
                orderedBySku.GetValueOrDefault(offer.SkuId)));
        }

        return result;
    }

    private static Result<(long? BeforeUtcTicks, int Limit)> ValidatePage(CampaignPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > 100)
        {
            return Result<(long?, int)>.Failure(
                "campaign.invalid-page-limit",
                "每頁筆數必須介於 1 到 100 之間。");
        }

        if (string.IsNullOrWhiteSpace(request.Cursor))
        {
            return (null, request.Limit);
        }

        return long.TryParse(
            request.Cursor,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var ticks)
            ? (ticks, request.Limit)
            : Result<(long?, int)>.Failure("campaign.invalid-cursor", "分頁游標格式不正確。");
    }

    private static StorefrontCampaignListItem ToStorefrontListItem(
        CampaignAggregate campaign,
        DateTimeOffset now) =>
        new(
            campaign.Id,
            campaign.Title,
            campaign.Destination,
            campaign.DepartAt,
            campaign.ReturnAt,
            campaign.ClosesAt,
            campaign.Status,
            campaign.IsAcceptingOrders(now),
            campaign.CoverImageUrl);

    private static AdminCampaignView ToAdminView(CampaignAggregate campaign, int orderCount) =>
        new(
            campaign.Id,
            campaign.Title,
            campaign.Destination,
            campaign.DepartAt,
            campaign.ReturnAt,
            campaign.ClosesAt,
            campaign.PriceInquiryTimeoutMinutes,
            campaign.Status,
            orderCount,
            campaign.TripCostTotal,
            campaign.Description,
            campaign.CoverImageUrl);
}
