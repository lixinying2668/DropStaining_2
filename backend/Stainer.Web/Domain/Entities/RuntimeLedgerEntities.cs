namespace Stainer.Web.Domain.Entities;

public sealed class ChannelBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? MachineRunId { get; set; }
    public string DrawerId { get; set; } = string.Empty;
    public string DrawerCode { get; set; } = string.Empty;
    public string Status { get; set; } = RuntimeLedgerStatus.Pending;
    public string? ExperimentType { get; set; }
    public string? SelectedWorkflowVersionId { get; set; }
    public string WorkflowSnapshotJson { get; set; } = "{}";
    public string WorkflowSelectionStatus { get; set; } = Stainer.Web.Domain.Entities.WorkflowSelectionStatus.Unselected;
    public bool NeedsManualResolution { get; set; }
    public string ManualResolutionReason { get; set; } = string.Empty;
    public DateTimeOffset? WorkflowSelectedAtUtc { get; set; }
    public string? WorkflowSelectedByUserId { get; set; }
    public DateTimeOffset? WorkflowLockedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public MachineRun? MachineRun { get; set; }
    public Drawer? Drawer { get; set; }
    public WorkflowVersion? SelectedWorkflowVersion { get; set; }
    public User? WorkflowSelectedByUser { get; set; }
    public ICollection<SlideTask> SlideTasks { get; set; } = new List<SlideTask>();
    public ICollection<WorkflowAssignmentHistory> WorkflowAssignmentHistory { get; set; } = new List<WorkflowAssignmentHistory>();
}

public sealed class WorkflowAssignmentHistory
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ChannelBatchId { get; set; } = string.Empty;
    public string? OldExperimentType { get; set; }
    public string? OldWorkflowVersionId { get; set; }
    public string? OldWorkflowSnapshotJson { get; set; }
    public string? NewExperimentType { get; set; }
    public string? NewWorkflowVersionId { get; set; }
    public string? NewWorkflowSnapshotJson { get; set; }
    public string ActionType { get; set; } = WorkflowAssignmentAction.InitialSelection;
    public string? ActorUserId { get; set; }
    public string? OperatorUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = string.Empty;
    public string? CommandId { get; set; }
    public string? CorrelationId { get; set; }

    public ChannelBatch? ChannelBatch { get; set; }
    public User? ActorUser { get; set; }
    public User? OperatorUser { get; set; }
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
    public string? MachineRunId { get; set; }
    public string? DabBatchId { get; set; }
    public string? ReagentBottleId { get; set; }
    public string ReagentCode { get; set; } = string.Empty;
    public string ReservationKind { get; set; } = ReagentReservationKind.MachineRun;
    public string SourceRole { get; set; } = string.Empty;
    public string Status { get; set; } = ReagentReservationStatus.Reserved;
    public string? CommandId { get; set; }
    public string? CreatedByUserId { get; set; }
    public int RequiredVolumeUl { get; set; }
    public int ReservedVolumeUl { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public MachineRun? MachineRun { get; set; }
    public DabBatch? DabBatch { get; set; }
    public ReagentBottle? ReagentBottle { get; set; }
    public User? CreatedByUser { get; set; }
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
    public string? DabAReagentBottleId { get; set; }
    public string? DabBReagentBottleId { get; set; }
    public string? CreatedByUserId { get; set; }
    public string Status { get; set; } = DabBatchStatus.PendingPreparation;
    public string CleaningStatus { get; set; } = DabCleaningStatus.NotRequired;
    public int SlideCount { get; set; }
    public int VolumePerSlideUl { get; set; } = DabFormula.VolumePerSlideUl;
    public int LineReserveVolumeUl { get; set; } = DabFormula.LineReserveVolumeUl;
    public int DabARatioParts { get; set; } = DabFormula.DabARatioParts;
    public int DabBRatioParts { get; set; } = DabFormula.DabBRatioParts;
    public int WaterRatioParts { get; set; } = DabFormula.WaterRatioParts;
    public int TotalRequiredVolumeUl { get; set; }
    public int ActualPreparedVolumeUl { get; set; }
    public int DabAVolumeUl { get; set; }
    public int DabBVolumeUl { get; set; }
    public int WaterVolumeUl { get; set; }
    public int UsedVolumeUl { get; set; }
    public int RemainingVolumeUl { get; set; }
    public DateTimeOffset? PreparedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? CleaningConfirmedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public DabMixPosition? DabMixPosition { get; set; }
    public ReagentBottle? DabAReagentBottle { get; set; }
    public ReagentBottle? DabBReagentBottle { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<DabBatchTask> Tasks { get; } = new List<DabBatchTask>();
    public ICollection<DabBatchUsage> Usages { get; } = new List<DabBatchUsage>();
    public ICollection<ReagentReservation> ReagentReservations { get; } = new List<ReagentReservation>();
}

public sealed class DabBatchTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DabBatchId { get; set; } = string.Empty;
    public string StainingTaskId { get; set; } = string.Empty;
    public int RequiredVolumeUl { get; set; } = DabFormula.VolumePerSlideUl;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DabBatch? DabBatch { get; set; }
    public StainingTask? StainingTask { get; set; }
}

public sealed class DabBatchUsage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DabBatchId { get; set; } = string.Empty;
    public string? MachineRunId { get; set; }
    public string? WorkflowStepExecutionId { get; set; }
    public string? StainingTaskId { get; set; }
    public string? CommandId { get; set; }
    public string? CreatedByUserId { get; set; }
    public int VolumeUl { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DabBatch? DabBatch { get; set; }
    public MachineRun? MachineRun { get; set; }
    public WorkflowStepExecution? WorkflowStepExecution { get; set; }
    public StainingTask? StainingTask { get; set; }
    public User? CreatedByUser { get; set; }
}

public static class DabBatchStatus
{
    public const string PendingPreparation = "PendingPreparation";
    public const string Preparing = "Preparing";
    public const string Available = "Available";
    public const string Depleted = "Depleted";
    public const string Expired = "Expired";
    public const string AwaitingCleaning = "AwaitingCleaning";
    public const string Cleaned = "Cleaned";
    public const string Failed = "Failed";
    public const string Unknown = "Unknown";
    public const string LegacyUnverified = "LegacyUnverified";
}

public static class DabCleaningStatus
{
    public const string NotRequired = "NotRequired";
    public const string Required = "Required";
    public const string Confirmed = "Confirmed";
    public const string NeedsManualResolution = "NeedsManualResolution";
}

public static class ReagentReservationKind
{
    public const string MachineRun = "MachineRun";
    public const string DabBatch = "DabBatch";
}

public static class ReagentReservationStatus
{
    public const string Reserved = "Reserved";
    public const string Consumed = "Consumed";
    public const string Released = "Released";
    public const string NeedsManualResolution = "NeedsManualResolution";
}

public static class DabFormula
{
    public const int DabARatioParts = 1;
    public const int DabBRatioParts = 1;
    public const int WaterRatioParts = 18;
    public const int TotalRatioParts = DabARatioParts + DabBRatioParts + WaterRatioParts;
    public const int VolumePerSlideUl = 200;
    public const int LineReserveVolumeUl = 400;
    public const int ValidityHours = 3;

    public static DabFormulaVolumes CalculateRequired(int slideCount)
    {
        if (slideCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slideCount), "At least one slide is required.");
        }

        return Calculate((checked(slideCount * VolumePerSlideUl)) + LineReserveVolumeUl);
    }

    public static DabFormulaVolumes Calculate(int totalVolumeUl)
    {
        if (totalVolumeUl <= 0 || totalVolumeUl % TotalRatioParts != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalVolumeUl), "DAB volume must be a positive multiple of 20 uL.");
        }

        var onePart = totalVolumeUl / TotalRatioParts;
        return new DabFormulaVolumes(totalVolumeUl, onePart, onePart, onePart * WaterRatioParts);
    }
}

public sealed record DabFormulaVolumes(
    int TotalVolumeUl,
    int DabAVolumeUl,
    int DabBVolumeUl,
    int WaterVolumeUl);

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
