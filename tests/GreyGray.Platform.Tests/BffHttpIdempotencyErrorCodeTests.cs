using System.Text.Json;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.Platform.Tests;

/// <summary>
/// BE-37：<see cref="BffHttp"/> 四種冪等失敗回的 <c>code</c> 字串與 HTTP 狀態碼。
/// </summary>
/// <remarks>
/// <para>
/// <b>權威是 <c>docs/05-API契約.md</c> §4「冪等」，不是這支測試、也不是實作。</b>
/// 契約已經凍結，這四個字串就是前後端的邊界；要改請先改契約，不要改這裡。
/// </para>
/// <para>
/// 這一組存在的理由是「現在卡在哪」#24：BFF 一直回 <c>request.</c> 前綴，契約寫的是
/// <c>platform.</c>，而前端 <c>problem.ts</c> 的 <c>isInFlight</c> 比對的是
/// <c>platform.request-in-flight</c>——<b>後端從來沒有回過那個字串，所以「409 用同一把 key
/// 自動重試」從上線第一天起就沒有觸發過一次</b>。這個缺陷活到現在，正是因為
/// <b>沒有任何測試斷言過這些字串</b>。所以下面四條是逐字比對：改一個字就立刻紅。
/// </para>
/// <para>
/// 刻意不用 Testcontainers：要釘的是 <see cref="BffHttp"/> 把冪等結果翻成 HTTP 的那一段對應，
/// 不是 <c>IdempotencyStore</c> 的 SQL（那由 <see cref="IdempotencyAndSagaTests"/> 守著）。
/// </para>
/// </remarks>
public sealed class BffHttpIdempotencyErrorCodeTests
{
    private const string Scope = "test:idempotency-codes";
    private const string Key = "key-1";

    [Fact(DisplayName = "BE-37 契約①：沒帶 Idempotency-Key → 400 platform.idempotency-key-required")]
    public async Task Missing_key_returns_400_with_the_contract_code()
    {
        var store = new InMemoryIdempotencyStore();
        var context = NewContext(idempotencyKey: null);
        var workCalls = 0;

        var result = await BffHttp.ExecuteIdempotentAsync(
            context,
            store,
            Scope,
            new { Payload = "x" },
            _ =>
            {
                workCalls++;
                return Task.FromResult(Result<string>.Success("不該跑到這裡"));
            },
            StatusCodes.Status201Created,
            TestContext.Current.CancellationToken);

        var problem = await ReadProblemAsync(result, context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        problem.Status.ShouldBe(StatusCodes.Status400BadRequest);
        problem.Code.ShouldBe("platform.idempotency-key-required");
        problem.Type.ShouldBe("https://greygray.tw/errors/platform.idempotency-key-required");
        workCalls.ShouldBe(
            0,
            "★ 缺鍵在 work 執行之前就被擋掉——模組層那個同名的 400 經由 HTTP 走不到（見 docs/33 §1）。");
    }

    [Fact(DisplayName = "BE-37 契約②：key 超過 255 字元 → 400 platform.idempotency-key-too-long")]
    public async Task Over_long_key_returns_400_with_the_contract_code()
    {
        var store = new InMemoryIdempotencyStore();
        var context = NewContext(new string('k', 256));

        var result = await BffHttp.ExecuteIdempotentAsync(
            context,
            store,
            Scope,
            new { Payload = "x" },
            _ => Task.FromResult(Result<string>.Success("不該跑到這裡")),
            StatusCodes.Status201Created,
            TestContext.Current.CancellationToken);

        var problem = await ReadProblemAsync(result, context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        problem.Status.ShouldBe(StatusCodes.Status400BadRequest);
        problem.Code.ShouldBe(
            "platform.idempotency-key-too-long",
            "契約表裡沒有這一列，它是 400 的子類；docs/05-API契約.md §4 表格底下有記。");
        problem.Type.ShouldBe("https://greygray.tw/errors/platform.idempotency-key-too-long");
    }

    [Fact(DisplayName = "BE-37 契約③：同一把 key 仍在處理中 → 409 platform.request-in-flight")]
    public async Task In_flight_key_returns_409_with_the_contract_code()
    {
        var store = new InMemoryIdempotencyStore();
        var payload = new { Payload = "same" };
        ProblemBody? observed = null;
        var observedStatus = 0;

        // 「仍在處理中」在這裡是真的發生，不是 stub 出來的：趁第一次的 work 還沒回來，
        // 就拿同一把 key、同一份 payload 再送一次——冪等鍵此刻正是 IN_FLIGHT。
        var first = NewContext(Key);
        await BffHttp.ExecuteIdempotentAsync(
            first,
            store,
            Scope,
            payload,
            async token =>
            {
                var retry = NewContext(Key);
                var retried = await BffHttp.ExecuteIdempotentAsync(
                    retry,
                    store,
                    Scope,
                    payload,
                    _ => Task.FromResult(Result<string>.Success("不該跑到這裡")),
                    StatusCodes.Status201Created,
                    token);
                observed = await ReadProblemAsync(retried, retry);
                observedStatus = retry.Response.StatusCode;
                return Result<string>.Success("第一次的結果");
            },
            StatusCodes.Status201Created,
            TestContext.Current.CancellationToken);

        observedStatus.ShouldBe(StatusCodes.Status409Conflict);
        observed.ShouldNotBeNull();
        observed!.Status.ShouldBe(StatusCodes.Status409Conflict);
        observed.Code.ShouldBe(
            "platform.request-in-flight",
            "★ 前端 problem.ts 的 isInFlight 就是比對這一個字串，它決定 409 要不要用同一把 key 自動重試。");
        observed.Type.ShouldBe("https://greygray.tw/errors/platform.request-in-flight");
    }

    [Fact(DisplayName = "BE-37 契約④：同一把 key 換了 payload → 422 platform.idempotency-key-reused")]
    public async Task Key_reused_with_a_different_payload_returns_422_with_the_contract_code()
    {
        var store = new InMemoryIdempotencyStore();

        var first = NewContext(Key);
        var created = await BffHttp.ExecuteIdempotentAsync(
            first,
            store,
            Scope,
            new { Payload = "第一次的內容" },
            _ => Task.FromResult(Result<string>.Success("第一次的結果")),
            StatusCodes.Status201Created,
            TestContext.Current.CancellationToken);

        // IResult 要 ExecuteAsync 過才會把狀態碼寫進 Response，直接讀是預設的 200。
        await created.ExecuteAsync(first);
        first.Response.StatusCode.ShouldBe(
            StatusCodes.Status201Created,
            "第一次要真的成功，冪等鍵才會收成 COMPLETED——不然第二次比到的是別的狀態。");

        var second = NewContext(Key);
        var result = await BffHttp.ExecuteIdempotentAsync(
            second,
            store,
            Scope,
            new { Payload = "換過的內容" },
            _ => Task.FromResult(Result<string>.Success("不該跑到這裡")),
            StatusCodes.Status201Created,
            TestContext.Current.CancellationToken);

        var problem = await ReadProblemAsync(result, second);
        second.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        problem.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        problem.Code.ShouldBe("platform.idempotency-key-reused");
        problem.Type.ShouldBe("https://greygray.tw/errors/platform.idempotency-key-reused");
    }

    private static async Task<ProblemBody> ReadProblemAsync(IResult result, DefaultHttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        return new ProblemBody(
            json.RootElement.GetProperty("code").GetString() ?? string.Empty,
            json.RootElement.GetProperty("status").GetInt32(),
            json.RootElement.GetProperty("type").GetString() ?? string.Empty);
    }

    private static DefaultHttpContext NewContext(string? idempotencyKey)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        if (idempotencyKey is not null)
        {
            context.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed record ProblemBody(string Code, int Status, string Type);

    /// <summary>
    /// 記憶體冪等儲存，語意比照 <c>IdempotencyStore</c>：
    /// 先比 request hash（不同就是 <c>KeyReusedWithDifferentPayload</c>），再看狀態。
    /// </summary>
    private sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<(string Key, string Scope), Entry> _entries = [];

        public Task<(IdempotencyOutcome Outcome, string? cachedResponse)> TryBeginAsync(
            string key,
            string scope,
            string requestHash,
            CancellationToken cancellationToken)
        {
            var identity = (key, scope);
            if (!_entries.TryGetValue(identity, out var entry))
            {
                _entries[identity] = new Entry(requestHash, EntryStatus.InFlight, null);
                return Task.FromResult((IdempotencyOutcome.Proceed, (string?)null));
            }

            if (!StringComparer.Ordinal.Equals(entry.RequestHash, requestHash))
            {
                return Task.FromResult(
                    (IdempotencyOutcome.KeyReusedWithDifferentPayload, (string?)null));
            }

            switch (entry.Status)
            {
                case EntryStatus.Abandoned:
                    _entries[identity] = entry with { Status = EntryStatus.InFlight, Response = null };
                    return Task.FromResult((IdempotencyOutcome.Proceed, (string?)null));
                case EntryStatus.Completed:
                    return Task.FromResult((IdempotencyOutcome.AlreadyCompleted, entry.Response));
                default:
                    return Task.FromResult((IdempotencyOutcome.InFlight, (string?)null));
            }
        }

        public Task CompleteAsync(
            string key,
            string scope,
            string responseSnapshot,
            CancellationToken cancellationToken)
        {
            var entry = _entries[(key, scope)];
            _entries[(key, scope)] = entry with
            {
                Status = EntryStatus.Completed,
                Response = responseSnapshot,
            };
            return Task.CompletedTask;
        }

        public Task AbandonAsync(string key, string scope, CancellationToken cancellationToken)
        {
            if (_entries.TryGetValue((key, scope), out var entry))
            {
                _entries[(key, scope)] = entry with
                {
                    Status = EntryStatus.Abandoned,
                    Response = null,
                };
            }

            return Task.CompletedTask;
        }

        private enum EntryStatus
        {
            InFlight,
            Completed,
            Abandoned,
        }

        private sealed record Entry(string RequestHash, EntryStatus Status, string? Response);
    }
}
