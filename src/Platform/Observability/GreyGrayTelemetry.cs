using System.Diagnostics;

namespace GreyGray.Platform.Observability;

/// <summary>GreyGray 自有 span 的唯一 <see cref="ActivitySource"/> 與 outbox 傳播工具。</summary>
public static class GreyGrayTelemetry
{
    public const string ActivitySourceName = "GreyGray.Platform";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>強制整個行程使用 W3C Activity 識別格式。</summary>
    public static void ConfigureW3CActivityIds()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
    }

    /// <summary>建立「寫入 outbox」的 producer span。</summary>
    public static Activity? StartProducerActivity(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        Activity? activity = ActivitySource.StartActivity(
            $"outbox publish {eventType}",
            ActivityKind.Producer);

        SetOutboxTags(activity, eventType, "publish");
        return activity;
    }

    /// <summary>
    /// 以 outbox 儲存的 TraceId 與父 SpanId 建立 consumer span，讓 trace 跨越資料庫佇列後保持連續。
    /// </summary>
    public static Activity? StartConsumerActivity(
        string eventType,
        string correlationId,
        string? causationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ValidateTraceId(correlationId);

        string parentSpanId;
        bool hasSyntheticParent = causationId is null;
        if (hasSyntheticParent)
        {
            // W3C ActivityContext 無法只指定 TraceId。舊資料若沒有 causation_id，
            // 用一個不會被匯出的合成父 span 保住 trace，並以 tag 明確留下診斷訊號。
            parentSpanId = ActivitySpanId.CreateRandom().ToHexString();
        }
        else
        {
            ValidateSpanId(causationId!);
            parentSpanId = causationId!;
        }

        string traceParent = $"00-{correlationId}-{parentSpanId}-01";

        if (!ActivityContext.TryParse(traceParent, null, out ActivityContext parsedParent))
        {
            throw new ArgumentException("無法從 correlationId 與 causationId 還原 W3C Activity 上下文。");
        }

        var remoteParent = new ActivityContext(
            parsedParent.TraceId,
            parsedParent.SpanId,
            parsedParent.TraceFlags,
            parsedParent.TraceState,
            isRemote: true);

        Activity? activity = ActivitySource.StartActivity(
            $"outbox process {eventType}",
            ActivityKind.Consumer,
            remoteParent);

        SetOutboxTags(activity, eventType, "process");
        activity?.SetTag("greygray.outbox.missing_causation_id", hasSyntheticParent);
        return activity;
    }

    internal static void ValidateTraceId(string traceId)
    {
        if (!IsLowerHexIdentifier(traceId, 32))
        {
            throw new ArgumentException(
                "correlationId 必須是 32 位、非全零的小寫十六進位 W3C TraceId。",
                nameof(traceId));
        }
    }

    internal static void ValidateSpanId(string spanId)
    {
        if (!IsLowerHexIdentifier(spanId, 16))
        {
            throw new ArgumentException(
                "causationId 必須是 16 位、非全零的小寫十六進位 W3C SpanId。",
                nameof(spanId));
        }
    }

    private static bool IsLowerHexIdentifier(string? value, int expectedLength)
    {
        if (value is null || value.Length != expectedLength)
        {
            return false;
        }

        bool hasNonZeroDigit = false;
        foreach (char character in value)
        {
            bool isDigit = character is >= '0' and <= '9';
            bool isLowerHexLetter = character is >= 'a' and <= 'f';
            if (!isDigit && !isLowerHexLetter)
            {
                return false;
            }

            hasNonZeroDigit |= character != '0';
        }

        return hasNonZeroDigit;
    }

    private static void SetOutboxTags(Activity? activity, string eventType, string operation)
    {
        activity?.SetTag("messaging.system", "greygray.outbox");
        activity?.SetTag("messaging.destination.name", "platform.outbox_message");
        activity?.SetTag("messaging.operation", operation);
        activity?.SetTag("messaging.message.type", eventType);
    }
}
