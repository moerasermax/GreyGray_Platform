using System.Text.Json.Serialization;

namespace GreyGray.Modules.CustomerService.Contracts;

/// <summary>
/// <c>EventTypeRegistry</c> 要求每個 <c>*.Contracts</c> 組件都要有一個這個名字的
/// <see cref="JsonSerializerContext"/>，即使像這個模組一樣<b>目前沒有任何整合事件</b>——
/// 工單建立與結案都是同步呼叫，不經 outbox。
/// </summary>
/// <remarks>
/// source generator 沒有 <c>[JsonSerializable]</c> 就不會產生 <see cref="JsonSerializerContext"/>
/// 抽象成員的實作（<c>GetTypeInfo</c>／<c>GeneratedSerializerOptions</c>），編不過；
/// <c>EventTypeRegistry</c> 只會為<b>真的實作 <c>IIntegrationEvent</c></b> 的型別呼叫
/// <c>GetTypeInfo</c>，所以掛一個不會被用到的私有標記型別純粹是為了讓產生器有東西可產生，
/// 等哪天真的要發事件時直接換掉。
/// </remarks>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(NoIntegrationEventsYet))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;

/// <summary>
/// 只給 <see cref="IntegrationEventJsonContext"/> 的 source generator 佔位用；
/// 必須是 <c>public</c>，否則產生的 <c>JsonTypeInfo&lt;T&gt;</c> 屬性存取範圍會跟
/// 外層的 public context 不一致，編不過。
/// </summary>
public sealed record NoIntegrationEventsYet;
