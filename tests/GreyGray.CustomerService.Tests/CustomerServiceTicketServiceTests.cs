using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.CustomerService.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;

namespace GreyGray.CustomerService.Tests;

public sealed class CustomerServiceTicketServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "分頁 limit 超出 1..100 → 擋下")]
    public async Task ListAsync_rejects_out_of_range_limit()
    {
        var service = BuildService(out _);

        var result = await service.ListAsync(
            new AdminTicketListRequest(null, null, 0),
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("support.invalid-page-limit");
    }

    [Fact(DisplayName = "找不到的工單 → 404 support.ticket-not-found")]
    public async Task GetAsync_returns_not_found_for_unknown_ticket()
    {
        var service = BuildService(out _);

        var result = await service.GetAsync(TicketId.New(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("support.ticket-not-found");
    }

    [Fact(DisplayName = "建立成功後可以查得到、可以結案；重複結案回 409")]
    public async Task Create_then_get_then_resolve_then_resolve_again()
    {
        var service = BuildService(out _);

        var created = await service.CreateAsync(
            new CreateSupportTicketRequest("我需要幫忙", "a@b.com", null, [], null, null),
            TestContext.Current.CancellationToken);
        created.IsSuccess.ShouldBeTrue();

        var fetched = await service.GetAsync(created.Value.Id, TestContext.Current.CancellationToken);
        fetched.IsSuccess.ShouldBeTrue();
        fetched.Value.Message.ShouldBe("我需要幫忙");

        var staffId = StaffId.New();
        var resolved = await service.ResolveAsync(
            created.Value.Id, staffId, "已處理", TestContext.Current.CancellationToken);
        resolved.IsSuccess.ShouldBeTrue();
        resolved.Value.Status.ShouldBe(SupportTicketStatus.Resolved);

        var resolvedAgain = await service.ResolveAsync(
            created.Value.Id, staffId, null, TestContext.Current.CancellationToken);
        resolvedAgain.IsFailure.ShouldBeTrue();
        resolvedAgain.Error.Code.ShouldBe("support.already-resolved");
    }

    [Fact(DisplayName = "建立時的驗證錯誤原樣往上傳")]
    public async Task Create_propagates_validation_failures()
    {
        var service = BuildService(out _);

        var result = await service.CreateAsync(
            new CreateSupportTicketRequest("嗨", null, null, [], null, null),
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("support.contact-required");
    }

    private static CustomerServiceTicketService BuildService(out InMemoryTicketRepository repository)
    {
        repository = new InMemoryTicketRepository();
        return new CustomerServiceTicketService(
            repository,
            new NoopUnitOfWork(),
            new FakeClock(Now),
            new FakeCorrelationContext());
    }
}

/// <summary>純記憶體版 <see cref="ITicketRepository"/>，讓服務層測試不必碰資料庫。</summary>
internal sealed class InMemoryTicketRepository : ITicketRepository
{
    private readonly List<Ticket> _tickets = [];

    public void Add(Ticket ticket) => _tickets.Add(ticket);

    public Task<Ticket?> FindAsync(TicketId id, TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(_tickets.SingleOrDefault(
            ticket => ticket.Id == id && ticket.TenantId == tenantId));

    public Task<(IReadOnlyList<Ticket> Items, bool HasNext)> ListAsync(
        TenantId tenantId,
        SupportTicketStatus? status,
        TicketId? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = _tickets.Where(ticket => ticket.TenantId == tenantId);
        if (status is not null)
        {
            query = query.Where(ticket => ticket.Status == status.Value);
        }

        var ordered = query
            .OrderByDescending(ticket => ticket.CreatedAt)
            .ThenByDescending(ticket => ticket.Id.Value)
            .ToArray();

        if (cursor is not null)
        {
            var anchorIndex = Array.FindIndex(ordered, ticket => ticket.Id == cursor.Value);
            ordered = anchorIndex < 0 ? [] : ordered.Skip(anchorIndex + 1).ToArray();
        }

        var hasNext = ordered.Length > limit;
        return Task.FromResult<(IReadOnlyList<Ticket>, bool)>((ordered.Take(limit).ToArray(), hasNext));
    }
}
