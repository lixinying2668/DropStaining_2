namespace Stainer.Web.Domain.Entities;

public sealed class ChannelBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MachineRunId { get; set; } = string.Empty;
    public string DrawerId { get; set; } = string.Empty;
    public string DrawerCode { get; set; } = string.Empty;
    public string Status { get; set; } = RuntimeLedgerStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public MachineRun? MachineRun { get; set; }
    public Drawer? Drawer { get; set; }
    public ICollection<SlideTask> SlideTasks { get; set; } = new List<SlideTask>();
}

public sealed class SlideTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ChannelBatchId { get; set; } = string.Empty;
    public string StainingTaskId { get; set; } = string.Empty;
    public string PhysicalSlotId { get; set; } = string.Empty;
    public string SlotCode { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = RuntimeLedgerStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ChannelBatch? ChannelBatch { get; set; }
    public StainingTask? StainingTask { get; set; }
    public PhysicalSlot? PhysicalSlot { get; set; }
    public ICollection<WorkflowExecution> WorkflowExecutions { get; set; } = new List<WorkflowExecution>();
}

public sealed class MachineRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RunCode { get; set; } = string.Empty;
    public string Status { get; set; } = RuntimeLedgerStatus.Created;
    public string? RequestedByUserId { get; set; }
    public bool PauseRequested { get; set; }
    public bool StopRequested { get; set; }
    public string? FaultMessage { get; set; }
    public string? CurrentMajorStepCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public User? RequestedByUser { get; set; }
    public ICollection<ChannelBatch> ChannelBatches { get; set; } = new List<ChannelBatch>();
    public ICollection<WorkflowExecution> WorkflowExecutions { get; set; } = new List<WorkflowExecution>();
}

public sealed class WorkflowExecution
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MachineRunId { get; set; } = string.Empty;
    public string SlideTaskId { get; set; } = string.Empty;
    public string WorkflowVersionId { get; set; } = string.Empty;
    public string Status { get; set; } = RuntimeLedgerStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public MachineRun? MachineRun { get; set; }
    public SlideTask? SlideTask { get; set; }
    public WorkflowVersion? WorkflowVersion { get; set; }
    public ICollection<WorkflowStepExecution> StepExecutions { get; set; } = new List<WorkflowStepExecution>();
}

public sealed class WorkflowStepExecution
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkflowExecutionId { get; set; } = string.Empty;
    public int StepNo { get; set; }
    public string MajorStepCode { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? ReagentCode { get; set; }
    public int? VolumeUl { get; set; }
    public string Status { get; set; } = RuntimeLedgerStatus.Pending;
    public int RedoCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public WorkflowExecution? WorkflowExecution { get; set; }
}

public sealed class DeviceCommandExecution
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MachineRunId { get; set; } = string.Empty;
    public string? WorkflowStepExecutionId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string Status { get; set; } = DeviceCommandStatus.Planned;
    public string PayloadJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CommandSentAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public MachineRun? MachineRun { get; set; }
    public WorkflowStepExecution? WorkflowStepExecution { get; set; }
}

public sealed class ReagentReservation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MachineRunId { get; set; } = string.Empty;
    public string ReagentCode { get; set; } = string.Empty;
    public int RequiredVolumeUl { get; set; }
    public int ReservedVolumeUl { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public MachineRun? MachineRun { get; set; }
}

public sealed class ReagentConsumption
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MachineRunId { get; set; } = string.Empty;
    public string WorkflowStepExecutionId { get; set; } = string.Empty;
    public string ReagentBottleId { get; set; } = string.Empty;
    public string ReagentCode { get; set; } = string.Empty;
    public int VolumeUl { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public MachineRun? MachineRun { get; set; }
    public WorkflowStepExecution? WorkflowStepExecution { get; set; }
    public ReagentBottle? ReagentBottle { get; set; }
}

public sealed class DispenseExecution
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DeviceCommandExecutionId { get; set; } = string.Empty;
    public string? ReagentBottleId { get; set; }
    public string ReagentCode { get; set; } = string.Empty;
    public int VolumeUl { get; set; }
    public string? SourcePositionCode { get; set; }
    public string? TargetSlotCode { get; set; }
    public string Status { get; set; } = DeviceCommandStatus.Planned;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DeviceCommandExecution? DeviceCommandExecution { get; set; }
    public ReagentBottle? ReagentBottle { get; set; }
}

public sealed class DabBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DabMixPositionId { get; set; } = string.Empty;
    public string PositionCode { get; set; } = string.Empty;
    public string Status { get; set; } = RuntimeLedgerStatus.Available;
    public int RemainingVolumeUl { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DabMixPosition? DabMixPosition { get; set; }
}

public sealed class DabBatchUsage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DabBatchId { get; set; } = string.Empty;
    public string MachineRunId { get; set; } = string.Empty;
    public string WorkflowStepExecutionId { get; set; } = string.Empty;
    public int VolumeUl { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DabBatch? DabBatch { get; set; }
    public MachineRun? MachineRun { get; set; }
    public WorkflowStepExecution? WorkflowStepExecution { get; set; }
}

public sealed class Alarm
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? MachineRunId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClearedAtUtc { get; set; }

    public MachineRun? MachineRun { get; set; }
    public ICollection<AlarmAction> Actions { get; set; } = new List<AlarmAction>();
}

public sealed class AlarmAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AlarmId { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Alarm? Alarm { get; set; }
    public User? ActorUser { get; set; }
}
