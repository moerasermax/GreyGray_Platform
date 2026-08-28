using GreyGray.Platform.Abstractions.Saga;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Platform.Saga;

/// <summary>把靜態 saga type 對應到 dispatcher scope 內解析的 handler。</summary>
public sealed class SagaTimeoutHandlerRegistration
{
    private readonly Func<IServiceProvider, string, string, CancellationToken, Task> _invoke;

    private SagaTimeoutHandlerRegistration(
        string sagaType,
        Func<IServiceProvider, string, string, CancellationToken, Task> invoke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaType);
        SagaType = sagaType;
        _invoke = invoke;
    }

    public string SagaType { get; }

    public static SagaTimeoutHandlerRegistration For<THandler>()
        where THandler : class, ISagaTimeoutHandler =>
        new(
            THandler.SagaType,
            static (serviceProvider, sagaId, payload, cancellationToken) =>
                serviceProvider.GetRequiredService<THandler>()
                    .HandleTimeoutAsync(sagaId, payload, cancellationToken));

    internal Task InvokeAsync(
        IServiceProvider serviceProvider,
        string sagaId,
        string payload,
        CancellationToken cancellationToken) =>
        _invoke(serviceProvider, sagaId, payload, cancellationToken);
}

public static class SagaTimeoutHandlerServiceCollectionExtensions
{
    public static IServiceCollection AddSagaTimeoutHandler<THandler>(
        this IServiceCollection services)
        where THandler : class, ISagaTimeoutHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<THandler>();
        services.AddSingleton(SagaTimeoutHandlerRegistration.For<THandler>());
        return services;
    }
}
