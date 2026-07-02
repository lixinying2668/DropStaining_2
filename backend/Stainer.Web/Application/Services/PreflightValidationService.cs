using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class PreflightValidationService(
    StainerDbContext dbContext,
    DeviceInitializationService deviceInitializationService,
    ThermalControlService thermalControlService,
    FluidicsControlService fluidicsControlService,
    MotionControlService motionControlService)
{
    public async Task<PreflightValidationReportResponse> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var issues = new List<PreflightValidationIssueResponse>();
        var initialization = await deviceInitializationService.GetLatestAsync(cancellationToken);
        if (!initialization.Ok)
        {
            issues.Add(new PreflightValidationIssueResponse(
                "Device",
                "device_initialization_required",
                $"Device initialization is not ready for the current mode. Status={initialization.Status}."));
        }
        var thermalReadiness = await thermalControlService.GetReadinessAsync(cancellationToken);
        if (!thermalReadiness.Ok)
        {
            issues.Add(new PreflightValidationIssueResponse("Thermal", thermalReadiness.ErrorCode!, thermalReadiness.Message));
        }
        var fluidicsReadiness = await fluidicsControlService.GetReadinessAsync(cancellationToken);
        if (!fluidicsReadiness.Ok)
        {
            issues.Add(new PreflightValidationIssueResponse("Fluidics", fluidicsReadiness.ErrorCode!, fluidicsReadiness.Message));
        }
        var motionReadiness = await motionControlService.GetReadinessAsync(cancellationToken);
        if (!motionReadiness.Ok)
        {
            issues.Add(new PreflightValidationIssueResponse("Motion", motionReadiness.ErrorCode!, motionReadiness.Message));
        }
        var tasks = await dbContext.StainingTasks
            .AsNoTracking()
            .Where(x => x.Status == StainingTaskStatus.Confirmed)
            .ToListAsync(cancellationToken);

        if (tasks.Count == 0)
        {
            issues.Add(new PreflightValidationIssueResponse("Tasks", "no_confirmed_tasks", "No confirmed staining tasks were found."));
        }

        var taskIds = tasks.Select(x => x.Id).ToList();
        var slideTasks = await dbContext.SlideTasks
            .AsNoTracking()
            .Include(x => x.StainingTask)
            .Include(x => x.ChannelBatch)
            .ThenInclude(x => x!.SelectedWorkflowVersion)
            .ThenInclude(x => x!.WorkflowDefinition)
            .Where(x => taskIds.Contains(x.StainingTaskId))
            .ToListAsync(cancellationToken);
        var slideTaskIds = slideTasks.Select(x => x.StainingTaskId).ToHashSet(StringComparer.Ordinal);
        foreach (var task in tasks.Where(x => !slideTaskIds.Contains(x.Id)))
        {
            issues.Add(new PreflightValidationIssueResponse("Workflow", "channel_batch_required", $"Task {task.TaskCode} is not assigned to a channel batch."));
        }

        var channelBatches = slideTasks
            .Select(x => x.ChannelBatch)
            .Where(x => x is not null)
            .Cast<ChannelBatch>()
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();
        foreach (var batch in channelBatches.OrderBy(x => x.DrawerCode))
        {
            if (batch.WorkflowSelectionStatus == WorkflowSelectionStatus.NeedsManualResolution)
            {
                issues.Add(new PreflightValidationIssueResponse("Workflow", "channel_batch_needs_manual_resolution", $"Channel {batch.DrawerCode} needs manual workflow resolution."));
                continue;
            }

            if (batch.WorkflowSelectionStatus is not (WorkflowSelectionStatus.Selected or WorkflowSelectionStatus.Locked)
                || string.IsNullOrWhiteSpace(batch.SelectedWorkflowVersionId)
                || string.IsNullOrWhiteSpace(batch.WorkflowSnapshotJson)
                || batch.WorkflowSnapshotJson == "{}")
            {
                issues.Add(new PreflightValidationIssueResponse("Workflow", "channel_workflow_required", $"Channel {batch.DrawerCode} has no selected workflow."));
                continue;
            }

            var batchSlides = slideTasks.Where(x => x.ChannelBatchId == batch.Id).ToList();
            if (batchSlides.Count is < 1 or > 4)
            {
                issues.Add(new PreflightValidationIssueResponse("Tasks", "drawer_batch_size_invalid", $"Channel {batch.DrawerCode} must contain 1 to 4 slides."));
            }

            if (batchSlides.Any(x => x.TaskType != batch.ExperimentType || x.StainingTask?.TaskType != batch.ExperimentType))
            {
                issues.Add(new PreflightValidationIssueResponse("Workflow", "channel_experiment_type_mismatch", $"Channel {batch.DrawerCode} contains slides that do not match its experiment type."));
            }

            if (batchSlides.Any(x => x.StainingTask?.WorkflowVersionId != batch.SelectedWorkflowVersionId))
            {
                issues.Add(new PreflightValidationIssueResponse("Workflow", "channel_workflow_mismatch", $"Channel {batch.DrawerCode} contains slides whose compatibility copy does not match the channel workflow."));
            }

            if (batch.ExperimentType == StainingTaskType.Ihc)
            {
                var antibodyCodes = batchSlides
                    .Select(x => x.StainingTask?.PrimaryAntibodyCode)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .Distinct()
                    .ToList();
                var missingAntibodyCount = batchSlides.Count(x => string.IsNullOrWhiteSpace(x.StainingTask?.PrimaryAntibodyCode));
                if (missingAntibodyCount > 0)
                {
                    issues.Add(new PreflightValidationIssueResponse("Workflow", "primary_antibody_required", $"Channel {batch.DrawerCode} has {missingAntibodyCount} IHC slide(s) without primary antibody code."));
                }

                var compatibleCodes = await dbContext.PrimaryAntibodyWorkflowMappings
                    .AsNoTracking()
                    .Where(x => x.IsEnabled
                        && x.WorkflowVersionId == batch.SelectedWorkflowVersionId
                        && antibodyCodes.Contains(x.PrimaryAntibodyCode))
                    .Select(x => x.PrimaryAntibodyCode)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                var incompatible = antibodyCodes.Except(compatibleCodes, StringComparer.OrdinalIgnoreCase).ToList();
                if (incompatible.Count > 0)
                {
                    issues.Add(new PreflightValidationIssueResponse("Workflow", "channel_workflow_incompatible", $"Channel {batch.DrawerCode} has incompatible primary antibody code(s): {string.Join(", ", incompatible)}."));
                }
            }
        }

        var workflowVersionIds = channelBatches
            .Select(x => x.SelectedWorkflowVersionId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct()
            .ToList();
        var workflowVersions = await dbContext.WorkflowVersions
            .AsNoTracking()
            .Include(x => x.WorkflowDefinition)
            .Where(x => workflowVersionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var batch in channelBatches.Where(x => !string.IsNullOrWhiteSpace(x.SelectedWorkflowVersionId)))
        {
            if (!workflowVersions.TryGetValue(batch.SelectedWorkflowVersionId!, out var version))
            {
                issues.Add(new PreflightValidationIssueResponse("Workflow", "workflow_version_missing", $"Workflow version is missing for channel {batch.DrawerCode}."));
            }
            else if (version.Status != WorkflowVersionStatus.Published)
            {
                issues.Add(new PreflightValidationIssueResponse("Workflow", "workflow_version_not_published", $"Workflow version is not published for channel {batch.DrawerCode}."));
            }
            else if (version.WorkflowDefinition?.WorkflowType != batch.ExperimentType)
            {
                issues.Add(new PreflightValidationIssueResponse("Workflow", "channel_experiment_type_mismatch", $"Workflow version type does not match channel {batch.DrawerCode}."));
            }
        }

        var scanSessions = await dbContext.ReagentScanSessions
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var latestScan = scanSessions
            .OrderByDescending(x => x.CompletedAtUtc ?? x.StartedAtUtc)
            .FirstOrDefault();
        if (latestScan is null)
        {
            issues.Add(new PreflightValidationIssueResponse("Reagents", "scan_missing", "No reagent scan confirmation was found."));
        }
        else
        {
            var invalidItems = await dbContext.ReagentScanItems
                .AsNoTracking()
                .Where(x => x.ReagentScanSessionId == latestScan.Id && x.ScanResult == ReagentScanResult.Invalid)
                .CountAsync(cancellationToken);
            if (invalidItems > 0)
            {
                issues.Add(new PreflightValidationIssueResponse("Reagents", "scan_has_invalid_items", $"Latest reagent scan contains {invalidItems} invalid item(s)."));
            }

            var positionCount = await dbContext.ReagentRackPositions
                .AsNoTracking()
                .CountAsync(cancellationToken);
            var scannedPositionCount = await dbContext.ReagentScanItems
                .AsNoTracking()
                .Where(x => x.ReagentScanSessionId == latestScan.Id)
                .Select(x => x.ReagentRackPositionId)
                .Distinct()
                .CountAsync(cancellationToken);
            if (scannedPositionCount < positionCount)
            {
                issues.Add(new PreflightValidationIssueResponse(
                    "Reagents",
                    "scan_has_unscanned_positions",
                    $"Latest reagent scan has {positionCount - scannedPositionCount} unscanned position(s).",
                    "Warning"));
            }
        }

        var requirements = await dbContext.WorkflowReagentRequirements
            .AsNoTracking()
            .Where(x => workflowVersionIds.Contains(x.WorkflowVersionId) && x.IsRequired)
            .GroupBy(x => x.ReagentCode)
            .Select(x => new
            {
                ReagentCode = x.Key,
                RequiredVolumeUl = x.Sum(r => r.RequiredVolumeUl ?? 0)
            })
            .ToListAsync(cancellationToken);

        if (requirements.Count > 0)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var bottles = await dbContext.ReagentRackPlacements
                .AsNoTracking()
                .Where(x => x.RemovedAtUtc == null)
                .Include(x => x.ReagentBottle)
                .Select(x => x.ReagentBottle!)
                .Where(x => x.Status == "Available")
                .ToListAsync(cancellationToken);

            foreach (var requirement in requirements)
            {
                var matching = bottles.Where(x => x.ReagentCode == requirement.ReagentCode).ToList();
                if (matching.Count == 0)
                {
                    issues.Add(new PreflightValidationIssueResponse("Reagents", "required_reagent_missing", $"Required reagent is missing: {requirement.ReagentCode}."));
                    continue;
                }

                if (matching.Any(x => x.ExpirationDate < today))
                {
                    issues.Add(new PreflightValidationIssueResponse("Reagents", "required_reagent_expired", $"Required reagent is expired: {requirement.ReagentCode}."));
                }

                var availableVolume = matching.Where(x => x.ExpirationDate >= today).Sum(x => x.RemainingVolumeUl);
                if (availableVolume < requirement.RequiredVolumeUl)
                {
                    issues.Add(new PreflightValidationIssueResponse(
                        "Reagents",
                        "required_reagent_volume_insufficient",
                        $"Required reagent volume is insufficient: {requirement.ReagentCode}, required {requirement.RequiredVolumeUl} ul, available {availableVolume} ul."));
                }
            }
        }

        var failCount = issues.Count(x => !x.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));
        var warningCount = issues.Count - failCount;
        var stateHash = await BuildStateHashAsync(cancellationToken);
        return new PreflightValidationReportResponse(
            failCount == 0,
            tasks.Count,
            failCount,
            issues,
            generatedAtUtc,
            Guid.NewGuid().ToString("N"),
            failCount == 0,
            warningCount,
            stateHash);
    }

    private async Task<string> BuildStateHashAsync(CancellationToken cancellationToken)
    {
        var tasks = await dbContext.StainingTasks
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.WorkflowVersionId,
                x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
        var batches = await dbContext.ChannelBatches
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.WorkflowSelectionStatus,
                x.SelectedWorkflowVersionId,
                x.WorkflowLockedAtUtc,
                x.NeedsManualResolution
            })
            .ToListAsync(cancellationToken);
        var scanSessions = await dbContext.ReagentScanSessions
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.StartedAtUtc,
                x.CompletedAtUtc
            })
            .ToListAsync(cancellationToken);
        var scanItems = await dbContext.ReagentScanItems
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.ReagentScanSessionId,
                x.ReagentRackPositionId,
                x.ScanResult,
                x.RawBarcode,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
        var bottles = await dbContext.ReagentBottles
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.ReagentCode,
                x.Status,
                x.RemainingVolumeUl,
                x.ExpirationDate,
                x.UpdatedAtUtc,
                x.LastScannedAtUtc
            })
            .ToListAsync(cancellationToken);
        var placements = await dbContext.ReagentRackPlacements
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.ReagentBottleId,
                x.ReagentRackPositionId,
                x.ReagentScanSessionId,
                x.RemovedAtUtc
            })
            .ToListAsync(cancellationToken);
        var initializationRows = await dbContext.DeviceInitializationRuns
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.DeviceMode,
                x.AdapterName,
                x.AttemptNo,
                x.CreatedAtUtc,
                x.CompletedAtUtc
            })
            .ToListAsync(cancellationToken);
        var initialization = initializationRows
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        var thermalPoints = await dbContext.ThermalPointStates
            .AsNoTracking()
            .OrderBy(x => x.BoardNo).ThenBy(x => x.PointNo)
            .Select(x => new { x.Id, x.CurrentTemperatureDeciC, x.TargetTemperatureDeciC, x.IsEnabled, x.IsConnected, x.Status, x.FaultCode, x.UpdatedAtUtc })
            .ToListAsync(cancellationToken);
        var cooling = await dbContext.CoolingUnitStates
            .AsNoTracking()
            .Select(x => new { x.Id, x.CurrentTemperatureDeciC, x.TargetTemperatureDeciC, x.IsEnabled, x.IsConnected, x.Status, x.FaultCode, x.UpdatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        var pumps = await dbContext.PumpChannelStates
            .AsNoTracking()
            .OrderBy(x => x.PwmChannelNo)
            .Select(x => new { x.Id, x.PwmChannelCode, x.DrawerCode, x.SpeedPercent, x.Direction, x.Status, x.IsConnected, x.FaultCode, x.UpdatedAtUtc })
            .ToListAsync(cancellationToken);
        var mixers = await dbContext.MixerChannelStates
            .AsNoTracking()
            .OrderBy(x => x.ChannelNo)
            .Select(x => new { x.Id, x.DrawerCode, x.Status, x.IsConnected, x.FaultCode, x.UpdatedAtUtc })
            .ToListAsync(cancellationToken);
        var liquidLevels = await dbContext.LiquidContainerStates
            .AsNoTracking()
            .OrderBy(x => x.SourceType)
            .Select(x => new { x.Id, x.SourceType, x.CurrentVolumeUl, x.CapacityUl, x.LevelStatus, x.IsConnected, x.FaultCode, x.UpdatedAtUtc })
            .ToListAsync(cancellationToken);
        var robotArm = await dbContext.RobotArmStates
            .AsNoTracking()
            .Select(x => new { x.Id, x.IsHomed, x.IsConnected, x.Status, x.CurrentTargetPointCode, x.CoordinateProfileVersionId, x.LastErrorCode, x.UpdatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        var needles = await dbContext.NeedleStates
            .AsNoTracking()
            .OrderBy(x => x.NeedleNo)
            .Select(x => new { x.Id, x.NeedleCode, x.IsConnected, x.Status, x.LoadedSourceType, x.LoadedReagentCode, x.SourceBottleId, x.DabBatchId, x.VolumeUl, x.LiquidClassVersionId, x.LiquidClassVersionNo, x.NeedsWash, x.LastErrorCode, x.UpdatedAtUtc })
            .ToListAsync(cancellationToken);

        var json = JsonSerializer.Serialize(new { tasks, batches, scanSessions, scanItems, bottles, placements, initialization, thermalPoints, cooling, pumps, mixers, liquidLevels, robotArm, needles });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
