using System.Collections.Concurrent;
using PortSyncs.Api.Events;

namespace PortSyncs.Api.Infrastructure.Messaging;

/// <summary>
/// Carries standard events between modules. H1 starts in-process; RabbitMQ and then the
/// cloud's own queue replace it behind this interface (T75).
/// </summary>
public interface IEventBus
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default);
    void Subscribe(string eventType, Func<EventEnvelope, CancellationToken, Task> handler);
}

public sealed class InProcessEventBus(ILogger<InProcessEventBus> logger) : IEventBus
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<Func<EventEnvelope, CancellationToken, Task>>> _handlers = new();
    private readonly ConcurrentDictionary<string, byte> _seenKeys = new();

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // Drop duplicates from outside systems (idempotency key is required for connectors).
        if (envelope.IdempotencyKey is { } key && !_seenKeys.TryAdd(key, 0))
        {
            logger.LogDebug("Duplicate event dropped: {Key}", key);
            return;
        }

        if (!_handlers.TryGetValue(envelope.Type, out var handlers)) return;

        foreach (var handler in handlers)
        {
            try
            {
                await handler(envelope, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handler failed for {Type} {EventId}", envelope.Type, envelope.EventId);
            }
        }
    }

    public void Subscribe(string eventType, Func<EventEnvelope, CancellationToken, Task> handler) =>
        _handlers.GetOrAdd(eventType, _ => []).Add(handler);
}
