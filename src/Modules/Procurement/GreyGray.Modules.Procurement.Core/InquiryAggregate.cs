using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Procurement.Core;

/// <summary>
/// 現場詢價的軌跡。糾紛時這份軌跡就是證據，所以只記三件事：
/// 問了、幾點問的、客人有沒有回。解決之後不再變動（除了第一次回覆／逾時那一筆寫入）。
/// </summary>
internal sealed class InquiryAggregate
{
    private InquiryAggregate()
    {
    }

    private InquiryAggregate(
        InquiryId id,
        TenantId tenantId,
        PurchaseItemId purchaseItemId,
        Money originalPrice,
        Money newPrice,
        DateTimeOffset askedAt,
        DateTimeOffset timeoutAt)
    {
        Id = id;
        TenantId = tenantId;
        PurchaseItemId = purchaseItemId;
        OriginalPriceAmountMinor = originalPrice.AmountMinor;
        OriginalPriceCurrency = originalPrice.Currency;
        NewPriceAmountMinor = newPrice.AmountMinor;
        NewPriceCurrency = newPrice.Currency;
        AskedAt = askedAt;
        TimeoutAt = timeoutAt;
    }

    public InquiryId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public PurchaseItemId PurchaseItemId { get; private set; }

    public long OriginalPriceAmountMinor { get; private set; }

    public Currency OriginalPriceCurrency { get; private set; }

    public long NewPriceAmountMinor { get; private set; }

    public Currency NewPriceCurrency { get; private set; }

    public DateTimeOffset AskedAt { get; private set; }

    public DateTimeOffset TimeoutAt { get; private set; }

    public DateTimeOffset? RepliedAt { get; private set; }

    public InquiryOutcome? Outcome { get; private set; }

    public string? ReplyText { get; private set; }

    public Money OriginalPrice => new(OriginalPriceAmountMinor, OriginalPriceCurrency);

    public Money NewPrice => new(NewPriceAmountMinor, NewPriceCurrency);

    public static InquiryAggregate Open(
        InquiryId id,
        TenantId tenantId,
        PurchaseItemId purchaseItemId,
        Money originalPrice,
        Money newPrice,
        DateTimeOffset askedAt,
        DateTimeOffset timeoutAt) =>
        new(id, tenantId, purchaseItemId, originalPrice, newPrice, askedAt, timeoutAt);

    /// <summary>
    /// 冪等：以第一次解決為準。客人回覆晚到、或逾時之後客人才回，
    /// 第二次呼叫不改變已經記下的結果，只是單純的 no-op。
    /// </summary>
    public InquiryResolutionTransition Resolve(
        InquiryOutcome outcome,
        string? replyText,
        DateTimeOffset repliedAt)
    {
        if (RepliedAt is not null)
        {
            return InquiryResolutionTransition.AlreadyResolved;
        }

        RepliedAt = repliedAt;
        Outcome = outcome;
        ReplyText = replyText;
        return InquiryResolutionTransition.Resolved;
    }

    public Inquiry ToContract() =>
        new(
            Id,
            PurchaseItemId,
            OriginalPrice,
            NewPrice,
            AskedAt,
            TimeoutAt,
            RepliedAt,
            Outcome,
            ReplyText);
}

internal enum InquiryResolutionTransition
{
    AlreadyResolved = 0,
    Resolved = 1,
}

internal interface IInquiryRepository
{
    Task<InquiryAggregate?> GetAsync(
        TenantId tenantId,
        InquiryId id,
        CancellationToken cancellationToken);

    /// <summary>某採購品項尚未解決的詢問；同一時間最多一輪。</summary>
    Task<InquiryAggregate?> GetOpenByPurchaseItemAsync(
        TenantId tenantId,
        PurchaseItemId purchaseItemId,
        CancellationToken cancellationToken);

    /// <summary>批次取每個採購品項最近一輪詢問（不論是否已解決），給清單畫面用。</summary>
    Task<IReadOnlyDictionary<PurchaseItemId, InquiryAggregate>> GetLatestByPurchaseItemsAsync(
        TenantId tenantId,
        IReadOnlyCollection<PurchaseItemId> purchaseItemIds,
        CancellationToken cancellationToken);

    void Add(InquiryAggregate inquiry);
}
