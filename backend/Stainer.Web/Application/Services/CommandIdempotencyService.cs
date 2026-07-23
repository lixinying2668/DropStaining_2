using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class CommandIdempotencyService(StainerDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> RunAsync<T>(
        string commandId,
        string operation,
        object request,
        AuthenticatedUser actor,
        Func<Task<CommandExecutionResult<T>>> execute,
        CancellationToken cancellationToken = default)
        where T : class
    {
        commandId = RequireCommandId(commandId);
        var requestHash = HashRequest(operation, request);

        var existing = await dbContext.CommandReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Operation != operation || existing.RequestHash != requestHash)
            {
                throw new BusinessRuleException("command_conflict", "commandId already exists for a different request.", StatusCodes.Status409Conflict);
            }

            var response = JsonSerializer.Deserialize<T>(existing.ResponseJson, JsonOptions);
            if (response is null)
            {
                throw new BusinessRuleException("command_replay_failed", "Stored command response could not be replayed.", StatusCodes.Status409Conflict);
            }

            return MarkReplay(response);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var receipt = new CommandReceipt
        {
            CommandId = commandId,
            Operation = operation,
            RequestHash = requestHash,
            Status = "Started",
            ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.CommandReceipts.Add(receipt);

        var result = await execute();
        receipt.Status = "Completed";
        receipt.CompletedAtUtc = DateTimeOffset.UtcNow;
        receipt.EntityType = result.EntityType;
        receipt.EntityId = result.EntityId;
        receipt.ResponseJson = JsonSerializer.Serialize(result.Response, JsonOptions);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsActiveChannelBatchConflict(ex))
        {
            throw new BusinessRuleException("active_channel_batch_exists", "Drawer already has an active channel batch.", StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException ex) when (IsActiveSlideSlotConflict(ex))
        {
            throw new BusinessRuleException("slot_not_idle", "Selected slot is not idle.", StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException ex) when (IsDefaultWorkflowConflict(ex))
        {
            throw new BusinessRuleException("default_workflow_conflict", "Another request changed the default workflow. Refresh and retry.", StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException ex) when (IsDabPositionConflict(ex))
        {
            throw new BusinessRuleException("dab_position_occupied", "The selected DAB mix position was occupied by another command.", StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException ex) when (IsCoordinateActiveVersionConflict(ex))
        {
            throw new BusinessRuleException(
                "coordinate_active_version_conflict",
                "Another command activated a coordinate version for this profile. Refresh and retry.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException ex) when (IsLiquidClassEnabledVersionConflict(ex))
        {
            throw new BusinessRuleException(
                "liquid_class_enabled_version_conflict",
                "Another command enabled a Liquid Class version for this profile. Refresh and retry.",
                StatusCodes.Status409Conflict);
        }

        return result.Response;
    }

    private static string RequireCommandId(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            throw new BusinessRuleException("command_id_required", "commandId is required.", StatusCodes.Status400BadRequest);
        }

        return commandId.Trim();
    }

    private static string HashRequest(string operation, object request)
    {
        var json = JsonSerializer.Serialize(new { operation, request }, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static bool IsActiveChannelBatchConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("channel_batches.drawer_id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ix_channel_batches_drawer_id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveSlideSlotConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("slide_tasks.physical_slot_id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ix_slide_tasks_physical_slot_id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultWorkflowConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("workflow_versions.default_experiment_type", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ix_workflow_versions_default_experiment_type", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDabPositionConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("dab_batches.dab_mix_position_id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ix_dab_batches_dab_mix_position_id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dab_mix_positions.active_dab_batch_id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoordinateActiveVersionConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("UX_coordinate_profile_versions_profile_active", StringComparison.OrdinalIgnoreCase)
            || message.Contains("coordinate_profile_versions.coordinate_profile_id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLiquidClassEnabledVersionConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("liquid_class_versions.liquid_class_profile_id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ix_liquid_class_versions_liquid_class_profile_id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("liquid_class_profiles.enabled_version_id", StringComparison.OrdinalIgnoreCase);
    }

    private static T MarkReplay<T>(T response)
        where T : class
    {
        return response switch
        {
            UserMutationResponse x => x with { Replayed = true } as T ?? response,
            WorkflowDraftMutationResponse x => x with { Replayed = true } as T ?? response,
            DefaultWorkflowVersionResponse x => x with { Replayed = true } as T ?? response,
            ChannelBatchWorkflowResponse x => x with { Replayed = true } as T ?? response,
            ChannelBatchActivationResponse x => x with { Replayed = true } as T ?? response,
            TaskCreationResponse x => x with { Replayed = true } as T ?? response,
            SampleScanSessionResponse x => x with { Replayed = true } as T ?? response,
            MockLisQueryResponse x => x with { Replayed = true } as T ?? response,
            MockDemoDataResponse x => x with { Replayed = true } as T ?? response,
            DabBatchResponse x => x with { Replayed = true } as T ?? response,
            ReagentScanConfirmationResponse x => x with { Replayed = true } as T ?? response,
            ReagentScanSessionMutationResponse x => x with { Replayed = true } as T ?? response,
            AlarmMutationResponse x => x with { Replayed = true } as T ?? response,
            DeviceModeChangeResponse x => x with { Replayed = true } as T ?? response,
            DeviceInitializationResponse x => x with { Replayed = true } as T ?? response,
            DeviceFaultMutationResponse x => x with { Replayed = true } as T ?? response,
            ThermalMutationResponse x => x with { Replayed = true } as T ?? response,
            FluidicsMutationResponse x => x with { Replayed = true } as T ?? response,
            WaterSupplyMutationResponse x => x with { Replayed = true } as T ?? response,
            DatabaseBackupResponse x => x with { Replayed = true } as T ?? response,
            DatabaseRestoreResponse x => x with { Replayed = true } as T ?? response,
            EngineeringSessionResponse x => x with { Replayed = true } as T ?? response,
            EngineeringWriteResponse x => x with { Replayed = true } as T ?? response,
            EngineeringPipettingTestResponse x => x with { Replayed = true } as T ?? response,
            DigitalTwinCoordinateImportResponse x => x with { Replayed = true } as T ?? response,
            CoordinateProfileVersionMutationResponse x => x with { Replayed = true } as T ?? response,
            LiquidClassVersionMutationResponse x => x with { Replayed = true } as T ?? response,
            ScannerConfigurationMutationResponse x => x with { Replayed = true } as T ?? response,
            SerialConnectionMutationResponse x => x with { Replayed = true } as T ?? response,
            PrecisionCalibrationMutationResponse x => x with { Replayed = true } as T ?? response,
            MixerParameterMutationResponse x => x with { Replayed = true } as T ?? response,
            WashValveConfigMutationResponse x => x with { Replayed = true } as T ?? response,
            AppSettingsMutationResponse x => x with { Replayed = true } as T ?? response,
            ReagentPositionConfigMutationResponse x => x with { Replayed = true } as T ?? response,
            CoordinatePointHardwareActionResponse x => x with { Replayed = true } as T ?? response,
            RobotArmHardwareActionResponse x => x with { Replayed = true } as T ?? response,
            ScannerControlResponse x => x with { Replayed = true } as T ?? response,
            ReagentCoordinateGenerationResultResponse x => x with { Replayed = true } as T ?? response,
            MachineRunResponse x => x with { Replayed = true } as T ?? response,
            RunCommandResponse x => x with { Replayed = true } as T ?? response,
            CommandResponse x => x with { Replayed = true } as T ?? response,
            MockRuntimeResetResponse x => x with { Replayed = true } as T ?? response,
            _ => response
        };
    }
}
