using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.Platform.Tests;

/// <summary>
/// BE-35：<see cref="BffHttp.ExecuteIdempotentAsync{TState, TResponse}"/> 兩階段多載的四條語意。
/// </summary>
/// <remarks>
/// <para>
/// 這個多載存在的理由是「現在卡在哪 #22」那一整個家族：<b>副作用已經 commit，之後才在組回應
/// 那一步失敗，於是冪等鍵被 abandon、客人拿到錯誤</b>。舊多載分不出這兩段，所以只要拿到
/// <c>Result.Failure</c> 或例外就一律 <c>AbandonAsync</c>。
/// </para>
/// <para>
/// 四條語意逐條測：① <c>work</c> 失敗 → abandon ＋ Problem；② <c>work</c> 成功之後
/// <b>絕不</b> abandon；③ <c>render</c> 丟例外 → 仍 <c>CompleteAsync</c> 再往上丟；
/// ④ 一切正常 → <c>CompleteAsync</c> ＋ 重送直接回快取。
/// </para>
/// <para>
/// 這一組刻意不用 Testcontainers：要釘的是 <see cref="BffHttp"/> 的控制流，
/// 不是 <c>IdempotencyStore</c> 的 SQL（那已經由 <see cref="IdempotencyAndSagaTests"/> 守著）。
/// </para>
/// </remarks>
public sealed class BffHttpTwoPhaseIdempotencyTests
{
    private const string Scope = "test:two-phase";

    [Fact(DisplayName = "BE-35 語意①：work 失敗時 abandon 冪等鍵並回 Problem")]
    public async Task Work_failure_abandons_the_key_and_returns_a_problem()
    {
        var store = new RecordingIdempotencyStore();
        var context = NewContext("key-1");
        var renderCalls = 0;

        var result = await BffHttp.ExecuteIdempotentAsync(
            context,
            store,
            Scope,
            new { Payload = "x" },
            _ => Task.FromResult(Result<string>.Failure("test.rejected", "業務規則擋下來了。")),
            (string state, CancellationToken _) =>
            {
                renderCalls++;
                return Task.FromResult(state);
            },
            StatusCodes.Status201Created,
            TestContext.Current.CancellationToken);

        var body = await ReadAsync(result, context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        body.ShouldContain("test.rejected");
        renderCalls.ShouldBe(0, "work 失敗就不該進到組回應。");
        store.AbandonCalls.ShouldBe(1, "副作用還沒產生，abandon 讓同一把 key 的重試走得回來。");
        store.CompleteCalls.ShouldBe(0);
        store.StatusOf("key-1", Scope).ShouldBe(RecordingIdempotencyStore.EntryStatus.Abandoned);
    }

    [Fact(DisplayName = "BE-35 語意②：work 成功之後就算 render 炸掉也絕不 abandon")]
    public async Task Committed_work_is_never_abandoned_even_when_render_throws()
    {
        var store = new RecordingIdempotencyStore();
        var context = NewContext("key-1");

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await BffHttp.ExecuteIdempotentAsync(
                context,
                store,
                Scope,
                new { Payload = "x" },
                _ => Task.FromResult(Result<string>.Success("已經 commit 的副作用")),
                (string _, CancellationToken _) =>
                    Task.FromException<string>(new InvalidOperationException("組回應炸了")),
                StatusCodes.Status201Created,
                TestContext.Current.CancellationToken));

        store.AbandonCalls.ShouldBe(
            0,
            "★ 這是整個 BE-35 的核心：副作用已經產生，abandon 等於把冪等保護解除。");
        store.StatusOf("key-1", Scope)
            .ShouldNotBe(RecordingIdempotencyStore.EntryStatus.Abandoned);
    }

    [Fact(DisplayName = "BE-35 語意③：render 丟例外時仍 CompleteAsync 收尾，重送不會再跑一次 work")]
    public async Task Render_exception_still_completes_the_key_before_rethrowing()
    {
        var store = new RecordingIdempotencyStore();
        var workCalls = 0;

        Task<Result<string>> Work(CancellationToken _)
        {
            workCalls++;
            return Task.FromResult(Result<string>.Success("已經 commit 的副作用"));
        }

        var first = NewContext("key-1");
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await BffHttp.ExecuteIdempotentAsync(
                first,
                store,
                Scope,
                new { Payload = "x" },
                Work,
                (string _, CancellationToken _) =>
                    Task.FromException<string>(new InvalidOperationException("組回應炸了")),
                StatusCodes.Status201Created,
                TestContext.Current.CancellationToken));

        store.CompleteCalls.ShouldBe(1, "冪等鍵要被收成 COMPLETED，不能留在 IN_FLIGHT。");
        store.SnapshotOf("key-1", Scope).ShouldBe(
            "null",
            "組回應炸了就不存在正確的回應可以存；退化成 JSON null 會讓前端立刻炸開，" +
            "不會像 {} 那樣被當成一個所有欄位都缺席的正常物件而靜默錯下去。");

        // 重送同一把 key：work 不可以再跑一次——副作用已經產生了。
        var second = NewContext("key-1");
        var replay = await BffHttp.ExecuteIdempotentAsync(
            second,
            store,
            Scope,
            new { Payload = "x" },
            Work,
            (string state, CancellationToken _) => Task.FromResult(state),
            StatusCodes.Status201Created,
            TestContext.Current.CancellationToken);

        await ReadAsync(replay, second);
        workCalls.ShouldBe(1, "★ abandon 掉的話這裡會是 2，副作用就被做了第二次。");
    }

    [Fact(DisplayName = "BE-35 語意④：一切正常時 CompleteAsync 存快照，重送直接回快取")]
    public async Task Successful_render_completes_the_key_and_replays_from_cache()
    {
        var store = new RecordingIdempotencyStore();
        var workCalls = 0;
        var renderCalls = 0;

        Task<Result<string>> Work(CancellationToken _)
        {
            workCalls++;
            return Task.FromResult(Result<string>.Success("訂單"));
        }

        Task<Response> Render(string state, CancellationToken _)
        {
            renderCalls++;
            return Task.FromResult(new Response(state, 42));
        }

        var first = NewContext("key-1");
        var body = await ReadAsync(
            await BffHttp.ExecuteIdempotentAsync(
                first,
                store,
                Scope,
                new { Payload = "x" },
                Work,
                Render,
                StatusCodes.Status201Created,
                TestContext.Current.CancellationToken),
            first);

        first.Response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        body.ShouldBe(
            "{\"name\":\"訂單\",\"quantity\":42}",
            "回應快照就是 GreyGrayJson 序列化的結果：camelCase、CJK 不轉義。");
        store.CompleteCalls.ShouldBe(1);
        store.AbandonCalls.ShouldBe(0);

        var second = NewContext("key-1");
        var replayBody = await ReadAsync(
            await BffHttp.ExecuteIdempotentAsync(
                second,
                store,
                Scope,
                new { Payload = "x" },
                Work,
                Render,
                StatusCodes.Status201Created,
                TestContext.Current.CancellationToken),
            second);

        second.Response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        replayBody.ShouldBe(body, "重送要拿到一模一樣的快照。");
        workCalls.ShouldBe(1, "第二次連 work 都不該進去。");
        renderCalls.ShouldBe(1);
    }

    private static async Task<string> ReadAsync(IResult result, DefaultHttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private static DefaultHttpContext NewContext(string idempotencyKey)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Request.Headers["Idempotency-Key"] = idempotencyKey;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed record Response(string Name, int Quantity);

    /// <summary>
    /// 記憶體冪等儲存，語意比照 <c>IdempotencyStore</c>，另外把
    /// <c>CompleteAsync</c>／<c>AbandonAsync</c> 被呼叫幾次記下來——
    /// 「不准 abandon」這條不變式要能直接斷言，不能只看最後狀態。
    /// </summary>
    private sealed class RecordingIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<(string Key, string Scope), Entry> _entries = [];

        internal enum EntryStatus
        {
            InFlight,
            Completed,
            Abandoned,
        }

        public int CompleteCalls { get; private set; }

        public int AbandonCalls { get; private set; }

        public EntryStatus? StatusOf(string key, string scope) =>
            _entries.TryGetValue((key, scope), out var entry) ? entry.Status : null;

        public string? SnapshotOf(string key, string scope) =>
            _entries.TryGetValue((key, scope), out var entry) ? entry.Response : null;

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
            CompleteCalls++;
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
            AbandonCalls++;
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

        private sealed record Entry(string RequestHash, EntryStatus Status, string? Response);
    }
}
