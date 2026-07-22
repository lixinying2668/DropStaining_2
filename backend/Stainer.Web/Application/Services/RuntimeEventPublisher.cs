using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Stainer.Web.Application.Services;

public interface IRuntimeEventPublisher
{
    void Publish(string runId, string eventType, string message);
    void Publish(MachineEventMessage message);
}

public sealed class InMemoryRuntimeEventPublisher : IRuntimeEventPublisher
{
    private readonly Channel<MachineEventMessage> channel = Channel.CreateUnbounded<MachineEventMessage>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentQueue<MachineEventMessage> events = new();

    public void Publish(string runId, string eventType, string message)
    {
        var mappedType = eventType switch
        {
            "step.completed" => MachineEventTypes.WorkflowStepCompleted,
            "step.failed" => MachineEventTypes.WorkflowStepCompleted,
            "run.paused" or "run.stopped" or "run.completed" or "run.faulted" or "run.running" or "run.redo" => MachineEventTypes.MachineStateChanged,
            _ => MachineEventTypes.MachineStateChanged
        };
        Publish(MachineEventMessage.Create(
            mappedType,
            runId,
            "MachineRun",
            runId,
            null,
            new Dictionary<string, object?>
            {
                ["message"] = message,
                ["legacyEventType"] = eventType
            }));
    }

    public void Publish(MachineEventMessage message)
    {
        events.Enqueue(message);
        channel.Writer.TryWrite(message);
        while (events.Count > 500)
        {
            events.TryDequeue(out _);
        }
    }

    public IAsyncEnumerable<MachineEventMessage> ReadAllAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    public IReadOnlyList<MachineEventMessage> Snapshot()
    {
        return events.ToArray();
    }
}

public static class MachineEventTypes
{
    public const string MachineStateChanged = "machine.stateChanged";
    public const string ChannelBatchChanged = "channelBatch.changed";
    public const string SlideTaskCreated = "slideTask.created";
    public const string SlideTaskStateChanged = "slideTask.stateChanged";
    public const string WorkflowStepStarted = "workflowStep.started";
    public const string WorkflowStepCompleted = "workflowStep.completed";
    public const string WorkflowChanged = "workflow.changed";
    public const string WorkflowVersionChanged = "workflowVersion.changed";
    public const string WorkflowStepChanged = "workflowStep.changed";
    public const string WorkflowReagentRequirementChanged = "workflowReagentRequirement.changed";
    public const string PrimaryAntibodyMappingChanged = "primaryAntibodyMapping.changed";
    public const string TemperatureChanged = "temperature.changed";
    public const string CoolingChanged = "cooling.changed";
    public const string PumpChanged = "pump.changed";
    public const string MixerChanged = "mixer.changed";
    public const string LiquidLevelChanged = "liquidLevel.changed";
    public const string WaterSupplyChanged = "waterSupply.changed";
    public const string ReagentChanged = "reagent.changed";
    public const string ReagentBottleChanged = "reagentBottle.changed";
    public const string ReagentBottleDepleted = "reagent.bottleDepleted";
    public const string DabBatchChanged = "dab.batchChanged";
    public const string AlarmRaised = "alarm.raised";
    public const string AlarmAcknowledged = "alarm.acknowledged";
    public const string DeviceConnectionChanged = "device.connectionChanged";
    public const string DeviceStateChanged = "device.stateChanged";
    public const string DeviceInitializationChanged = "device.initializationChanged";
    public const string QrScanCompleted = "qr.scanCompleted";
    public const string ScanSessionChanged = "scanSession.changed";
    public const string MockRuntimeReset = "mockRuntime.resetCompleted";
}

public sealed record MachineEventMessage(
    string EventId,
    string Type,
    DateTimeOffset OccurredAtUtc,
    string? RunId,
    string? EntityType,
    string? EntityId,
    string? RequiredRole,
    IReadOnlyDictionary<string, object?> Payload)
{
    public static MachineEventMessage Create(
        string type,
        string? runId,
        string? entityType,
        string? entityId,
        string? requiredRole,
        IReadOnlyDictionary<string, object?> payload)
    {
        return new MachineEventMessage(
            Guid.NewGuid().ToString("N"),
            type,
            DateTimeOffset.UtcNow,
            runId,
            entityType,
            entityId,
            requiredRole,
            payload);
    }
}
