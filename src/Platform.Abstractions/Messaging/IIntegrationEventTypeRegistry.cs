using System.Diagnostics.CodeAnalysis;

namespace GreyGray.Platform.Abstractions.Messaging;

/// <summary>
/// 事件型別名 ↔ CLR 型別的雙向對照。
/// </summary>
/// <remarks>
/// <b>為什麼需要這個：</b><see cref="IIntegrationEvent.EventType"/> 是 <c>static abstract</c>，
/// 只能正向（型別 → 字串）。但 dispatcher 是從 <c>platform.outbox_message.event_type</c>
/// 讀回一個字串，它必須反查回 CLR 型別才能反序列化 payload 並找到 handler。
/// 沒有這個登錄，43 個事件在消費端一個都還原不了。
/// <para>
/// 實作（<c>GreyGray.Platform</c>）在啟動時掃描各模組的 <c>*.Contracts</c> 組件建表，
/// 並在發現重複的 <see cref="IIntegrationEvent.EventType"/> 時<b>直接拋例外</b>——
/// 重複的事件型別名會讓訊息被送到錯的 handler，而且症狀是隨機的。
/// 掃描只在啟動時做一次；每則訊息的序列化仍走 source generator，不走反射。
/// </para>
/// </remarks>
public interface IIntegrationEventTypeRegistry
{
    /// <summary>反查 CLR 型別。查不到就拋——收到不認識的事件型別是設定錯誤，不是業務失敗。</summary>
    Type Resolve(string eventType);

    /// <summary>反查 CLR 型別。給「未知型別要進死信而不是讓 dispatcher 整批停擺」的路徑用。</summary>
    bool TryResolve(string eventType, [NotNullWhen(true)] out Type? clrType);

    /// <summary>正查事件型別名。</summary>
    string EventTypeOf(Type clrType);

    /// <summary>目前登錄到的全部事件型別名。啟動時印出來，跟 <c>docs/02-事件與狀態機.md</c> 對得起來。</summary>
    IReadOnlyCollection<string> KnownEventTypes { get; }
}
