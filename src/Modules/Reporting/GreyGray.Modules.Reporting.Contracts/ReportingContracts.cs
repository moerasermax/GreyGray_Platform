using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Reporting.Contracts;

// ── 支撐模組：只訂閱事件建 read model，不被任何人依賴 ──────────────────
//
// 這個模組是「某天報表查詢拖垮線上時，要能在一天內拆成獨立服務」的那一個。
// 所以它只有 read model，不參與任何寫入路徑。

public sealed record CampaignProfitReport(
    CampaignId CampaignId,
    string Title,
    string Destination,
    DateOnly DepartAt,
    DateOnly ReturnAt,
    int OrderCount,
    CampaignMargin Margin,
    DateTimeOffset GeneratedAt);

public sealed record MonthlyPnLReport(
    int Year,
    int Month,
    Money SalesRevenue,
    Money ShippingRevenue,
    Money CostOfGoodsSold,
    Money ShippingCost,
    Money TripCost,
    Money PaymentProcessingFee,
    DateTimeOffset GeneratedAt)
{
    public Money GrossProfit => SalesRevenue
        .Add(ShippingRevenue)
        .Subtract(CostOfGoodsSold)
        .Subtract(ShippingCost)
        .Subtract(TripCost)
        .Subtract(PaymentProcessingFee);
}

/// <summary>批號成本分析。用來檢討「上次那批買貴了沒」。</summary>
public sealed record LotCostAnalysis(
    string SkuName,
    string? BatchCode,
    Money UnitCost,
    int QuantityReceived,
    int QuantitySold,
    Money? AverageSellingPrice);

public sealed record OrderFunnelSnapshot(
    IReadOnlyDictionary<OrderStatus, int> CountsByStatus,
    DateTimeOffset AsOf);

public interface IReportingQuery
{
    Task<Result<CampaignProfitReport>> GetCampaignProfitAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken);

    Task<Result<MonthlyPnLReport>> GetMonthlyPnLAsync(
        int year,
        int month,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<LotCostAnalysis>>> GetLotCostAnalysisAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    Task<Result<OrderFunnelSnapshot>> GetOrderFunnelAsync(CancellationToken cancellationToken);
}

/// <summary>報表產生完成（Worker 排程產出後上 R2）。</summary>
public sealed record ReportGenerated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    string ReportCode,
    string ObjectKey)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "reporting.ReportGenerated.v1";
}
