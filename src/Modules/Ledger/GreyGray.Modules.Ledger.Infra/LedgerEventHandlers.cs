using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ledger.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Ledger.Infra;

internal sealed class PaymentCapturedLedgerHandler(LedgerPostingService posting)
    : IIntegrationEventHandler<PaymentCaptured>
{
    public async Task HandleAsync(PaymentCaptured @event, CancellationToken cancellationToken)
    {
        await posting.PostAsync(
            @event.TenantId,
            @event.OccurredAt,
            "Payment",
            @event.PaymentId.ToString(),
            $"訂單 {@event.OrderId} 收款",
            [
                new(TransitAccount(@event.Provider), Direction.Debit, @event.Amount),
                new(AccountCodes.DeferredGoodsRevenue, Direction.Credit, @event.GoodsPortion),
                new(AccountCodes.DeferredShippingRevenue, Direction.Credit, @event.ShippingPortion),
            ],
            cancellationToken);
    }

    internal static string TransitAccount(PaymentProvider provider) => provider switch
    {
        PaymentProvider.ECPay => AccountCodes.InTransitECPay,
        PaymentProvider.NewebPay => AccountCodes.InTransitNewebPay,
        PaymentProvider.LinePay => AccountCodes.InTransitLinePay,
        _ => throw new InvalidOperationException($"付款來源 {provider} 沒有在途科目。"),
    };
}

internal sealed class PayoutSettledLedgerHandler(LedgerPostingService posting)
    : IIntegrationEventHandler<PayoutSettled>
{
    public async Task HandleAsync(PayoutSettled @event, CancellationToken cancellationToken)
    {
        await posting.PostAsync(
            @event.TenantId,
            @event.OccurredAt,
            "PaymentPayout",
            $"{@event.Provider}:{@event.SettlementBatchRef}",
            $"{@event.Provider} 撥款 {@event.SettlementBatchRef}",
            [
                new(AccountCodes.Cash, Direction.Debit, @event.NetAmount),
                new(AccountCodes.PaymentProcessingFee, Direction.Debit, @event.FeeAmount),
                new(PaymentCapturedLedgerHandler.TransitAccount(@event.Provider), Direction.Credit, @event.GrossAmount),
            ],
            cancellationToken);
    }
}

internal sealed class PaymentRefundedLedgerHandler(
    LedgerPostingService posting,
    IOrderQuery orders) : IIntegrationEventHandler<PaymentRefunded>
{
    public async Task HandleAsync(PaymentRefunded @event, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(@event.OrderId, cancellationToken);
        if (order.IsFailure)
        {
            throw new InvalidOperationException(
                $"退款 {@event.RefundId} 找不到訂單 {@event.OrderId}：{order.Error.Code}");
        }

        var creditAccount = @event.Destination == RefundDestination.StoredValue
            ? AccountCodes.CustomerStoredValue
            : PaymentCapturedLedgerHandler.TransitAccount(@event.Provider);
        var lines = LiabilityLinesFor(@event, order.Value).ToList();
        lines.Add(new PostingLine(
            creditAccount,
            Direction.Credit,
            @event.Amount,
            CustomerId: @event.Destination == RefundDestination.StoredValue
                ? order.Value.CustomerId
                : null));
        await posting.PostAsync(
            @event.TenantId,
            @event.OccurredAt,
            "PaymentRefund",
            @event.RefundId.ToString(),
            $"訂單 {@event.OrderId} 退款",
            lines,
            cancellationToken);
    }

    internal static IReadOnlyList<PostingLine> LiabilityLinesFor(
        PaymentRefunded @event,
        OrderView order)
    {
        if (@event.Amount.IsNegative || @event.Amount.IsZero)
        {
            throw new InvalidOperationException("退款金額必須大於零。");
        }

        if (@event.LineId is { } lineId)
        {
            var line = order.Lines.SingleOrDefault(candidate => candidate.Id == lineId)
                ?? throw new InvalidOperationException($"退款品項 {lineId} 不屬於訂單 {order.Id}。");
            if (@event.Amount > line.LineTotal)
            {
                throw new InvalidOperationException("單一品項退款不得超過該品項金額。");
            }

            return [new(AccountCodes.DeferredGoodsRevenue, Direction.Debit, @event.Amount)];
        }

        if (@event.Amount > order.GrandTotal)
        {
            throw new InvalidOperationException("退款金額不得超過訂單總額。");
        }

        if (@event.Amount != order.GrandTotal)
        {
            return [new(AccountCodes.DeferredGoodsRevenue, Direction.Debit, @event.Amount)];
        }

        if (order.GoodsTotal.Add(order.ShippingFee) != order.GrandTotal)
        {
            throw new InvalidOperationException("訂單貨款與運費加總不等於訂單總額，無法建立全額退款分錄。");
        }

        var result = new List<PostingLine>(2);
        if (!order.GoodsTotal.IsZero)
        {
            result.Add(new PostingLine(
                AccountCodes.DeferredGoodsRevenue,
                Direction.Debit,
                order.GoodsTotal));
        }

        if (!order.ShippingFee.IsZero)
        {
            result.Add(new PostingLine(
                AccountCodes.DeferredShippingRevenue,
                Direction.Debit,
                order.ShippingFee));
        }

        return result;
    }
}

internal sealed class OrderCompletedLedgerHandler(
    LedgerPostingService posting,
    IOrderQuery orders) : IIntegrationEventHandler<OrderCompleted>
{
    public async Task HandleAsync(OrderCompleted @event, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(@event.OrderId, cancellationToken);
        if (order.IsFailure)
        {
            throw new InvalidOperationException(
                $"完成訂單 {@event.OrderId} 時無法取得訂單快照：{order.Error.Code}");
        }

        var groups = order.Value.Lines
            .GroupBy(line => line.CampaignId)
            .Select(group => new CampaignWeight(
                group.Key,
                group.Sum(line => checked(line.UnitPrice.AmountMinor * line.Quantity))))
            .Where(group => group.Weight > 0)
            .ToArray();
        if (groups.Length == 0)
        {
            groups = [new CampaignWeight(null, 1)];
        }

        var weights = groups.Select(group => group.Weight).ToArray();
        var goodsAllocations = @event.GoodsTotal.AllocateByWeights(weights);
        var shippingAllocations = @event.ShippingFee.IsZero
            ? groups.Select(_ => Money.Zero(@event.ShippingFee.Currency)).ToArray()
            : @event.ShippingFee.AllocateByWeights(weights).ToArray();
        var lines = new List<PostingLine>(groups.Length * 4);
        for (var index = 0; index < groups.Length; index++)
        {
            var campaignId = groups[index].CampaignId;
            var goods = goodsAllocations[index];
            var shipping = shippingAllocations[index];
            lines.Add(new PostingLine(
                AccountCodes.DeferredGoodsRevenue,
                Direction.Debit,
                goods,
                campaignId));
            lines.Add(new PostingLine(
                AccountCodes.SalesRevenue,
                Direction.Credit,
                goods,
                campaignId));
            if (!shipping.IsZero)
            {
                lines.Add(new PostingLine(
                    AccountCodes.DeferredShippingRevenue,
                    Direction.Debit,
                    shipping,
                    campaignId));
                lines.Add(new PostingLine(
                    AccountCodes.ShippingRevenue,
                    Direction.Credit,
                    shipping,
                    campaignId));
            }
        }

        await posting.PostAsync(
            @event.TenantId,
            @event.OccurredAt,
            "Ordering",
            @event.OrderId.ToString(),
            $"訂單 {@event.OrderId} 完成，預收轉收入",
            lines,
            cancellationToken);
    }

    private sealed record CampaignWeight(CampaignId? CampaignId, long Weight);
}

/// <summary>現場刷卡買入、帶回入庫。DR 存貨 / CR 現金。</summary>
internal sealed class GoodsReceivedLedgerHandler(LedgerPostingService posting)
    : IIntegrationEventHandler<GoodsReceived>
{
    public async Task HandleAsync(GoodsReceived @event, CancellationToken cancellationToken)
    {
        var totalCost = @event.UnitCost.MultiplyByQuantity(@event.Quantity);
        await posting.PostAsync(
            @event.TenantId,
            @event.OccurredAt,
            "Procurement",
            @event.EventId.ToString(),
            $"現場買入帶回入庫，SKU {@event.SkuId}",
            [
                new(AccountCodes.Inventory, Direction.Debit, totalCost, @event.CampaignId),
                new(AccountCodes.Cash, Direction.Credit, totalCost, @event.CampaignId),
            ],
            cancellationToken);
    }
}

/// <summary>旅程成本登錄。DR 旅程成本 / CR 現金；團被取消時這筆仍要入帳。</summary>
internal sealed class TripCostRecordedLedgerHandler(LedgerPostingService posting)
    : IIntegrationEventHandler<TripCostRecorded>
{
    public async Task HandleAsync(TripCostRecorded @event, CancellationToken cancellationToken)
    {
        await posting.PostAsync(
            @event.TenantId,
            @event.OccurredAt,
            "Campaign",
            @event.TripCostId.ToString(),
            string.IsNullOrWhiteSpace(@event.Memo)
                ? $"旅程成本（{@event.Kind}）"
                : $"旅程成本（{@event.Kind}）：{@event.Memo}",
            [
                new(AccountCodes.TripCost, Direction.Debit, @event.Amount, @event.CampaignId),
                new(AccountCodes.Cash, Direction.Credit, @event.Amount, @event.CampaignId),
            ],
            cancellationToken);
    }
}
