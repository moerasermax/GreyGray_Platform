using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Inventory.Contracts;
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

/// <summary>
/// M2 本地批發進貨建立批號。DR 存貨 / CR 現金。
/// <para>
/// <b>★ 只有 <see cref="LotSource.LocalWholesale"/> 在這裡入帳。</b>
/// 代購那條線的進貨成本已經由 <see cref="GoodsReceivedLedgerHandler"/> 記過，而
/// <c>GoodsReceivedInventoryHandler</c> 建完批號之後<b>還會再發一次 <c>LotCreated</c></b>；
/// 不擋的話同一批貨會記兩次帳，存貨與現金雙雙翻倍（見 <c>docs/34</c> §1）。
/// 拒收退回轉現貨（<see cref="LotSource.CustomerReturn"/>）還沒有實作，
/// 它的成本沿用原採購成本、不是一筆新的進貨，所以同樣不在這裡入帳。
/// </para>
/// <para>
/// 科目與代購完全相同：<c>Ledger.Contracts.AccountCodes</c> 的註解已經定了
/// 「兩種模式用的是同一組科目，差別在 Saga 的狀態機上，不在帳上」。
/// 本地批發沒有團，所以 <c>campaignId</c> 留空。
/// </para>
/// </summary>
internal sealed class LotCreatedLedgerHandler(LedgerPostingService posting)
    : IIntegrationEventHandler<LotCreated>
{
    public async Task HandleAsync(LotCreated @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.Source != LotSource.LocalWholesale)
        {
            return;
        }

        var totalCost = @event.UnitCost.MultiplyByQuantity(@event.Quantity);
        await posting.PostAsync(
            @event.TenantId,
            @event.OccurredAt,
            "Inventory",
            @event.LotId.ToString(),
            $"本地批發進貨建立批號，SKU {@event.SkuId}",
            [
                new(AccountCodes.Inventory, Direction.Debit, totalCost),
                new(AccountCodes.Cash, Direction.Credit, totalCost),
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

/// <summary>
/// 出貨從批號結轉銷貨成本（<c>docs/02-事件與狀態機.md</c> §5 第 ⑦ 階段第一筆）。
/// DR 銷貨成本 / CR 存貨。
/// <para>
/// 事件由 Inventory 在<b>交運</b>時逐筆 allocation 發出，<see cref="StockCostAllocated.SourceRef"/>
/// 直接沿用作 <c>journal_entry.source_ref</c>——那是看帳的人唯一能追回「這筆成本是哪張訂單、
/// 哪個 SKU 出的貨」的線索，不要再包一層。
/// </para>
/// </summary>
internal sealed class StockCostAllocatedLedgerHandler(LedgerPostingService posting)
    : IIntegrationEventHandler<StockCostAllocated>
{
    public async Task HandleAsync(StockCostAllocated @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        await posting.PostAsync(
            @event.TenantId,
            @event.OccurredAt,
            "Inventory",
            @event.SourceRef,
            $"出貨結轉銷貨成本，SKU {@event.SkuId} × {@event.Quantity}",
            [
                new(AccountCodes.CostOfGoodsSold, Direction.Debit, @event.TotalCost),
                new(AccountCodes.Inventory, Direction.Credit, @event.TotalCost),
            ],
            cancellationToken);
    }
}

/// <summary>
/// 交運支付宅配運費（<c>docs/02-事件與狀態機.md</c> §5 第 ⑦ 階段第二筆）。
/// DR 運費成本 / CR 現金。
/// <para>
/// <see cref="ShipmentDispatched.CarrierCost"/> 是<b>付給物流商的成本</b>，
/// 不是向客人收的運費（後者在訂單的 <c>ShippingFee</c>，走 <c>OrderCompleted</c> 那條路）。
/// 兩個是獨立的數字，月結時運費是賺是賠自己會浮出來。
/// </para>
/// <para>
/// <b>成本為零就完全不開分錄</b>：一筆全零的 entry 在帳上沒有任何意義，
/// 只會讓「這張出貨單有沒有運費成本」變成要點進去看才知道。
/// </para>
/// </summary>
internal sealed class ShipmentDispatchedLedgerHandler(LedgerPostingService posting)
    : IIntegrationEventHandler<ShipmentDispatched>
{
    public async Task HandleAsync(ShipmentDispatched @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.CarrierCost.IsZero)
        {
            return;
        }

        await posting.PostAsync(
            @event.TenantId,
            @event.OccurredAt,
            "Fulfillment",
            @event.ShipmentId.ToString(),
            $"出貨單 {@event.ShipmentId} 交運，支付物流商運費（{@event.Method}）",
            [
                new(AccountCodes.ShippingCost, Direction.Debit, @event.CarrierCost),
                new(AccountCodes.Cash, Direction.Credit, @event.CarrierCost),
            ],
            cancellationToken);
    }
}
