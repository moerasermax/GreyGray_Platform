using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.CustomerService.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;

namespace GreyGray.CustomerService.Tests;

public sealed class TicketTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "email 與 phone 都沒填 → 422 support.contact-required")]
    public void Create_without_any_contact_fails()
    {
        var result = Ticket.Create(TenantId.Default, "你好", null, null, [], null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("support.contact-required");
    }

    [Theory(DisplayName = "email 或 phone 其中一個就夠")]
    [InlineData("a@b.com", null)]
    [InlineData(null, "0912345678")]
    [InlineData("a@b.com", "0912345678")]
    public void Create_with_either_contact_succeeds(string? email, string? phone)
    {
        var result = Ticket.Create(TenantId.Default, "你好", email, phone, [], null, null, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(SupportTicketStatus.Open);
        result.Value.CreatedAt.ShouldBe(Now);
    }

    [Fact(DisplayName = "留言超過 2000 字 → 擋下")]
    public void Create_with_message_over_2000_chars_fails()
    {
        var result = Ticket.Create(
            TenantId.Default, new string('字', 2001), "a@b.com", null, [], null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("support.invalid-message");
    }

    [Fact(DisplayName = "空白留言 → 擋下")]
    public void Create_with_blank_message_fails()
    {
        var result = Ticket.Create(TenantId.Default, "   ", "a@b.com", null, [], null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("support.invalid-message");
    }

    [Fact(DisplayName = "menuPath 超過 5 層 → 擋下")]
    public void Create_with_menu_path_over_five_levels_fails()
    {
        var result = Ticket.Create(
            TenantId.Default, "你好", "a@b.com", null,
            ["1", "2", "3", "4", "5", "6"], null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("support.invalid-menu-path");
    }

    [Fact(DisplayName = "menuPath 單層超過 100 字 → 擋下")]
    public void Create_with_menu_path_step_over_100_chars_fails()
    {
        var result = Ticket.Create(
            TenantId.Default, "你好", "a@b.com", null,
            [new string('x', 101)], null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("support.invalid-menu-path");
    }

    [Fact(DisplayName = "menuPath 恰好 5 層、每層恰好 100 字都可以")]
    public void Create_with_menu_path_at_the_boundary_succeeds()
    {
        var path = Enumerable.Range(0, 5).Select(_ => new string('x', 100)).ToArray();
        var result = Ticket.Create(TenantId.Default, "你好", "a@b.com", null, path, null, null, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MenuPath.ShouldBe(path);
    }

    [Fact(DisplayName = "建立時帶入登入客人與訂單，工單記得住")]
    public void Create_records_customer_and_order()
    {
        var customerId = CustomerId.New();
        var orderId = new TicketOrderId(Guid.NewGuid());
        var result = Ticket.Create(
            TenantId.Default, "訂單有問題", "a@b.com", null, ["FAQ", "訂單問題"], orderId, customerId, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CustomerId.ShouldBe(customerId);
        result.Value.OrderId.ShouldBe(orderId);
    }

    [Fact(DisplayName = "結案設定 ResolvedAt／ResolvedBy／StaffNote，且能被匯出成契約 DTO")]
    public void Resolve_sets_resolution_fields_and_projects_to_contract()
    {
        var ticket = Ticket.Create(
            TenantId.Default, "你好", "a@b.com", null, [], null, null, Now).Value;
        var staffId = StaffId.New();
        var resolvedAt = Now.AddHours(1);

        var resolved = ticket.Resolve(staffId, "已回覆", resolvedAt);

        resolved.IsSuccess.ShouldBeTrue();
        ticket.Status.ShouldBe(SupportTicketStatus.Resolved);
        ticket.ResolvedAt.ShouldBe(resolvedAt);
        ticket.ResolvedBy.ShouldBe(staffId);
        ticket.StaffNote.ShouldBe("已回覆");

        var contract = ticket.ToContract();
        contract.Status.ShouldBe(SupportTicketStatus.Resolved);
        contract.ResolvedBy.ShouldBe(staffId);
    }

    [Fact(DisplayName = "重複結案 → 409 support.already-resolved")]
    public void Resolve_twice_fails_the_second_time()
    {
        var ticket = Ticket.Create(
            TenantId.Default, "你好", "a@b.com", null, [], null, null, Now).Value;
        ticket.Resolve(StaffId.New(), null, Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();

        var second = ticket.Resolve(StaffId.New(), "第二次", Now.AddMinutes(10));

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("support.already-resolved");
    }
}
