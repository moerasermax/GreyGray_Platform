using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel.Json;

namespace GreyGray.Platform.Messaging;

/// <summary>
/// 啟動時掃描 <c>GreyGray.Modules.*.Contracts</c>，建立穩定事件型別名、CLR 型別與
/// source-generated JSON metadata 的對照。
/// </summary>
public sealed class EventTypeRegistry : IIntegrationEventTypeRegistry
{
    private const string ContractsAssemblyPrefix = "GreyGray.Modules.";
    private const string ContractsAssemblySuffix = ".Contracts";
    private const string ContextTypeName = "IntegrationEventJsonContext";

    private readonly IReadOnlyDictionary<string, Registration> _byEventType;
    private readonly IReadOnlyDictionary<Type, Registration> _byClrType;

    /// <summary>從應用程式目錄載入並掃描全部 Contracts 組件。</summary>
    public EventTypeRegistry()
        : this(DiscoverContractAssemblies())
    {
    }

    /// <summary>掃描指定的 Contracts 組件。主要供組合根與啟動驗證使用。</summary>
    public static EventTypeRegistry FromAssemblies(IEnumerable<Assembly> contractAssemblies) =>
        new(contractAssemblies);

    private EventTypeRegistry(IEnumerable<Assembly> contractAssemblies)
    {
        ArgumentNullException.ThrowIfNull(contractAssemblies);

        var assemblies = contractAssemblies
            .DistinctBy(assembly => assembly.FullName, StringComparer.Ordinal)
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();

        if (assemblies.Length == 0)
        {
            throw new InvalidOperationException(
                "找不到任何 GreyGray.Modules.*.Contracts 組件，無法建立事件型別登錄。");
        }

        var byEventType = new Dictionary<string, Registration>(StringComparer.Ordinal);
        var byClrType = new Dictionary<Type, Registration>();

        foreach (var assembly in assemblies)
        {
            RegisterAssembly(assembly, byEventType, byClrType);
        }

        _byEventType = byEventType;
        _byClrType = byClrType;
        KnownEventTypes = byEventType.Keys
            .OrderBy(eventType => eventType, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyCollection<string> KnownEventTypes { get; }

    public Type Resolve(string eventType)
    {
        if (TryResolve(eventType, out var clrType))
        {
            return clrType;
        }

        throw new KeyNotFoundException($"未登錄的整合事件型別：'{eventType}'。");
    }

    public bool TryResolve(string eventType, [NotNullWhen(true)] out Type? clrType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        if (_byEventType.TryGetValue(eventType, out var registration))
        {
            clrType = registration.ClrType;
            return true;
        }

        clrType = null;
        return false;
    }

    public string EventTypeOf(Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);

        if (_byClrType.TryGetValue(clrType, out var registration))
        {
            return registration.EventType;
        }

        throw new KeyNotFoundException($"未登錄的整合事件 CLR 型別：'{clrType.FullName}'。");
    }

    /// <summary>取得該事件由 Contracts 組件產生的 JSON metadata。</summary>
    public JsonTypeInfo JsonTypeInfoOf(Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);

        if (_byClrType.TryGetValue(clrType, out var registration))
        {
            return registration.JsonTypeInfo;
        }

        throw new KeyNotFoundException($"未登錄的整合事件 CLR 型別：'{clrType.FullName}'。");
    }

    private static void RegisterAssembly(
        Assembly assembly,
        IDictionary<string, Registration> byEventType,
        IDictionary<Type, Registration> byClrType)
    {
        var types = GetLoadableTypes(assembly);
        var eventTypes = types
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(IIntegrationEvent).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        var contextTypes = types
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && type.Name == ContextTypeName
                && typeof(JsonSerializerContext).IsAssignableFrom(type))
            .ToArray();

        if (contextTypes.Length != 1)
        {
            throw new InvalidOperationException(
                $"Contracts 組件 '{assembly.GetName().Name}' 必須恰有一個名為 {ContextTypeName} " +
                $"的 JsonSerializerContext，實際找到 {contextTypes.Length} 個。");
        }

        var context = CreateContext(contextTypes[0]);

        foreach (var clrType in eventTypes)
        {
            var eventType = ReadEventType(clrType);
            var jsonTypeInfo = context.GetTypeInfo(clrType)
                ?? throw new InvalidOperationException(
                    $"{context.GetType().FullName} 沒有為事件 {clrType.FullName} 產生 JSON metadata。");

            var registration = new Registration(eventType, clrType, jsonTypeInfo);

            if (byEventType.TryGetValue(eventType, out var existing))
            {
                throw new InvalidOperationException(
                    $"重複的整合事件型別名 '{eventType}'：" +
                    $"{existing.ClrType.FullName} 與 {clrType.FullName}。啟動已中止，避免訊息送錯 handler。");
            }

            if (!byClrType.TryAdd(clrType, registration))
            {
                throw new InvalidOperationException($"整合事件 CLR 型別重複登錄：{clrType.FullName}。");
            }

            byEventType.Add(eventType, registration);
        }
    }

    private static JsonSerializerContext CreateContext(Type contextType)
    {
        var constructor = contextType.GetConstructor([typeof(JsonSerializerOptions)])
            ?? throw new InvalidOperationException(
                $"{contextType.FullName} 必須提供接受 JsonSerializerOptions 的 public 建構式。");

        try
        {
            // 每個 JsonSerializerContext 都會封裝並鎖定自己的 options；同一個 Options 實例
            // 無法被第二個 context 重複使用。CreateOptions 仍由 GreyGrayJson 的同一個
            // private factory 建立，線上格式來源不會分岔。
            return (JsonSerializerContext)constructor.Invoke([GreyGrayJson.CreateOptions()]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException(
                $"無法用 GreyGrayJson.CreateOptions() 建立 {contextType.FullName}。",
                exception.InnerException);
        }
    }

    private static string ReadEventType(Type clrType)
    {
        var property = clrType.GetProperty(
            nameof(IIntegrationEvent.EventType),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (property?.PropertyType != typeof(string)
            || property.GetValue(null) is not string eventType
            || string.IsNullOrWhiteSpace(eventType))
        {
            throw new InvalidOperationException(
                $"整合事件 {clrType.FullName} 沒有有效的 public static string EventType。");
        }

        return eventType;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var loaderErrors = string.Join(
                Environment.NewLine,
                exception.LoaderExceptions
                    .Where(error => error is not null)
                    .Select(error => error!.Message));

            throw new InvalidOperationException(
                $"無法載入 Contracts 組件 '{assembly.FullName}' 的全部型別：{loaderErrors}",
                exception);
        }
    }

    private static IReadOnlyCollection<Assembly> DiscoverContractAssemblies()
    {
        var byName = AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsContractsAssembly)
            .ToDictionary(assembly => assembly.GetName().Name!, StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(
                     AppContext.BaseDirectory,
                     $"{ContractsAssemblyPrefix}*{ContractsAssemblySuffix}.dll",
                     SearchOption.TopDirectoryOnly))
        {
            var assemblyName = AssemblyName.GetAssemblyName(path).Name!;
            if (!byName.ContainsKey(assemblyName))
            {
                byName.Add(
                    assemblyName,
                    AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path)));
            }
        }

        return byName.Values.ToArray();
    }

    private static bool IsContractsAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name is not null
            && name.StartsWith(ContractsAssemblyPrefix, StringComparison.Ordinal)
            && name.EndsWith(ContractsAssemblySuffix, StringComparison.Ordinal);
    }

    private sealed record Registration(
        string EventType,
        Type ClrType,
        JsonTypeInfo JsonTypeInfo);
}
