using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GreyGray.Platform.Modules;

/// <summary>
/// 每個模組的 <c>*.Infra</c> 組件<b>只</b>對外公開一個這個介面的實作，作為組合根的接縫。
/// </summary>
/// <remarks>
/// 這是 Host 唯一被允許參考 <c>*.Infra</c> 的理由。Host 不得參考任何 <c>*.Core</c>，
/// 由 <c>tests/GreyGray.Architecture.Tests</c> 斷言（違規 build fail）。
/// <para>
/// 實作範例：<c>internal sealed class OrderingModule : IModuleRegistration</c>，
/// 加上一個 <c>public static class OrderingModuleExtensions { public static IServiceCollection AddOrderingModule(...) }</c>。
/// </para>
/// </remarks>
public interface IModuleRegistration
{
    /// <summary>模組名，例如 <c>Ordering</c>。</summary>
    static abstract string ModuleName { get; }

    /// <summary>該模組專屬的 Postgres schema，例如 <c>ordering</c>。</summary>
    static abstract string SchemaName { get; }

    /// <summary>註冊該模組的 DbContext、repository、事件 handler、外部 adapter。</summary>
    static abstract IServiceCollection Register(IServiceCollection services, IConfiguration configuration);
}
