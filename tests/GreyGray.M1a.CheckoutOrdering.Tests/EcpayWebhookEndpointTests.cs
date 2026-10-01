using GreyGray.Api.Storefront;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using GreyGray.Tools.EcpaySimulator.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class EcpayWebhookEndpointTests
{
    private const string MerchantId = "DEVFAKE0000";
    private const string HashKey = "DEVFAKEHASHKEY01";
    private const string HashIv = "DEVFAKEHASHIV001";
    private const string MerchantTradeNo = "GG0000000000000000A1";
    private const string TradeNo = "DEVFAKE2609301200001";
    private const string PaymentResultScope = "webhook:ecpay:payment-result";
    private const string PaymentInfoScope = "webhook:ecpay:payment-info";

    [Fact(DisplayName = "D1：Payment 組合根把驗簽器與命令註冊成同一個 scoped 實例")]
    public void Verifier_and_command_resolve_to_the_same_scoped_instance()
    {
        using var harness = VerifierHarness.Create();

        var verifier = harness.Scope.ServiceProvider.GetRequiredService<IEcpayCallbackVerifier>();
        var command = harness.Scope.ServiceProvider.GetRequiredService<IPaymentCommand>();

        verifier.ShouldBeSameAs(command);
    }

    [Fact(DisplayName = "W1：偽造通知先到不會擋住後續正式簽章通知")]
    public async Task Forged_notification_does_not_block_the_real_notification()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var command = new RecordingPaymentCommand();
        var forged = SignedNotification();
        forged["TradeAmt"] = "1";

        var forgedResponse = await SendAsync(forged, harness.Verifier, command, store);
        var realResponse = await SendAsync(SignedNotification(), harness.Verifier, command, store);

        forgedResponse.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        forgedResponse.Body.ShouldContain("payment.invalid-signature");
        realResponse.ShouldBe((StatusCodes.Status200OK, "1|OK"));
        command.CallbackCalls.ShouldBe(1);
        store.TryBeginCalls.ShouldBe(1);
    }

    [Fact(DisplayName = "W2：驗簽失敗時冪等儲存零筆且 TryBegin 零呼叫")]
    public async Task Invalid_signature_does_not_touch_idempotency_storage()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var command = new RecordingPaymentCommand();
        var forged = SignedNotification();
        forged["TradeAmt"] = "1";

        var response = await SendAsync(forged, harness.Verifier, command, store);

        response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        response.Body.ShouldContain("payment.invalid-signature");
        store.EntryCount.ShouldBe(0);
        store.TryBeginCalls.ShouldBe(0);
        command.CallbackCalls.ShouldBe(0);
    }

    [Fact(DisplayName = "W3：正式通知重送走 AlreadyCompleted 且服務只呼叫一次")]
    public async Task Duplicate_valid_notification_uses_the_completed_entry()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var command = new RecordingPaymentCommand();
        var notification = SignedNotification();

        var first = await SendAsync(notification, harness.Verifier, command, store);
        var second = await SendAsync(notification, harness.Verifier, command, store);

        first.ShouldBe((StatusCodes.Status200OK, "1|OK"));
        second.ShouldBe((StatusCodes.Status200OK, "1|OK"));
        command.CallbackCalls.ShouldBe(1);
        store.StatusOf(EventKey("1"), PaymentResultScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Completed);
    }

    [Theory(DisplayName = "W4：必要身分欄位缺漏或 MerchantID 不符都不寫冪等")]
    [InlineData("MerchantTradeNo")]
    [InlineData("TradeNo")]
    [InlineData("MerchantID")]
    public async Task Invalid_callback_identity_does_not_touch_idempotency_storage(string invalidField)
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var command = new RecordingPaymentCommand();
        var notification = SignedNotification();
        if (invalidField == "MerchantID")
        {
            notification[invalidField] = "WRONG";
        }
        else
        {
            notification.Remove(invalidField);
        }

        Resign(notification);
        var response = await SendAsync(notification, harness.Verifier, command, store);

        response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        response.Body.ShouldContain("payment.invalid-callback");
        store.EntryCount.ShouldBe(0);
        store.TryBeginCalls.ShouldBe(0);
        command.CallbackCalls.ShouldBe(0);
    }

    [Fact(DisplayName = "W5：付款結果使用事件 scope 與三欄複合 key")]
    public async Task Valid_notification_uses_the_payment_result_scope_and_event_key()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();

        await SendAsync(SignedNotification(), harness.Verifier, new RecordingPaymentCommand(), store);

        store.Identities.ShouldHaveSingleItem().ShouldBe((EventKey("1"), PaymentResultScope));
    }

    [Fact(DisplayName = "W6：舊 scope 的 poisoned row 不會擋住正式通知")]
    public async Task Old_scope_entry_does_not_block_the_new_payment_result_scope()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        await store.TryBeginAsync(
            MerchantTradeNo,
            "webhook:ecpay",
            "different-hash",
            TestContext.Current.CancellationToken);
        await store.AbandonAsync(
            MerchantTradeNo,
            "webhook:ecpay",
            TestContext.Current.CancellationToken);
        var command = new RecordingPaymentCommand();

        var response = await SendAsync(SignedNotification(), harness.Verifier, command, store);

        response.ShouldBe((StatusCodes.Status200OK, "1|OK"));
        command.CallbackCalls.ShouldBe(1);
        store.StatusOf(EventKey("1"), PaymentResultScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Completed);
    }

    [Fact(DisplayName = "W7：服務失敗會 Abandon，同 hash 重送可重新認領")]
    public async Task Failed_service_call_is_abandoned_and_can_be_retried()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var command = new RecordingPaymentCommand
        {
            Result = Result.Failure("payment.stale-callback", "通知已逾時。"),
        };
        var notification = SignedNotification();

        var first = await SendAsync(notification, harness.Verifier, command, store);
        var second = await SendAsync(notification, harness.Verifier, command, store);

        first.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        second.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        command.CallbackCalls.ShouldBe(2);
        store.StatusOf(EventKey("1"), PaymentResultScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Abandoned);
    }

    [Fact(DisplayName = "W8：服務丟例外時先 Abandon 再往外拋")]
    public async Task Service_exception_is_abandoned_and_rethrown()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var command = new RecordingPaymentCommand { Exception = new InvalidOperationException("boom") };

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => SendAsync(SignedNotification(), harness.Verifier, command, store));

        exception.Message.ShouldBe("boom");
        store.StatusOf(EventKey("1"), PaymentResultScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Abandoned);
    }

    [Fact(DisplayName = "W9：同交易編號的失敗與成功通知使用兩把鍵")]
    public async Task Failure_then_success_for_the_same_trade_number_are_distinct_events()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var command = new RecordingPaymentCommand();

        var failed = await SendAsync(
            SignedNotification(EcpaySimulatorCore.FailureRtnCode),
            harness.Verifier,
            command,
            store);
        var succeeded = await SendAsync(SignedNotification(), harness.Verifier, command, store);

        failed.ShouldBe((StatusCodes.Status200OK, "1|OK"));
        succeeded.ShouldBe((StatusCodes.Status200OK, "1|OK"));
        command.CallbackCalls.ShouldBe(2);
        store.Identities.ShouldContain((EventKey(EcpaySimulatorCore.FailureRtnCode), PaymentResultScope));
        store.Identities.ShouldContain((EventKey("1"), PaymentResultScope));
    }

    [Fact(DisplayName = "W10：同事件多一個已驗簽無關欄位仍走 AlreadyCompleted")]
    public async Task Extra_signed_field_does_not_change_the_event_hash()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var command = new RecordingPaymentCommand();
        var first = SignedNotification();
        var replay = SignedNotification();
        replay["CustomField1"] = "later-added";
        Resign(replay);

        var firstResponse = await SendAsync(first, harness.Verifier, command, store);
        var replayResponse = await SendAsync(replay, harness.Verifier, command, store);

        firstResponse.ShouldBe((StatusCodes.Status200OK, "1|OK"));
        replayResponse.ShouldBe((StatusCodes.Status200OK, "1|OK"));
        command.CallbackCalls.ShouldBe(1);
        store.EntryCount.ShouldBe(1);
    }

    [Fact(DisplayName = "P1：取號通知驗簽失敗時不碰冪等與服務")]
    public async Task Payment_info_invalid_signature_does_not_touch_idempotency_or_service()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var handler = new RecordingPaymentInfoHandler();
        var forged = SignedNotification();
        forged["TradeAmt"] = "1";

        var response = await SendPaymentInfoAsync(forged, harness.Verifier, handler, store);

        response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        response.Body.ShouldContain("payment.invalid-signature");
        store.TryBeginCalls.ShouldBe(0);
        store.EntryCount.ShouldBe(0);
        handler.Calls.ShouldBe(0);
    }

    [Fact(DisplayName = "P2：取號通知成功使用原始表單、事件鍵與獨立 scope，並完成 1|OK")]
    public async Task Payment_info_success_completes_plain_text_acknowledgement()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var handler = new RecordingPaymentInfoHandler();
        var notification = SignedNotification();

        var response = await SendPaymentInfoAsync(notification, harness.Verifier, handler, store);

        response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        response.Body.ShouldBe("1|OK");
        response.ContentType.ShouldStartWith("text/plain");
        handler.Calls.ShouldBe(1);
        handler.ReceivedFields.ShouldBeSameAs(notification);
        store.Identities.ShouldHaveSingleItem().ShouldBe((EventKey("1"), PaymentInfoScope));
        store.CompleteCalls.ShouldBe(1);
        store.LastCompletedResponse.ShouldBe("1|OK");
    }

    [Theory(DisplayName = "P3：取號通知三種冪等早退結果不呼叫服務")]
    [InlineData(IdempotencyOutcome.AlreadyCompleted, StatusCodes.Status200OK, "1|OK")]
    [InlineData(IdempotencyOutcome.InFlight, StatusCodes.Status409Conflict, "")]
    [InlineData(IdempotencyOutcome.KeyReusedWithDifferentPayload, StatusCodes.Status422UnprocessableEntity, "payment.callback-payload-mismatch")]
    public async Task Payment_info_idempotency_early_results_skip_service(
        IdempotencyOutcome outcome,
        int expectedStatus,
        string expectedBody)
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore { ForcedOutcome = outcome };
        var handler = new RecordingPaymentInfoHandler();

        var response = await SendPaymentInfoAsync(SignedNotification(), harness.Verifier, handler, store);

        response.StatusCode.ShouldBe(expectedStatus);
        response.Body.ShouldContain(expectedBody);
        handler.Calls.ShouldBe(0);
        store.CompleteCalls.ShouldBe(0);
        store.AbandonCalls.ShouldBe(0);
    }

    [Fact(DisplayName = "P4a：取號服務失敗先 Abandon，concurrent-update 回 409")]
    public async Task Payment_info_failure_is_abandoned_and_mapped_to_conflict()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var handler = new RecordingPaymentInfoHandler
        {
            Result = Result.Failure("payment.concurrent-update", "付款資料同時更新。"),
        };

        var response = await SendPaymentInfoAsync(SignedNotification(), harness.Verifier, handler, store);

        response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        response.Body.ShouldContain("payment.concurrent-update");
        store.AbandonCalls.ShouldBe(1);
        store.StatusOf(EventKey("1"), PaymentInfoScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Abandoned);
    }

    [Fact(DisplayName = "P4b：取號服務例外用 CancellationToken.None Abandon 後原樣拋出")]
    public async Task Payment_info_exception_is_abandoned_without_request_cancellation()
    {
        using var harness = VerifierHarness.Create();
        var store = new CountingIdempotencyStore();
        var handler = new RecordingPaymentInfoHandler { Exception = new InvalidOperationException("payment-info-boom") };

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => SendPaymentInfoAsync(SignedNotification(), harness.Verifier, handler, store));

        exception.Message.ShouldBe("payment-info-boom");
        store.AbandonCalls.ShouldBe(1);
        store.LastAbandonCancellationToken.ShouldBe(CancellationToken.None);
        store.StatusOf(EventKey("1"), PaymentInfoScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Abandoned);
    }

    [Fact(DisplayName = "P5：付款結果與取號通知同 key 雙向互不阻擋")]
    public async Task Payment_result_and_payment_info_scopes_are_independent_in_both_orders()
    {
        using var harness = VerifierHarness.Create();
        var notification = SignedNotification();

        var resultFirstStore = new CountingIdempotencyStore();
        var resultCommand = new RecordingPaymentCommand();
        var infoHandler = new RecordingPaymentInfoHandler();
        await SendAsync(notification, harness.Verifier, resultCommand, resultFirstStore);
        await SendPaymentInfoAsync(notification, harness.Verifier, infoHandler, resultFirstStore);

        resultCommand.CallbackCalls.ShouldBe(1);
        infoHandler.Calls.ShouldBe(1);
        resultFirstStore.Identities.ShouldContain((EventKey("1"), PaymentResultScope));
        resultFirstStore.Identities.ShouldContain((EventKey("1"), PaymentInfoScope));

        var infoFirstStore = new CountingIdempotencyStore();
        resultCommand = new RecordingPaymentCommand();
        infoHandler = new RecordingPaymentInfoHandler();
        await SendPaymentInfoAsync(notification, harness.Verifier, infoHandler, infoFirstStore);
        await SendAsync(notification, harness.Verifier, resultCommand, infoFirstStore);

        infoHandler.Calls.ShouldBe(1);
        resultCommand.CallbackCalls.ShouldBe(1);
    }

    private static Dictionary<string, string> SignedNotification(string rtnCode = "1")
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = MerchantId,
            ["MerchantTradeNo"] = MerchantTradeNo,
            ["TradeNo"] = TradeNo,
            ["RtnCode"] = rtnCode,
            ["TradeAmt"] = "160",
        };
        Resign(fields);
        return fields;
    }

    private static void Resign(IDictionary<string, string> fields)
    {
        fields.Remove("CheckMacValue");
        fields["CheckMacValue"] = EcpaySimulatorCore.Sign(
            (IReadOnlyDictionary<string, string>)fields,
            HashKey,
            HashIv);
    }

    private static string EventKey(string rtnCode) => $"{MerchantTradeNo}:{TradeNo}:{rtnCode}";

    private static async Task<(int StatusCode, string Body)> SendAsync(
        IReadOnlyDictionary<string, string> fields,
        IEcpayCallbackVerifier verifier,
        IPaymentCommand command,
        IIdempotencyStore store)
    {
        var result = await M1aEndpoints.HandleEcpayWebhookAsync(
            fields,
            verifier,
            command,
            store,
            TestContext.Current.CancellationToken);
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, body);
    }

    private static async Task<(int StatusCode, string Body, string? ContentType)> SendPaymentInfoAsync(
        IReadOnlyDictionary<string, string> fields,
        IEcpayCallbackVerifier verifier,
        IEcpayPaymentInfoHandler handler,
        IIdempotencyStore store)
    {
        var result = await M1aEndpoints.HandleEcpayPaymentInfoWebhookAsync(
            fields,
            verifier,
            handler,
            store,
            TestContext.Current.CancellationToken);
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, body, context.Response.ContentType);
    }

    private sealed class VerifierHarness(ServiceProvider provider, IServiceScope scope) : IDisposable
    {
        public IServiceScope Scope { get; } = scope;

        public IEcpayCallbackVerifier Verifier =>
            Scope.ServiceProvider.GetRequiredService<IEcpayCallbackVerifier>();

        public static VerifierHarness Create()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:GreyGray_payment"] =
                        "Host=127.0.0.1;Database=model;Username=model;Password=model",
                    ["Payment:ECPay:MerchantId"] = MerchantId,
                    ["Payment:ECPay:HashKey"] = HashKey,
                    ["Payment:ECPay:HashIV"] = HashIv,
                    ["Payment:ECPay:CheckoutUrl"] =
                        "https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5",
                    ["Payment:ECPay:CreditDetailUrl"] =
                        "https://payment-stage.ecpay.com.tw/CreditDetail/DoAction",
                })
                .Build();
            var services = new ServiceCollection();
            services.AddSingleton<IClock>(new FakeClock(new DateTimeOffset(2026, 9, 30, 4, 0, 0, TimeSpan.Zero)));
            services.AddSingleton<ICorrelationContext>(new FakeCorrelation());
            services.AddPaymentModule(configuration);
            var provider = services.BuildServiceProvider();
            return new VerifierHarness(provider, provider.CreateScope());
        }

        public void Dispose()
        {
            Scope.Dispose();
            provider.Dispose();
        }
    }

    private sealed class RecordingPaymentCommand : IPaymentCommand
    {
        public int CallbackCalls { get; private set; }

        public Result Result { get; init; } = Result.Success();

        public Exception? Exception { get; init; }

        public Task<Result<PaymentInitiation>> InitiateAsync(
            PaymentInitiationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> HandleEcpayCallbackAsync(
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken)
        {
            CallbackCalls++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingPaymentInfoHandler : IEcpayPaymentInfoHandler
    {
        public int Calls { get; private set; }

        public IReadOnlyDictionary<string, string>? ReceivedFields { get; private set; }

        public Result Result { get; init; } = Result.Success();

        public Exception? Exception { get; init; }

        public Task<Result> HandleEcpayPaymentInfoAsync(
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedFields = fields;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class CountingIdempotencyStore : IIdempotencyStore
    {
        private readonly InspectableIdempotencyStore _inner = new();
        private readonly HashSet<(string Key, string Scope)> _identities = [];

        public int TryBeginCalls { get; private set; }

        public int CompleteCalls { get; private set; }

        public int AbandonCalls { get; private set; }

        public IdempotencyOutcome? ForcedOutcome { get; init; }

        public string? LastCompletedResponse { get; private set; }

        public CancellationToken LastAbandonCancellationToken { get; private set; }

        public int EntryCount => _identities.Count;

        public IReadOnlyCollection<(string Key, string Scope)> Identities => _identities;

        public InspectableIdempotencyStore.EntryStatus? StatusOf(string key, string scope) =>
            _inner.StatusOf(key, scope);

        public async Task<(IdempotencyOutcome Outcome, string? cachedResponse)> TryBeginAsync(
            string key,
            string scope,
            string requestHash,
            CancellationToken cancellationToken)
        {
            TryBeginCalls++;
            if (ForcedOutcome is { } forcedOutcome)
            {
                return (forcedOutcome, forcedOutcome == IdempotencyOutcome.AlreadyCompleted ? "1|OK" : null);
            }

            var result = await _inner.TryBeginAsync(key, scope, requestHash, cancellationToken);
            if (result.Outcome == IdempotencyOutcome.Proceed)
            {
                _identities.Add((key, scope));
            }

            return result;
        }

        public Task CompleteAsync(
            string key,
            string scope,
            string responseSnapshot,
            CancellationToken cancellationToken)
        {
            CompleteCalls++;
            LastCompletedResponse = responseSnapshot;
            return _inner.CompleteAsync(key, scope, responseSnapshot, cancellationToken);
        }

        public Task AbandonAsync(
            string key,
            string scope,
            CancellationToken cancellationToken)
        {
            AbandonCalls++;
            LastAbandonCancellationToken = cancellationToken;
            return _inner.AbandonAsync(key, scope, cancellationToken);
        }
    }
}
