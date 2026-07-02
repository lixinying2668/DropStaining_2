using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class DeviceInitializationService(
    IDeviceAdapter deviceAdapter,
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    IRuntimeEventPublisher eventPublisher,
    SafetyLogWriter safetyLogWriter,
    DeviceCommunicationPersistenceService communicationPersistence,
    ThermalControlService thermalControlService,
    FluidicsControlService fluidicsControlService,
    MotionControlService motionControlService)
{
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyList<(string ModuleCode, string Action)> Steps =
    [
        (DeviceModules.Controller, "check-connection"),
        (DeviceModules.Temperature, "check-16-points"),
        (DeviceModules.Cooling, "check-connection"),
        (DeviceModules.SampleScanner, "check-connection"),
        (DeviceModules.ReagentScanner, "check-connection"),
        (DeviceModules.RobotArm, "home"),
        (DeviceModules.Needles, "check-empty"),
        (DeviceModules.Pipette, "check-pipette"),
        (DeviceModules.Pump, "check-pwm-channels"),
        (DeviceModules.Mixer, "check-mixer-channels"),
        (DeviceModules.LiquidLevel, "read-levels"),
        (DeviceModules.NeedleWash, "prepare")
    ];

    public async Task<DeviceInitializationResponse> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var runs = await dbContext.DeviceInitializationRuns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Checks)
            .ToListAsync(cancellationToken);
        var run = runs
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.AttemptNo)
            .FirstOrDefault();
        return run is null
            ? new DeviceInitializationResponse(
                false,
                string.Empty,
                false,
                null,
                "NotStarted",
                deviceAdapter.Mode,
                deviceAdapter.Name,
                0,
                null,
                null,
                null,
                [],
                "Device initialization has not started.")
            : ToResponse(run, false);
    }

    public Task<DeviceInitializationResponse> InitializeAsync(
        StartDeviceInitializationRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteSerializedAsync(request.CommandId, null, null, request, actor, cancellationToken);
    }

    public async Task<DeviceInitializationResponse> RetryAsync(
        string initializationRunId,
        RetryDeviceInitializationRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var source = await dbContext.DeviceInitializationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == initializationRunId, cancellationToken)
            ?? throw new BusinessRuleException("device_initialization_not_found", "Device initialization run was not found.", StatusCodes.Status404NotFound);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("reason_required", "Retrying device initialization requires a reason.", StatusCodes.Status400BadRequest);
        }

        return await ExecuteSerializedAsync(request.CommandId, source, reason, request, actor, cancellationToken);
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var runs = await dbContext.DeviceInitializationRuns
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var latest = runs.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        if (latest is null
            || latest.Status != DeviceInitializationStatus.Ready
            || !string.Equals(latest.DeviceMode, deviceAdapter.Mode, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                "device_initialization_required",
                "A successful device initialization for the current DeviceMode is required before run start.",
                StatusCodes.Status409Conflict);
        }


        await thermalControlService.EnsureReadyForRunAsync(cancellationToken);
        await fluidicsControlService.EnsureReadyForRunAsync(cancellationToken);
        await motionControlService.EnsureReadyForRunAsync(cancellationToken);
    }

    private async Task<DeviceInitializationResponse> ExecuteSerializedAsync(
        string commandId,
        DeviceInitializationRun? retryOf,
        string? retryReason,
        object request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken)
    {
        await InitializationGate.WaitAsync(cancellationToken);
        try
        {
            return await idempotencyService.RunAsync(
                commandId,
                retryOf is null ? "device.initialization.start" : "device.initialization.retry",
                retryOf is null ? request : new { retryOfRunId = retryOf.Id, request },
                actor,
                async () =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var run = new DeviceInitializationRun
                    {
                        CommandId = commandId,
                        Status = DeviceInitializationStatus.Running,
                        DeviceMode = deviceAdapter.Mode,
                        AdapterName = deviceAdapter.Name,
                        AttemptNo = retryOf is null ? 1 : retryOf.AttemptNo + 1,
                        RetryOfRunId = retryOf?.Id,
                        RequestedByUserId = actor.UserId,
                        StartedAtUtc = now,
                        CreatedAtUtc = now
                    };
                    foreach (var (step, index) in Steps.Select((value, index) => (value, index)))
                    {
                        run.Checks.Add(new DeviceInitializationCheck
                        {
                            StepNo = index + 1,
                            ModuleCode = step.ModuleCode,
                            Status = DeviceInitializationCheckStatus.Pending
                        });
                    }

                    dbContext.DeviceInitializationRuns.Add(run);
                    foreach (var check in run.Checks.OrderBy(x => x.StepNo))
                    {
                        var step = Steps[check.StepNo - 1];
                        check.Status = DeviceInitializationCheckStatus.Running;
                        check.StartedAtUtc = DateTimeOffset.UtcNow;
                        PublishProgress(run, check, commandId);
                        var parameters = new Dictionary<string, object?>();
                        ThermalDeviceResult? thermalResult = null;
                        if (check.ModuleCode is DeviceModules.Temperature or DeviceModules.Cooling)
                        {
                            thermalResult = await thermalControlService.InitializeModuleAsync(check.ModuleCode, cancellationToken);
                            foreach (var pair in thermalResult.Data)
                            {
                                parameters[pair.Key] = pair.Value;
                            }
                            parameters["thermalStateValidated"] = thermalResult.Ok;
                        }
                        FluidicsDeviceResult? fluidicsResult = null;
                        if (check.ModuleCode is DeviceModules.Pump or DeviceModules.Mixer or DeviceModules.LiquidLevel)
                        {
                            fluidicsResult = await fluidicsControlService.InitializeModuleAsync(check.ModuleCode, cancellationToken);
                            foreach (var pair in fluidicsResult.Data)
                            {
                                parameters[pair.Key] = pair.Value;
                            }
                            parameters["fluidicsStateValidated"] = fluidicsResult.Ok;
                        }
                        MotionDeviceResult? motionResult = null;
                        if (check.ModuleCode is DeviceModules.RobotArm or DeviceModules.Needles or DeviceModules.Pipette or DeviceModules.NeedleWash)
                        {
                            motionResult = await motionControlService.InitializeModuleAsync(check.ModuleCode, cancellationToken);
                            foreach (var pair in motionResult.Data)
                            {
                                parameters[pair.Key] = pair.Value;
                            }
                            parameters["motionStateValidated"] = motionResult.Ok;
                        }

                        var operationRequest = new DeviceOperationRequest(
                            new DeviceCommandContext($"{commandId}:{check.ModuleCode}", commandId, actor.Username, nameof(DeviceInitializationService)),
                            check.ModuleCode,
                            step.Action,
                            parameters);
                        var communicationRecord = communicationPersistence.Begin(operationRequest);
                        var result = thermalResult is { Ok: false }
                            ? new DeviceCommandResult(
                                false,
                                thermalResult.Status,
                                check.ModuleCode,
                                step.Action,
                                thermalResult.ErrorCode,
                                thermalResult.Message,
                                check.StartedAtUtc.Value,
                                DateTimeOffset.UtcNow,
                                thermalResult.Status is not DeviceCommandStatuses.TimedOut and not DeviceCommandStatuses.Unknown,
                                thermalResult.Data)
                            : fluidicsResult is { Ok: false }
                            ? new DeviceCommandResult(
                                false,
                                fluidicsResult.Status,
                                check.ModuleCode,
                                step.Action,
                                fluidicsResult.ErrorCode,
                                fluidicsResult.Message,
                                check.StartedAtUtc.Value,
                                DateTimeOffset.UtcNow,
                                fluidicsResult.Status is not DeviceCommandStatuses.TimedOut and not DeviceCommandStatuses.Unknown,
                                fluidicsResult.Data)
                            : motionResult is { Ok: false }
                            ? new DeviceCommandResult(
                                false,
                                motionResult.Status,
                                check.ModuleCode,
                                step.Action,
                                motionResult.ErrorCode,
                                motionResult.Message,
                                check.StartedAtUtc.Value,
                                DateTimeOffset.UtcNow,
                                motionResult.Status is not DeviceCommandStatuses.TimedOut and not DeviceCommandStatuses.Unknown,
                                motionResult.Data)
                            : await deviceAdapter.InitializeModuleAsync(operationRequest, cancellationToken);
                        communicationPersistence.Complete(communicationRecord, result);
                        check.Status = MapCheckStatus(result.Status);
                        check.ErrorCode = result.ErrorCode;
                        check.Message = result.Message;
                        check.ResultJson = JsonSerializer.Serialize(result.Data, JsonOptions);
                        check.CompletedAtUtc = result.CompletedAtUtc;
                        PublishProgress(run, check, commandId);
                    }

                    var failed = run.Checks.FirstOrDefault(x => x.Status != DeviceInitializationCheckStatus.Succeeded);
                    run.CompletedAtUtc = DateTimeOffset.UtcNow;
                    run.Status = failed is null ? DeviceInitializationStatus.Ready : DeviceInitializationStatus.Failed;
                    run.FailureCode = failed?.ErrorCode;
                    run.Message = failed is null
                        ? "Device initialization completed successfully."
                        : $"Device initialization failed at {failed.ModuleCode}: {failed.Message}";
                    if (failed is not null)
                    {
                        dbContext.Alarms.Add(new Alarm
                        {
                            Code = "device_initialization_failed",
                            Severity = "Error",
                            Message = $"InitializationRun={run.Id}; Module={failed.ModuleCode}; Error={failed.ErrorCode}; {failed.Message}",
                            Status = "Active",
                            CreatedAtUtc = DateTimeOffset.UtcNow
                        });
                    }

                    dbContext.AuditLogs.Add(new AuditLog
                    {
                        ActorUserId = actor.UserId,
                        Action = failed is null ? "device.initialization.completed" : "device.initialization.failed",
                        EntityType = "DeviceInitializationRun",
                        EntityId = run.Id,
                        Message = JsonSerializer.Serialize(new
                        {
                            commandId,
                            run.DeviceMode,
                            run.AdapterName,
                            run.AttemptNo,
                            run.RetryOfRunId,
                            retryReason,
                            run.Status,
                            run.FailureCode,
                            run.Message
                        }, JsonOptions),
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    });
                    await safetyLogWriter.WriteAsync(
                        "device",
                        failed is null ? "Information" : "Error",
                        run.Message,
                        new SafetyLogContext(
                            CorrelationId: commandId,
                            CommandId: commandId,
                            DeviceMode: deviceAdapter.Mode,
                            Actor: actor.Username,
                            Source: nameof(DeviceInitializationService)),
                        cancellationToken: cancellationToken);
                    PublishCompleted(run, commandId);
                    return new CommandExecutionResult<DeviceInitializationResponse>(
                        ToResponse(run, false),
                        "DeviceInitializationRun",
                        run.Id);
                },
                cancellationToken);
        }
        finally
        {
            InitializationGate.Release();
        }
    }

    private static string MapCheckStatus(string adapterStatus)
    {
        return adapterStatus switch
        {
            DeviceCommandStatuses.Succeeded => DeviceInitializationCheckStatus.Succeeded,
            DeviceCommandStatuses.TimedOut => DeviceInitializationCheckStatus.TimedOut,
            DeviceCommandStatuses.Unknown => DeviceInitializationCheckStatus.Unknown,
            _ => DeviceInitializationCheckStatus.Failed
        };
    }

    private static DeviceInitializationResponse ToResponse(DeviceInitializationRun run, bool replayed)
    {
        return new DeviceInitializationResponse(
            run.Status == DeviceInitializationStatus.Ready,
            run.CommandId,
            replayed,
            run.Id,
            run.Status,
            run.DeviceMode,
            run.AdapterName,
            run.AttemptNo,
            run.RetryOfRunId,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.Checks.OrderBy(x => x.StepNo).Select(ToCheckResponse).ToList(),
            run.Message ?? string.Empty);
    }

    private static DeviceInitializationCheckResponse ToCheckResponse(DeviceInitializationCheck check)
    {
        IReadOnlyDictionary<string, object?> result;
        try
        {
            result = JsonSerializer.Deserialize<Dictionary<string, object?>>(check.ResultJson, JsonOptions)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            result = new Dictionary<string, object?>();
        }

        return new DeviceInitializationCheckResponse(
            check.Id,
            check.StepNo,
            check.ModuleCode,
            check.Status,
            check.ErrorCode,
            check.Message,
            check.StartedAtUtc,
            check.CompletedAtUtc,
            result);
    }

    private void PublishProgress(DeviceInitializationRun run, DeviceInitializationCheck check, string commandId)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.DeviceInitializationChanged,
            null,
            "DeviceInitializationRun",
            run.Id,
            null,
            new Dictionary<string, object?>
            {
                ["commandId"] = commandId,
                ["runId"] = run.Id,
                ["runStatus"] = run.Status,
                ["moduleCode"] = check.ModuleCode,
                ["checkStatus"] = check.Status,
                ["errorCode"] = check.ErrorCode,
                ["message"] = check.Message
            }));
    }

    private void PublishCompleted(DeviceInitializationRun run, string commandId)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.DeviceInitializationChanged,
            null,
            "DeviceInitializationRun",
            run.Id,
            null,
            new Dictionary<string, object?>
            {
                ["commandId"] = commandId,
                ["runId"] = run.Id,
                ["runStatus"] = run.Status,
                ["deviceMode"] = run.DeviceMode,
                ["adapterName"] = run.AdapterName,
                ["attemptNo"] = run.AttemptNo,
                ["message"] = run.Message
            }));
    }
}
