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

public static class DeviceCommandStatus
{
    public const string Planned = "Planned";
    public const string CommandSent = "CommandSent";
    public const string Acknowledged = "Acknowledged";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Unknown = "Unknown";
}
