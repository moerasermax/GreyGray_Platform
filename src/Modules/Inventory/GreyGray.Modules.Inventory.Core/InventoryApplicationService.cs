using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Inventory.Core;

/// <summary>
/// M2 批發進貨與批號列表。批號是<b>成本的載體</b>，所以建立這件事必須有自己的冪等：
/// 重複建立的後果是幽靈庫存加一筆多出來的存貨分錄。
/// </summary>
internal sealed class InventoryApplicationService(
    IInventoryLotRepository lots,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    IClock clock,
    ICorrelationContext correlationContext)
    : IInventoryReceiving, IInventoryLotQuery
{
    public async Task<Result<Contracts.Lot>> ReceiveWholesaleAsync(
        WholesaleReceipt receipt,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var key = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 255)
        {
            return Result<Contracts.Lot>.Failure(
                "platform.idempotency-key-required",
                "批發進貨必須提供 1 到 255 字元的 Idempotency-Key。");
        }

        // ★ 先查鍵，再跑其他驗證：重播路徑最短，也跟 Fulfillment／Checkout 的樣板一致。
        var existing = await lots.GetByCreationKeyAsync(
            correlationContext.TenantId,
            key,
            cancellationToken);
        if (existing is not null)
        {
            return SameReceipt(existing, receipt)
                ? existing.ToContract()
                : Result<Contracts.Lot>.Failure(
                    "inventory.idempotency-key-reused",
                    "這把 Idempotency-Key 已經建立過內容不同的批號。");
        }

        var receivedAt = clock.UtcNow;
        var created = LotAggregate.CreateFromWholesale(
            LotId.New(),
            correlationContext.TenantId,
            receipt.SkuId,
            receipt.Quantity,
            receipt.UnitCost,
            receipt.BatchCode,
            key,
            receivedAt);
        if (created.IsFailure)
        {
            return Result<Contracts.Lot>.Failure(created.Error);
        }

        lots.Add(created.Value);
        await eventPublisher.PublishAsync(
            new LotCreated(
                Guid.CreateVersion7(),
                receivedAt,
                correlationContext.TenantId,
                new LotId(created.Value.Id),
                receipt.SkuId,
                LotSource.LocalWholesale,
                receipt.UnitCost,
                receipt.Quantity,
                FromCampaign: null),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created.Value.ToContract();
    }

    public async Task<Result<LotPage>> ListLotsAsync(
        AdminLotListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await lots.ListAsync(correlationContext.TenantId, request, cancellationToken);
        return new LotPage(
            page.Items.Select(lot => lot.ToContract()).ToArray(),
            page.NextCursor?.ToString());
    }

    /// <summary>
    /// 重播比對只看<b>建立後不會再變的欄位</b>：SKU、成本、批號代碼。
    /// <para>
    /// 刻意不比 <c>QuantityOnHand</c>——今天沒有任何路徑會改它，但出貨結轉銷貨成本
    /// 一旦實作就會，屆時一次合法的重試會被判成 <c>idempotency-key-reused</c>，
    /// 而重試的唯一出路是換一把新鍵，換新鍵正好繞過這一層冪等、建出第二個批號。
    /// 那就是 #22 (A″)「重試永遠回不了成功」的死路，不要在這裡複製一個。
    /// </para>
    /// <para>
    /// 「同一把鍵但送了不同數量」在 HTTP 那一層就已經被
    /// <c>IIdempotencyStore</c> 的 payload 比對擋成 422（docs/05-API契約.md §4），
    /// 這一層的鍵是為了擋「鍵被 abandon 之後同鍵重試」，那條路徑的 payload 必然相同。
    /// </para>
    /// </summary>
    private static bool SameReceipt(LotAggregate existing, WholesaleReceipt receipt) =>
        existing.SkuId == receipt.SkuId.Value
        && existing.UnitCostAmountMinor == receipt.UnitCost.AmountMinor
        && existing.UnitCostCurrency == receipt.UnitCost.Currency
        && string.Equals(
            existing.BatchCode,
            string.IsNullOrWhiteSpace(receipt.BatchCode) ? null : receipt.BatchCode.Trim(),
            StringComparison.Ordinal);
}
