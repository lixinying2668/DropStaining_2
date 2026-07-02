using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Services;

namespace Stainer.Web.Infrastructure.Devices;

public sealed class MockDeviceStateStore
{
    private static readonly IReadOnlySet<string> RequiredReadyModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        DeviceModules.Controller,
        DeviceModules.Temperature,
        DeviceModules.Cooling,
        DeviceModules.SampleScanner,
        DeviceModules.ReagentScanner,
        DeviceModules.RobotArm,
        DeviceModules.Needles,
        DeviceModules.Pipette,
        DeviceModules.Pump,
        DeviceModules.Mixer,
        DeviceModules.LiquidLevel,
        DeviceModules.NeedleWash
    };

    private readonly object syncRoot = new();
    private readonly Dictionary<string, MutableModuleState> modules = DeviceModules.All
        .ToDictionary(x => x, x => new MutableModuleState(x), StringComparer.OrdinalIgnoreCase);
    private readonly List<MutableFaultPlan> faultPlans = [];
    private long version;
    private DateTimeOffset updatedAtUtc = DateTimeOffset.UtcNow;

    public DeviceStatusSnapshot Snapshot()
    {
        lock (syncRoot)
        {
            return SnapshotUnsafe();
        }
    }

    internal void BeginOperation(string moduleCode, string action, string? targetParametersJson)
    {
        lock (syncRoot)
        {
            var module = GetOrCreateModule(moduleCode);
            module.CurrentAction = action;
            module.TargetParametersJson = targetParametersJson;
            Touch(module);
        }
    }

    internal DeviceFaultPlanSnapshot? ConsumeFault(string moduleCode)
    {
        lock (syncRoot)
        {
            var plan = faultPlans
                .Where(x => x.Active && string.Equals(x.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefault();
            if (plan is null)
            {
                return null;
            }

            if (plan.FaultType is DeviceFaultTypes.FailNextCommand
                or DeviceFaultTypes.TimeoutNextCommand
                or DeviceFaultTypes.ReturnUnknown)
            {
                plan.Active = false;
                plan.ClearedAtUtc = DateTimeOffset.UtcNow;
                Touch();
            }

            return plan.ToSnapshot();
        }
    }

    internal void CompleteOperation(
        string moduleCode,
        string connectionStatus,
        string? currentParametersJson,
        string? errorCode,
        string? errorMessage)
    {
        lock (syncRoot)
        {
            var module = GetOrCreateModule(moduleCode);
            module.ConnectionStatus = connectionStatus;
            module.CurrentAction = "Idle";
            module.CurrentParametersJson = currentParametersJson;
            module.LastErrorCode = errorCode;
            module.LastErrorMessage = errorMessage;
            Touch(module);
        }
    }

    internal void SyncModuleState(
        string moduleCode,
        string connectionStatus,
        string currentAction,
        string? currentParametersJson,
        string? errorCode,
        string? errorMessage)
    {
        lock (syncRoot)
        {
            var module = GetOrCreateModule(moduleCode);
            if (!string.IsNullOrWhiteSpace(module.LastErrorCode) && string.IsNullOrWhiteSpace(errorCode))
            {
                return;
            }

            if (module.ConnectionStatus == connectionStatus
                && module.CurrentAction == currentAction
                && module.CurrentParametersJson == currentParametersJson
                && module.LastErrorCode == errorCode
                && module.LastErrorMessage == errorMessage)
            {
                return;
            }

            module.ConnectionStatus = connectionStatus;
            module.CurrentAction = currentAction;
            module.CurrentParametersJson = currentParametersJson;
            module.LastErrorCode = errorCode;
            module.LastErrorMessage = errorMessage;
            Touch(module);
        }
    }

    internal DeviceStatusSnapshot ConfigureFault(DeviceFaultCommand command)
    {
        lock (syncRoot)
        {
            faultPlans.Add(new MutableFaultPlan
            {
                Id = Guid.NewGuid().ToString(),
                ModuleCode = command.ModuleCode,
                FaultType = command.FaultType,
                ErrorCode = command.ErrorCode,
                Message = string.IsNullOrWhiteSpace(command.Message) ? $"Injected {command.FaultType}." : command.Message.Trim(),
                Reason = command.Reason,
                CommandId = command.CommandId,
                OperatorUserId = command.OperatorUserId,
                OperatorUsername = command.OperatorUsername,
                Active = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            var module = GetOrCreateModule(command.ModuleCode);
            module.LastErrorCode = command.ErrorCode ?? "mock_fault_planned";
            module.LastErrorMessage = command.Message ?? command.FaultType;
            Touch(module);
            return SnapshotUnsafe();
        }
    }

    internal DeviceStatusSnapshot ClearFaults(DeviceFaultClearCommand command)
    {
        lock (syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var matching = faultPlans.Where(x => x.Active
                && (string.IsNullOrWhiteSpace(command.ModuleCode)
                    || string.Equals(x.ModuleCode, command.ModuleCode, StringComparison.OrdinalIgnoreCase)));
            foreach (var plan in matching)
            {
                plan.Active = false;
                plan.ClearedAtUtc = now;
            }

            var affectedModules = string.IsNullOrWhiteSpace(command.ModuleCode)
                ? modules.Values
                : modules.Values.Where(x => string.Equals(x.ModuleCode, command.ModuleCode, StringComparison.OrdinalIgnoreCase));
            foreach (var module in affectedModules)
            {
                module.ConnectionStatus = DeviceConnectionStatuses.Connected;
                module.CurrentAction = "Idle";
                module.LastErrorCode = null;
                module.LastErrorMessage = null;
                module.UpdatedAtUtc = now;
            }

            Touch();
            return SnapshotUnsafe();
        }
    }

    private MutableModuleState GetOrCreateModule(string moduleCode)
    {
        if (!modules.TryGetValue(moduleCode, out var module))
        {
            module = new MutableModuleState(moduleCode);
            modules[moduleCode] = module;
        }

        return module;
    }

    private void Touch(MutableModuleState module)
    {
        version++;
        updatedAtUtc = DateTimeOffset.UtcNow;
        module.Version = version;
        module.UpdatedAtUtc = updatedAtUtc;
    }

    private void Touch()
    {
        version++;
        updatedAtUtc = DateTimeOffset.UtcNow;
    }

    private DeviceStatusSnapshot SnapshotUnsafe()
    {
        var moduleSnapshots = modules.Values
            .OrderBy(x => x.ModuleCode)
            .Select(x => x.ToSnapshot())
            .ToList();
        var ready = RequiredReadyModules.All(required => moduleSnapshots.Any(x =>
            string.Equals(x.ModuleCode, required, StringComparison.OrdinalIgnoreCase)
            && x.ConnectionStatus == DeviceConnectionStatuses.Connected
            && string.IsNullOrWhiteSpace(x.LastErrorCode)));
        return new DeviceStatusSnapshot(
            DeviceModes.Mock,
            nameof(MockDeviceAdapter),
            ready,
            version,
            updatedAtUtc,
            moduleSnapshots,
            faultPlans.OrderBy(x => x.CreatedAtUtc).Select(x => x.ToSnapshot()).ToList());
    }

    private sealed class MutableModuleState(string moduleCode)
    {
        public string ModuleCode { get; } = moduleCode;
        public string ConnectionStatus { get; set; } = DeviceConnectionStatuses.Unknown;
        public string CurrentAction { get; set; } = "Idle";
        public string? TargetParametersJson { get; set; }
        public string? CurrentParametersJson { get; set; }
        public string? LastErrorCode { get; set; }
        public string? LastErrorMessage { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public long Version { get; set; }

        public DeviceModuleStatusSnapshot ToSnapshot() => new(
            ModuleCode,
            ConnectionStatus,
            CurrentAction,
            TargetParametersJson,
            CurrentParametersJson,
            LastErrorCode,
            LastErrorMessage,
            UpdatedAtUtc,
            Version);
    }

    private sealed class MutableFaultPlan
    {
        public string Id { get; set; } = string.Empty;
        public string ModuleCode { get; set; } = string.Empty;
        public string FaultType { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string CommandId { get; set; } = string.Empty;
        public string? OperatorUserId { get; set; }
        public string? OperatorUsername { get; set; }
        public bool Active { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset? ClearedAtUtc { get; set; }

        public DeviceFaultPlanSnapshot ToSnapshot() => new(
            Id,
            ModuleCode,
            FaultType,
            ErrorCode,
            Message,
            Reason,
            CommandId,
            OperatorUserId,
            OperatorUsername,
            Active,
            CreatedAtUtc,
            ClearedAtUtc);
    }
}
