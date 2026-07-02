namespace Stainer.Web.Domain.Entities;

public sealed class RobotArmState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool IsHomed { get; set; }
    public bool IsConnected { get; set; } = true;
    public string Status { get; set; } = MotionStatuses.Idle;
    public string? CurrentTargetPointCode { get; set; }
    public long? CurrentXUm { get; set; }
    public long? CurrentYUm { get; set; }
    public long? CurrentZUm { get; set; }
    public string? CoordinateProfileVersionId { get; set; }
    public string? CurrentCommandId { get; set; }
    public string? MachineRunId { get; set; }
    public string? WorkflowStepExecutionId { get; set; }
    public string? DeviceCommandExecutionId { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NeedleState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NeedleCode { get; set; } = string.Empty;
    public int NeedleNo { get; set; }
    public bool IsConnected { get; set; } = true;
    public string Status { get; set; } = MotionStatuses.Idle;
    public string LoadedSourceType { get; set; } = NeedleLoadSourceTypes.Empty;
    public string? LoadedReagentCode { get; set; }
    public string? SourceBottleId { get; set; }
    public string? DabBatchId { get; set; }
    public string? SystemLiquidSourceType { get; set; }
    public string? SourcePositionCode { get; set; }
    public int VolumeUl { get; set; }
    public string? LiquidClassVersionId { get; set; }
    public int? LiquidClassVersionNo { get; set; }
    public string LiquidClassParametersJson { get; set; } = "{}";
    public bool NeedsWash { get; set; }
    public string? CurrentCommandId { get; set; }
    public string? MachineRunId { get; set; }
    public string? WorkflowStepExecutionId { get; set; }
    public string? DeviceCommandExecutionId { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PipettingOperation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = DeviceCommandStatus.Planned;
    public string? NeedleCode { get; set; }
    public string ExecutionMode { get; set; } = PipettingExecutionModes.Single;
    public string? TargetPointCode { get; set; }
    public string? SecondaryTargetPointCode { get; set; }
    public string? CoordinateProfileVersionId { get; set; }
    public string? LiquidClassVersionId { get; set; }
    public int? LiquidClassVersionNo { get; set; }
    public string LiquidClassParametersJson { get; set; } = "{}";
    public string SourceType { get; set; } = NeedleLoadSourceTypes.Empty;
    public string? ReagentCode { get; set; }
    public string? ReagentBottleId { get; set; }
    public string? DabBatchId { get; set; }
    public string? SystemLiquidSourceType { get; set; }
    public string? SourcePositionCode { get; set; }
    public int VolumeUl { get; set; }
    public string? MachineRunId { get; set; }
    public string? WorkflowStepExecutionId { get; set; }
    public string? DeviceCommandExecutionId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class MachineResourceLease
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ResourceCode { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Status { get; set; } = MachineResourceLeaseStatus.Acquired;
    public string? MachineRunId { get; set; }
    public string? WorkflowStepExecutionId { get; set; }
    public string? DeviceCommandExecutionId { get; set; }
    public string? CommandType { get; set; }
    public string? WaitReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcquiredAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
}

public static class NeedleCodes
{
    public const string Needle1 = "Needle1";
    public const string Needle2 = "Needle2";

    public static readonly IReadOnlyList<string> All = [Needle1, Needle2];
}

public static class NeedleLoadSourceTypes
{
    public const string Empty = "Empty";
    public const string ReagentBottle = "ReagentBottle";
    public const string DabBatch = "DabBatch";
    public const string SystemLiquid = "SystemLiquid";
}

public static class MotionStatuses
{
    public const string Idle = "Idle";
    public const string Homing = "Homing";
    public const string Moving = "Moving";
    public const string Aspirating = "Aspirating";
    public const string Dispensing = "Dispensing";
    public const string BlowingOut = "BlowingOut";
    public const string Washing = "Washing";
    public const string Completed = "Completed";
    public const string Faulted = "Faulted";
    public const string TimedOut = "TimedOut";
    public const string Unknown = "Unknown";
    public const string Disconnected = "Disconnected";
}

public static class PipettingOperationTypes
{
    public const string Home = "Home";
    public const string Move = "Move";
    public const string LiquidDetect = "LiquidDetect";
    public const string Aspirate = "Aspirate";
    public const string Dispense = "Dispense";
    public const string Blowout = "Blowout";
    public const string WashNeedle = "WashNeedle";
}

public static class PipettingExecutionModes
{
    public const string Single = "Single";
    public const string Synchronized = "Synchronized";
    public const string Sequential = "Sequential";
}

public static class MachineResourceLeaseStatus
{
    public const string Waiting = "Waiting";
    public const string Acquired = "Acquired";
    public const string Released = "Released";
    public const string NeedsManualResolution = "NeedsManualResolution";
}

public static class MachineResourceTypes
{
    public const string Platform = "Platform";
    public const string Needle = "Needle";
    public const string WashStation = "WashStation";
    public const string DabPosition = "DabPosition";
    public const string Pump = "Pump";
    public const string Mixer = "Mixer";
    public const string Source = "Source";
}
