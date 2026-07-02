namespace Stainer.Web.Domain.Entities;

public static class RuntimeLedgerStatus
{
    public const string Created = "Created";
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Paused = "Paused";
    public const string Stopped = "Stopped";
    public const string Failed = "Failed";
    public const string Unknown = "Unknown";
    public const string Faulted = "Faulted";
    public const string Available = "Available";
    public const string WaitingUnload = "WaitingUnload";
    public const string Expired = "Expired";
}

public static class WorkflowSelectionStatus
{
    public const string Unselected = "Unselected";
    public const string Selected = "Selected";
    public const string Locked = "Locked";
    public const string NeedsManualResolution = "NeedsManualResolution";
}

public static class WorkflowAssignmentAction
{
    public const string InitialSelection = "InitialSelection";
    public const string PreStartChange = "PreStartChange";
    public const string Locked = "Locked";
    public const string Lock = Locked;
    public const string Backfill = "Backfill";
    public const string ManualResolutionRequired = "ManualResolutionRequired";
}

public static class DeviceCommandStatus
{
    public const string Planned = "Planned";
    public const string CommandSent = "CommandSent";
    public const string DeviceAcknowledged = "DeviceAcknowledged";
    public const string Acknowledged = "Acknowledged";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Unknown = "Unknown";
}
