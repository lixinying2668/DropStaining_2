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
    MotionControlService motionControlService,
    DevicePrecheckService devicePrecheckService,
    DeviceModeService deviceModeService,
    WorkflowPrimaryAntibodyResolver workflowPrimaryAntibodyResolver)
{
    public async Task<PreflightValidationReportResponse> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var issues = new List<PreflightValidationIssueResponse>();
        // Twin/Mock uses its deterministic full precheck report as the device gate.
        // Hardware initialization and live readiness are mandatory only in Real mode.
        if (deviceModeService.IsReal)
        {
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
        }
        var tasks = await dbContext.StainingTasks
            .AsNoTracking()
            .Where(x => x.Status == StainingTaskStatus.Confirmed
                && dbContext.SlideTasks.Any(slide => slide.StainingTaskId == x.Id
                    && slide.ChannelBatch != null
                    && (slide.ChannelBatch.MachineRunId == null
                        || (slide.ChannelBatch.MachineRun != null && slide.ChannelBatch.MachineRun.Status == RuntimeLedgerStatus.Created))
                    && slide.ChannelBatch.WorkflowSelectionStatus == WorkflowSelectionStatus.Selected))
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
            .Include(x => x.ChannelBatch)
            .ThenInclude(x => x!.SelectedWorkflowVersion)
            .ThenInclude(x => x!.Steps)
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
                var missingAntibodyCount = batchSlides.Count(x => string.IsNullOrWhiteSpace(x.StainingTask?.PrimaryAntibodyCode));
                if (missingAntibodyCount > 0)
                {
                    issues.Add(new PreflightValidationIssueResponse("Workflow", "primary_antibody_required", $"Channel {batch.DrawerCode} has {missingAntibodyCount} IHC slide(s) without primary antibody code."));
                }

                // 主检查：任务冻结一抗须与所选染色流程解析出的一抗一致。新任务在创建时二者同源；
                // 历史脏数据（冻结值与流程解析值不一致）须被预检拦截。
                // 一抗是否可由本机提供，由“流程试剂需求 + 试剂扫描覆盖”校验（见下文 reagent 段）负责，
                // 不再依赖 primary_antibody_workflow_mappings 作为启动门禁——映射表仅用于查询/展示。
                var workflowPrimaryAntibodyCode = workflowPrimaryAntibodyResolver.ResolveCodeOrNull(batch.SelectedWorkflowVersion);
                if (!string.IsNullOrWhiteSpace(workflowPrimaryAntibodyCode))
                {
                    var mismatched = batchSlides
                        .Where(x => !string.IsNullOrWhiteSpace(x.StainingTask?.PrimaryAntibodyCode)
                            && !string.Equals(
                                x.StainingTask!.PrimaryAntibodyCode!.Trim(),
                                workflowPrimaryAntibodyCode,
                                StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.StainingTask?.PrimaryAntibodyCode ?? string.Empty)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (mismatched.Count > 0)
                    {
                        issues.Add(new PreflightValidationIssueResponse(
                            "Workflow",
                            "channel_workflow_incompatible",
                            $"通道 {batch.DrawerCode} 的染色流程一抗为 {workflowPrimaryAntibodyCode}，但存在冻结值不一致的玻片：{string.Join(", ", mismatched)}。"));
                    }
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
        var dabWorkflowVersionIds = await dbContext.WorkflowReagentRequirements
            .AsNoTracking()
            .Where(x => workflowVersionIds.Contains(x.WorkflowVersionId) && x.IsRequired && x.ReagentCode == "DAB")
            .Select(x => x.WorkflowVersionId)
            .Distinct()
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
            var fixedDabSources = await dbContext.ReagentBottles
                .AsNoTracking()
                .Where(x => (x.ReagentCode == "DBA" || x.ReagentCode == "DBB") && x.Status == "Available")
                .ToListAsync(cancellationToken);

            foreach (var requirement in requirements)
            {
                if (requirement.ReagentCode == "DAB")
                {
                    var requiredDabA = 0;
                    var requiredDabB = 0;
                    foreach (var group in slideTasks
                        .Where(x => x.ChannelBatch?.SelectedWorkflowVersionId is not null
                            && dabWorkflowVersionIds.Contains(x.ChannelBatch.SelectedWorkflowVersionId))
                        .GroupBy(x => x.ChannelBatch!.SelectedWorkflowVersionId!))
                    {
                        if (!workflowVersions.TryGetValue(group.Key, out var workflowVersion))
                        {
                            continue;
                        }

                        try
                        {
                            var ratio = DabLifecycleService.ReadDabRatio(workflowVersion.PlanningRulesJson);
                            var formula = DabFormula.CalculateRequired(
                                group.Select(x => x.StainingTaskId).Distinct().Count(),
                                ratio.A,
                                ratio.B,
                                ratio.Water);
                            requiredDabA = checked(requiredDabA + formula.DabAVolumeUl);
                            requiredDabB = checked(requiredDabB + formula.DabBVolumeUl);
                        }
                        catch (BusinessRuleException exception)
                        {
                            issues.Add(new PreflightValidationIssueResponse("Reagents", exception.Code, exception.Message));
                        }
                        catch (OverflowException)
                        {
                            issues.Add(new PreflightValidationIssueResponse("Reagents", "dab_volume_invalid", "Calculated DAB volume exceeds the supported integer range."));
                        }
                    }

                    foreach (var sourceRequirement in new[] { (Code: "DBA", VolumeUl: requiredDabA), (Code: "DBB", VolumeUl: requiredDabB) })
                    {
                        var matchingSources = fixedDabSources.Where(x => x.ReagentCode == sourceRequirement.Code && x.ExpirationDate >= today).ToList();
                        if (matchingSources.Sum(x => x.RemainingVolumeUl) < sourceRequirement.VolumeUl)
                        {
                            issues.Add(new PreflightValidationIssueResponse(
                                "Reagents",
                                "dab_source_missing_or_insufficient",
                                $"DAB source {sourceRequirement.Code} is missing, expired, or insufficient; required {sourceRequirement.VolumeUl} ul."));
                        }
                    }
                    continue;
                }

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
        var checks = await devicePrecheckService.EvaluateReadOnlyAsync(cancellationToken);
        // Ok 与 CanStart 统一为综合结果：业务 issues 无 Fail，且 11 项 blocking checks 全部 Passed。两者同时满足才视为整体通过/可启动。
        var canStart = failCount == 0 && checks.Count > 0 && checks.All(x => PrecheckStatuses.IsPassing(x.Status));
        return new PreflightValidationReportResponse(
            canStart,
            tasks.Count,
            failCount,
            issues,
            generatedAtUtc,
            Guid.NewGuid().ToString("N"),
            canStart,
            warningCount,
            stateHash,
            checks);
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
