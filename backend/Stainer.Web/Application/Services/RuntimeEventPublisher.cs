using System.Collections.Concurrent;

namespace Stainer.Web.Application.Services;

public interface IRuntimeEventPublisher
{
    void Publish(string runId, string eventType, string message);
}

public sealed class InMemoryRuntimeEventPublisher : IRuntimeEventPublisher
{
    private readonly ConcurrentQueue<RuntimeEvent> events = new();

    public void Publish(string runId, string eventType, string message)
    {
        events.Enqueue(new RuntimeEvent(runId, eventType, message, DateTimeOffset.UtcNow));
        while (events.Count > 500)
        {
            events.TryDequeue(out _);
        }
    }

    public IReadOnlyList<RuntimeEvent> Snapshot()
    {
        return events.ToArray();
    }
}

public sealed record RuntimeEvent(string RunId, string EventType, string Message, DateTimeOffset CreatedAtUtc);
