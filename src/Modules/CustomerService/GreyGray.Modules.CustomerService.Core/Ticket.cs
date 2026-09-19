using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.CustomerService.Core;

/// <summary>
/// 客服工單聚合根。<b>刻意拆成 ticket／ticket_message 兩張表</b>（見 db/migrations/0022），
/// 但 M1a 每張工單永遠只有一則 <see cref="TicketMessage"/>——欄位攤平到 <see cref="Message"/>／
/// <see cref="MenuPath"/> 是為了之後補「客服回覆」時不用搬資料，這一波不用多則訊息的能力。
/// </summary>
internal sealed class Ticket
{
    private readonly List<TicketMessage> _messages = [];

    private Ticket()
    {
    }

    private Ticket(
        TicketId id,
        TenantId tenantId,
        string message,
        string? contactEmail,
        string? contactPhone,
        IReadOnlyList<string> menuPath,
        TicketOrderId? orderId,
        CustomerId? customerId,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Status = SupportTicketStatus.Open;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        OrderId = orderId;
        CustomerId = customerId;
        CreatedAt = createdAt;
        _messages.Add(TicketMessage.Create(id, message, menuPath, createdAt));
    }

    public TicketId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public SupportTicketStatus Status { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    public TicketOrderId? OrderId { get; private set; }

    public CustomerId? CustomerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public StaffId? ResolvedBy { get; private set; }

    public string? StaffNote { get; private set; }

    public IReadOnlyList<TicketMessage> Messages => _messages;

    public string Message => _messages[0].Body;

    public IReadOnlyList<string> MenuPath => _messages[0].MenuPath;

    public static Result<Ticket> Create(
        TenantId tenantId,
        string message,
        string? contactEmail,
        string? contactPhone,
        IReadOnlyList<string> menuPath,
        TicketOrderId? orderId,
        CustomerId? customerId,
        DateTimeOffset createdAt)
    {
        var normalizedMessage = message?.Trim() ?? string.Empty;
        if (normalizedMessage.Length is < 1 or > 2000)
        {
            return Result<Ticket>.Failure(
                "support.invalid-message", "留言內容需為 1 到 2000 字。");
        }

        var normalizedEmail = NormalizeContact(contactEmail);
        var normalizedPhone = NormalizeContact(contactPhone);
        if (normalizedEmail is null && normalizedPhone is null)
        {
            return Result<Ticket>.Failure(
                "support.contact-required", "請至少留下 Email 或手機其中一項。");
        }

        var normalizedMenuPath = menuPath ?? [];
        if (normalizedMenuPath.Count > 5
            || normalizedMenuPath.Any(step => string.IsNullOrEmpty(step) || step.Length > 100))
        {
            return Result<Ticket>.Failure(
                "support.invalid-menu-path", "選單路徑最多 5 層，每層最多 100 字。");
        }

        return new Ticket(
            TicketId.New(),
            tenantId,
            normalizedMessage,
            normalizedEmail,
            normalizedPhone,
            normalizedMenuPath,
            orderId,
            customerId,
            createdAt);
    }

    /// <summary>供 Infra 從資料庫重建聚合，不重跑驗證（資料已經通過驗證才落地過）。</summary>
    public static Ticket Reconstruct(
        TicketId id,
        TenantId tenantId,
        SupportTicketStatus status,
        string? contactEmail,
        string? contactPhone,
        TicketOrderId? orderId,
        CustomerId? customerId,
        DateTimeOffset createdAt,
        DateTimeOffset? resolvedAt,
        StaffId? resolvedBy,
        string? staffNote,
        IReadOnlyList<TicketMessage> messages)
    {
        var ticket = new Ticket
        {
            Id = id,
            TenantId = tenantId,
            Status = status,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            OrderId = orderId,
            CustomerId = customerId,
            CreatedAt = createdAt,
            ResolvedAt = resolvedAt,
            ResolvedBy = resolvedBy,
            StaffNote = staffNote,
        };
        ticket._messages.AddRange(messages);
        return ticket;
    }

    public Result Resolve(StaffId staffId, string? staffNote, DateTimeOffset resolvedAt)
    {
        if (Status == SupportTicketStatus.Resolved)
        {
            return Result.Failure("support.already-resolved", "這張工單已經處理過。");
        }

        Status = SupportTicketStatus.Resolved;
        ResolvedAt = resolvedAt;
        ResolvedBy = staffId;
        StaffNote = string.IsNullOrWhiteSpace(staffNote) ? null : staffNote.Trim();
        return Result.Success();
    }

    public SupportTicket ToContract() => new(
        Id,
        Status,
        Message,
        ContactEmail,
        ContactPhone,
        MenuPath,
        OrderId,
        CustomerId,
        CreatedAt,
        ResolvedAt,
        ResolvedBy,
        StaffNote);

    private static string? NormalizeContact(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

internal sealed class TicketMessage
{
    private TicketMessage()
    {
    }

    private TicketMessage(
        Guid id,
        TicketId ticketId,
        string body,
        IReadOnlyList<string> menuPath,
        DateTimeOffset createdAt)
    {
        Id = id;
        TicketId = ticketId;
        Body = body;
        MenuPath = menuPath.ToArray();
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public TicketId TicketId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    /// <summary>
    /// <b>宣告成 <c>string[]</c> 不是 <c>IReadOnlyList&lt;string&gt;</c></b>——EF／Npgsql 的
    /// jsonb 對應要看到具體的 CLR 陣列型別才知道怎麼materialize，照 Pricing 的
    /// <c>PricingSnapshotEntity.Explain</c> 同一個形狀。
    /// </summary>
    public string[] MenuPath { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public static TicketMessage Create(
        TicketId ticketId, string body, IReadOnlyList<string> menuPath, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), ticketId, body, menuPath, createdAt);

    /// <summary>供 Infra 從資料庫重建。</summary>
    public static TicketMessage Reconstruct(
        Guid id, TicketId ticketId, string body, IReadOnlyList<string> menuPath, DateTimeOffset createdAt) =>
        new(id, ticketId, body, menuPath, createdAt);
}
