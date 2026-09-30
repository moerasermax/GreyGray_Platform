using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Payment.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct PaymentId(Guid Value)
{
    public static PaymentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct RefundId(Guid Value)
{
    public static RefundId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

/// <summary>
/// 金流商。<b>M1a 只做綠界一家</b>——它一家包辦金流、超商取貨、宅配，
/// 將來要開發票也不用換廠商。藍新與 LINE Pay 留到 M3，屆時 adapter 介面
/// 已被一個真實實作驗證過，第二、三家會快很多。
/// </summary>
public enum PaymentProvider
{
    ECPay = 1,
    NewebPay = 2,
    LinePay = 3,

    /// <summary>
    /// 通路擴充接縫 #5：外部通路（蝦皮）已代收結算。
    /// M1 不啟用，但列舉現在就要有位置——否則外部訂單走不完付款狀態機。
    /// </summary>
    ExternalSettled = 9,
}

public enum PaymentStatus
{
    Pending = 0,
    Captured = 1,
    Failed = 2,
    Refunded = 3,
    PartiallyRefunded = 4,
}

// ── DTO ──────────────────────────────────────────────────────────────────

public sealed record PaymentSummary(
    PaymentId Id,
    OrderId OrderId,
    PaymentProvider Provider,
    PaymentStatus Status,
    Money Amount,
    Money? Fee,
    string? ProviderTransactionId,
    DateTimeOffset? CapturedAt,
    DateTimeOffset? SettledAt);

/// <summary>
/// 付款方式 × 配送方式的相容性。<b>這是一條真實的業務規則，不是 UI 細節</b>——
/// 選 LINE Pay 的客人，超商取貨得靠另一家串接。Checkout 必須在客人選付款方式時
/// 就過濾可用的配送方式（或反過來）。
/// </summary>
public sealed record ProviderCapability(
    PaymentProvider Provider,
    bool SupportsConvenienceStore,
    bool SupportsHomeDelivery,
    bool SupportsEInvoice);

/// <summary>前端原封不動 POST 到金流商的導轉資料；簽章永遠只在後端產生。</summary>
public sealed record PaymentInitiation(
    PaymentProvider Provider,
    string Method,
    Uri Action,
    IReadOnlyDictionary<string, string> Fields,
    DateTimeOffset ExpiresAt);

/// <summary>
/// 發動一次綠界付款所需的全部資料。
/// <para>
/// <c>ClientBackUrl</c> 是綠界完成頁「返回商店」按鈕的目的地（綠界欄位 <c>ClientBackURL</c>）。
/// 綠界以 GET 導轉且不帶任何參數，所以網址本身就要帶得出「回到哪一張訂單」——
/// 由 Host 用 <c>Storefront:PublicOrigin</c> 組出 <c>/payment/result?orderId=…</c>。
/// </para>
/// </summary>
public sealed record PaymentInitiationRequest(
    OrderId OrderId,
    Money GoodsAmount,
    Money ShippingAmount,
    string Description,
    Uri ReturnUrl,
    Uri ClientBackUrl);

/// <summary>已通過綠界驗簽與必要欄位檢查的付款結果通知身分。</summary>
public sealed record EcpayCallbackEnvelope(
    string MerchantTradeNo,
    string TradeNo,
    int RtnCode);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface IPaymentQuery
{
    Task<Result<PaymentSummary>> GetAsync(PaymentId id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<PaymentSummary>>> GetByOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken);

    /// <summary>Checkout 用來過濾配送方式。</summary>
    Task<Result<IReadOnlyList<ProviderCapability>>> GetEnabledProvidersAsync(
        CancellationToken cancellationToken);
}

/// <summary>Payment 模組的同步寫入入口。Webhook 原始欄位交由模組內的 provider adapter 驗證。</summary>
public interface IPaymentCommand
{
    Task<Result<PaymentInitiation>> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken);

    Task<Result> HandleEcpayCallbackAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken);
}

/// <summary>綠界付款結果通知的只讀驗證入口；不讀寫資料庫，也不發送事件。</summary>
public interface IEcpayCallbackVerifier
{
    /// <summary>驗證 CheckMacValue、MerchantID 與必要欄位。</summary>
    Result<EcpayCallbackEnvelope> Verify(IReadOnlyDictionary<string, string> fields);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

/// <summary>
/// 收款成功。Ledger 訂閱後開 DR 現金（該金流商的在途子科目）/ CR 預收貨款・預收運費；
/// Ordering 訂閱後轉「已付款・湊團中」。
/// </summary>
/// <remarks>
/// <b>Payment 不記帳。</b>它只負責跟金流商互動並回報結果。
/// 這個分工必須從第一天守住，否則半年後你會有三個地方在算錢，而且互相對不上。
/// </remarks>
public sealed record PaymentCaptured(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    PaymentProvider Provider,
    Money Amount,
    Money GoodsPortion,
    Money ShippingPortion,
    string ProviderTransactionId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "payment.PaymentCaptured.v1";

    public override string AggregateType => "Payment";

    public override string AggregateId => PaymentId.ToString();
}

public sealed record PaymentFailed(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    PaymentProvider Provider,
    string FailureCode,
    string FailureMessage)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "payment.PaymentFailed.v1";

    public override string AggregateType => "Payment";

    public override string AggregateId => PaymentId.ToString();
}

public sealed record PaymentRefunded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    RefundId RefundId,
    PaymentId PaymentId,
    OrderId OrderId,
    OrderLineId? LineId,
    PaymentProvider Provider,
    Money Amount,
    RefundDestination Destination)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "payment.PaymentRefunded.v1";

    public override string AggregateType => "Payment";

    public override string AggregateId => PaymentId.ToString();
}

/// <summary>
/// 撥款到帳。客人付款當下錢在金流商手上，不在你的帳戶；
/// Ledger 訂閱後把「XX 在途」轉成「現金」，差額就是手續費。
/// <b>三家的撥款週期與手續費不同，所以在途科目必須分家</b>。
/// </summary>
public sealed record PayoutSettled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    PaymentProvider Provider,
    string SettlementBatchRef,
    Money GrossAmount,
    Money FeeAmount,
    Money NetAmount,
    IReadOnlyList<PaymentId> PaymentIds)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "payment.PayoutSettled.v1";

    public override string AggregateType => "PayoutBatch";

    public override string AggregateId => SettlementBatchRef;
}

/// <summary>對帳差異。<b>差異告警要按金流商分家</b>，混在一起看不出是哪家的問題。</summary>
public sealed record ReconciliationDiscrepancyFound(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    PaymentProvider Provider,
    DateOnly StatementDate,
    Money SystemTotal,
    Money StatementTotal,
    IReadOnlyList<string> Details)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "payment.ReconciliationDiscrepancyFound.v1";

    public override string AggregateType => "Reconciliation";

    public override string AggregateId => $"{Provider}:{StatementDate:yyyy-MM-dd}";
}
