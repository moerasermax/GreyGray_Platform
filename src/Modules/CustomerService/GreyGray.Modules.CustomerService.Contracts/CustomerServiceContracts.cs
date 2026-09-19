using System.Text.Json.Serialization;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.CustomerService.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct TicketId(Guid Value)
{
    public static TicketId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");

    public static bool operator <(TicketId left, TicketId right) => left.Value.CompareTo(right.Value) < 0;

    public static bool operator >(TicketId left, TicketId right) => left.Value.CompareTo(right.Value) > 0;
}

/// <summary>
/// 客人留言時可能帶的訂單參照。<b>刻意不是裸 <c>Guid?</c></b>——
/// <c>GuidIdJsonConverterFactory.CanConvert</c>（<c>Shared.Kernel</c>，不在這包的 allow 裡）
/// 對 <c>Nullable&lt;Guid&gt;</c> 誤判為「也是一種 <c>XxxId</c>」（<c>Nullable&lt;T&gt;.Value</c>
/// 剛好也是一個型別為 <c>Guid</c> 的公開屬性，且有吃 <c>Guid</c> 的建構式），
/// 於是嘗試 <c>GuidIdJsonConverter&lt;Guid?&gt;</c>，而 <c>Nullable&lt;T&gt;</c>
/// 不滿足 <c>where TId : struct</c> 的泛型限制，序列化與反序列化都會丟
/// <see cref="TypeLoadException"/>。包成這個型別後序列化出來的 JSON 字串完全不變
/// （一樣是 <c>Convert.ToHexStringLower</c> 那種無連字號 32 碼），只是不再撞到那個誤判。
/// 這也剛好是「定義自己要的最小投影，不要吃對方的完整狀態機」——CustomerService 只需要
/// 一個不透明的訂單參照，不需要引用 Ordering.Contracts。
/// </summary>
public readonly record struct TicketOrderId(Guid Value)
{
    public override string ToString() => Value.ToString("N");
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

/// <summary>
/// 契約的 JSON 值是 <c>open</c>／<c>resolved</c>（小寫），是全專案唯一的小寫列舉——
/// 其他列舉一律用成員名稱原樣（PascalCase）。<see cref="JsonStringEnumMemberName"/> 讓
/// <c>JsonStringEnumConverter</c>（<c>GreyGrayJson</c> 全域註冊）照契約寫，不必另建 converter。
/// </summary>
public enum SupportTicketStatus
{
    [JsonStringEnumMemberName("open")]
    Open = 0,

    [JsonStringEnumMemberName("resolved")]
    Resolved = 1,
}

// ── 對外 DTO ─────────────────────────────────────────────────────────────

/// <summary>
/// 客服工單。<b>刻意沒有 messages 陣列</b>——M1a 只有客人留的第一句話，
/// <c>Message</c>／<c>MenuPath</c> 取自那一則訊息；資料庫拆成 ticket／ticket_message
/// 兩張表是留給之後補「客服回覆」用的，這一波不用。
/// </summary>
public sealed record SupportTicket(
    TicketId Id,
    SupportTicketStatus Status,
    string Message,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<string> MenuPath,
    TicketOrderId? OrderId,
    CustomerId? CustomerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    StaffId? ResolvedBy,
    string? StaffNote);

public sealed record SupportTicketPage(
    IReadOnlyList<SupportTicket> Items,
    TicketId? NextCursor);

// ── 建立工單 ─────────────────────────────────────────────────────────────

public sealed record CreateSupportTicketRequest(
    string Message,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<string> MenuPath,
    TicketOrderId? OrderId,
    CustomerId? CustomerId);

// ── 後台列表 ─────────────────────────────────────────────────────────────

public sealed record AdminTicketListRequest(
    SupportTicketStatus? Status,
    TicketId? Cursor,
    int Limit);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface ICustomerServiceTickets
{
    /// <summary>
    /// 建立工單。<b>不做防灌</b>——同一 IP／購物車每小時上限是 HTTP 邊界關心的事，
    /// 由 Storefront Host 用 <c>IDistributedCache</c> 自己擋，這裡只管落地與驗證。
    /// </summary>
    Task<Result<SupportTicket>> CreateAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken);

    Task<Result<SupportTicketPage>> ListAsync(
        AdminTicketListRequest request,
        CancellationToken cancellationToken);

    Task<Result<SupportTicket>> GetAsync(TicketId id, CancellationToken cancellationToken);

    /// <summary>標記已處理。重複結案回 <c>409 support.already-resolved</c>。</summary>
    Task<Result<SupportTicket>> ResolveAsync(
        TicketId id,
        StaffId staffId,
        string? staffNote,
        CancellationToken cancellationToken);
}
