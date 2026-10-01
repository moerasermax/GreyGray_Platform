using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.Platform.Tests;

public sealed class BffHttpStatusForTests
{
    [Theory(DisplayName = "S1a：三個指定的競態錯誤碼精確對應 409")]
    [InlineData("payment.instructions-already-issued")]
    [InlineData("payment.concurrent-update")]
    [InlineData("ordering.concurrent-update")]
    public async Task Exact_concurrency_codes_return_conflict(string code)
    {
        var status = await ExecuteProblemAsync(code);

        status.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Theory(DisplayName = "S1b：近似競態錯誤碼不會誤判成 409")]
    [InlineData("payment.instructions-already-issued-again")]
    [InlineData("prefix.payment.concurrent-update")]
    [InlineData("ordering.concurrent-update.suffix")]
    [InlineData("Payment.concurrent-update")]
    public async Task Similar_concurrency_codes_remain_unprocessable(string code)
    {
        var status = await ExecuteProblemAsync(code);

        status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    [Theory(DisplayName = "S1c：既有狀態規則維持不變")]
    [InlineData("identity.invalid-credentials", StatusCodes.Status401Unauthorized)]
    [InlineData("ordering.not-found", StatusCodes.Status404NotFound)]
    [InlineData("payment.already-paid", StatusCodes.Status409Conflict)]
    [InlineData("ordering.cancelled", StatusCodes.Status409Conflict)]
    [InlineData("payment.some-other-error", StatusCodes.Status422UnprocessableEntity)]
    public async Task Existing_status_rules_are_unchanged(string code, int expectedStatus)
    {
        var status = await ExecuteProblemAsync(code);

        status.ShouldBe(expectedStatus);
    }

    private static async Task<int> ExecuteProblemAsync(string code)
    {
        var result = BffHttp.Problem(new Error(code, "測試錯誤。"));
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        return context.Response.StatusCode;
    }
}
