using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Inventory.Core;

internal sealed record StockReservationLine(SkuId SkuId, int Quantity);

internal sealed record StockReservationPlan(
    string ReservationKey,
    IReadOnlyList<StockReservationLine> Lines)
{
    public static Result<StockReservationPlan> Create(
        string reservationKey,
        IReadOnlyList<(SkuId SkuId, int Quantity)> lines)
    {
        var normalizedKey = reservationKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKey) || normalizedKey.Length > 255)
        {
            return Result<StockReservationPlan>.Failure(
                "inventory.reservation-key-invalid",
                "庫存保留 key 必須是 1 到 255 個字元。");
        }

        if (lines is null || lines.Count == 0)
        {
            return Result<StockReservationPlan>.Failure(
                "inventory.reservation-lines-empty",
                "庫存保留至少要有一個現貨品項。");
        }

        if (lines.Any(line => line.SkuId.Value == Guid.Empty || line.Quantity <= 0))
        {
            return Result<StockReservationPlan>.Failure(
                "inventory.reservation-line-invalid",
                "庫存保留的 SKU 與數量必須有效，數量必須大於零。");
        }

        try
        {
            var aggregated = lines
                .GroupBy(line => line.SkuId)
                .Select(group => new StockReservationLine(
                    group.Key,
                    group.Aggregate(0, (total, line) => checked(total + line.Quantity))))
                .OrderBy(line => line.SkuId.Value)
                .ToArray();
            return new StockReservationPlan(normalizedKey, aggregated);
        }
        catch (OverflowException)
        {
            return Result<StockReservationPlan>.Failure(
                "inventory.reservation-quantity-overflow",
                "庫存保留數量超出允許範圍。");
        }
    }

    public static string ForOrder(OrderId orderId) => $"ordering:{orderId}";
}
