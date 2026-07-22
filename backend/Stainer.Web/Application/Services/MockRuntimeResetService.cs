using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

/// <summary>
/// 把 Mock 运行时恢复到"刚初始化、未入库/扫码/运行/预检"状态。
/// 单事务（由 <see cref="CommandIdempotencyService.RunAsync"/> 持有）；任一删除/恢复失败→整体回滚。
/// 详见 <c>.omc/handoffs/mock-reset-design.md</c> §2 契约。
/// </summary>
public sealed class MockRuntimeResetService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    DeviceModeService deviceModeService,
    IRuntimeEventPublisher eventPublisher)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 活动运行状态（<see cref="RuntimeLedgerStatus"/>）— 出现任一状态时拒绝重置（见 §2.8）。
    /// </summary>
    // 仅当"真实执行中"（Running/Paused）才拒绝重置。Created/Pending/Faulted/WaitingUnload
    // 均允许重置——尤其 Faulted：Mock 运行故障后操作员需要靠"恢复初始状态"清理。
    // 任何仍被占用的硬件资源由下方 MachineResourceLease=Acquired 守卫独立拦截。
    private static readonly string[] BlockingRunStatuses =
    [
        RuntimeLedgerStatus.Running,
        RuntimeLedgerStatus.Paused,
    ];

    /// <summary>
    /// §2.3 实证后的最终删除顺序（子→父）。每一项的 FK 依据见行尾注释。
    /// 组内顺序无依赖；组间严格。FK 关系来源：StainerDbContextModelSnapshot.cs 5705-6660。
    /// </summary>
    private static readonly string[] DeleteOrder =
    [
        // ===== Phase A：叶子（无运行期子表依赖自身）=====
        "alarm_actions",                 // → alarms(Cascade IsRequired) [5715-5731]
        "dispense_executions",           // → device_command_executions(Cascade IsRequired) [6061-6077]
        "pipetting_operations",          // 无 FK（独立表）
        "reagent_consumptions",          // → reagent_bottles(Restrict IsRequired) [6268-6272]
                                         //   必须在 reagent_bottles 之前
        "system_liquid_usages",          // → dab_batches(Cascade IsRequired) [6514-6518]
                                         //   必须在 dab_batches 之前
        "reagent_reservations",          // → reagent_bottles(Restrict) [6362-6365]
        "dab_repreparation_plans",       // → dab_batches(Restrict IsRequired Expired + Restrict Replacement) [5984-5999]
                                         //   必须在 dab_batches 之前；→ machine_runs(Restrict) 必须在 machine_runs 之前
        "dab_batch_tasks",               // → staining_tasks(Restrict IsRequired) [5932-5936]
                                         //   必须在 staining_tasks 之前；→ dab_batches(Cascade)
        "dab_batch_usages",              // → staining_tasks(Restrict) [5961-5964] 必须在 staining_tasks 之前
        "temperature_telemetry",         // 无 FK
        "fluidics_telemetry",            // 无 FK
        "water_supply_telemetry",        // 无 FK
        "sample_scan_items",             // → sample_scan_sessions(Cascade IsRequired) [6405-6414]
        "reagent_scan_items",            // → reagent_scan_sessions(Cascade IsRequired) [6376-6393]
        "reagent_rack_placements",       // → reagent_bottles(Cascade IsRequired) [6319-6325]
                                         //   必须在 reagent_bottles 之前
        "workflow_assignment_history",   // → channel_batches(Restrict IsRequired) [6566-6589]
                                         //   必须在 channel_batches 之前
        "lis_query_logs",                // 仅 FK 到 user(SetNull)
        "machine_resource_leases",       // 无 FK
        "DevicePrecheckRuns",            // 无 FK；注意：此表在迁移里 ToTable("DevicePrecheckRuns")（PascalCase，项目唯一例外，非 snake_case）
        "device_communication_records",  // 无 FK
        "device_initialization_checks",  // → device_initialization_runs(Cascade IsRequired) [6033-6042]
        // ===== Phase B：中层 =====
        "workflow_step_executions",      // → workflow_executions(Cascade IsRequired) [6640-6649]
                                         //   必须在 workflow_executions 之前
        "device_command_executions",     // → machine_runs(Cascade IsRequired) + workflow_step_executions(SetNull) [6008-6031]
                                         //   必须在 machine_runs 之前；dispense_executions/system_liquid_usages
                                         //   /reagent_consumptions/dab_batch_usages 已在 Phase A 删除
        "alarms",                        // → machine_runs(Cascade) [5705-5713]；alarm_actions 已删
        "sample_scan_sessions",          // 仅 FK 到 user(SetNull)；items 已删
        "reagent_scan_sessions",         // 仅 FK 到 user(SetNull)；items + placements 已删
        "device_initialization_runs",    // 仅 FK 到 user(SetNull) + self-join RetryOfRun(Restrict)；checks 已删
        // ===== Phase C：分组（关键 Restrict 在此解决）=====
        "dab_batches",                   // 必须在 reagent_bottles 之前（DabA/DabB Restrict [5899-5907]）；
                                         //   dab_batch_tasks/usages/repreparation_plans、reagent_reservations、
                                         //   reagent_consumptions(SetNull)、system_liquid_usages 均已删
        "workflow_executions",           // → slide_tasks(Cascade IsRequired) [6591-6616] 必须在 slide_tasks 之前；
                                         //   → machine_runs(Cascade IsRequired) 必须在 machine_runs 之前；
                                         //   workflow_step_executions 已删
        "slide_tasks",                   // → staining_tasks(Restrict IsRequired) [6465-6469] 必须在 staining_tasks 之前；
                                         //   → channel_batches(Cascade IsRequired) 必须在 channel_batches 之前；
                                         //   workflow_executions 已删
        // ===== Phase D：上层（Restrict 依赖解除后）=====
        "staining_tasks",                // slide_tasks、dab_batch_tasks/usages 已删（Restrict 已解）；
                                         //   其余 FK 到 non-runtime 表（PhysicalSlot/WorkflowDefinition/Version Restrict 保留）
        "reagent_bottles",               // dab_batches、dispense_executions、reagent_consumptions、
                                         //   reagent_reservations、reagent_rack_placements 全已删
        // ===== Phase E：顶层 =====
        "channel_batches",               // 全部状态，不限 Active；slide_tasks、workflow_assignment_history 已删
        "machine_runs",                  // 最后删；被 alarms/workflow_executions/device_command_executions/
                                         //   dab_batch_usages/reagent_reservations/dab_repreparation_plans/
                                         //   reagent_consumptions/system_liquid_usages 引用，均已清理
    ];

    public async Task<MockRuntimeResetResponse> ResetAsync(
        RunCommandRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var response = await idempotencyService.RunAsync(
            request.CommandId,
            "mock_runtime.reset",
            request,
            actor,
            async () =>
            {
                // ① 模式门禁：非 Mock 拒绝（409）。在任何 SQL 之前抛出 → 无需回滚任何业务变更。
                if (!deviceModeService.IsMock)
                {
                    throw new BusinessRuleException(
                        "mock_runtime_reset_mode_required",
                        "Mock runtime reset is allowed only when DeviceMode=Mock.",
                        StatusCodes.Status409Conflict);
                }

                // ② 活动运行守卫（§2.8）：仅在"真实执行中"（Running/Paused）或资源锁仍占用时拒绝（409）。
                if (await dbContext.MachineRuns.AnyAsync(x => BlockingRunStatuses.Contains(x.Status), cancellationToken))
                {
                    throw new BusinessRuleException(
                        "mock_runtime_reset_active_run",
                        "存在正在执行的运行（Running/Paused），请先暂停或停止后再重置 Mock 运行时。",
                        StatusCodes.Status409Conflict);
                }
                if (await dbContext.MachineResourceLeases.AnyAsync(x => x.Status == MachineResourceLeaseStatus.Acquired, cancellationToken))
                {
                    throw new BusinessRuleException(
                        "mock_runtime_reset_active_run",
                        "存在已占用资源（MachineResourceLease=Acquired），请先释放后再重置 Mock 运行时。",
                        StatusCodes.Status409Conflict);
                }

                var deleted = 0;
                var reset = 0;

                try
                {
                    // ③ 顺序删除运行期表（Phase A-E）
                    foreach (var table in DeleteOrder)
                    {
                        deleted += await dbContext.Database.ExecuteSqlRawAsync(
                            "DELETE FROM " + table,
                            cancellationToken);
                    }

                    // ④ 运行期 audit 清理（§2.5）：以 EntityType(运行期实体) 为主、运行期 Action 前缀为辅，
                    //    覆盖 task.*/channel.*/channel_batch.*/channelbatch.*/dab.*/fluidics.*/run.*/resource.*/sample.*/reagent.scan*/mock_runtime.*/device.{communication,mock_fault,precheck,initialization}.*/alarm.*/startup.*/lis.*/thermal.point.*/mock.dab_source.scan；
                    //    保留账号/权限/流程配置/坐标/液体类别/设备档案/串口/精度/数据库/mock_demo seed 等配置类审计。
                    deleted += await DeleteRuntimeAuditAsync(cancellationToken);
                    // 保留本次回执（幂等服务在 SaveChanges 时落库新回执；此处仅清旧回执）。
                    deleted += await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM command_receipts WHERE command_id <> {request.CommandId}",
                        cancellationToken);

                    // ⑤ 设备状态恢复到 Mock 基线（§2.4；UPDATE 不删除记录）
                    reset += await RestoreThermalPointStatesAsync(cancellationToken);
                    reset += await RestoreCoolingUnitStateAsync(cancellationToken);
                    reset += await RestorePumpChannelStatesAsync(cancellationToken);
                    reset += await RestoreMixerChannelStatesAsync(cancellationToken);
                    reset += await RestoreLiquidContainerStatesAsync(cancellationToken);
                    reset += await RestoreWaterSupplyChannelStatesAsync(cancellationToken);
                    reset += await RestoreRobotArmStatesAsync(cancellationToken);
                    reset += await RestoreNeedleStatesAsync(cancellationToken);
                    reset += await RestoreDabMixPositionsAsync(cancellationToken);

                    // ⑥ W3 测试缝隙：设备状态恢复完成后、audit 写入前注入一次失败，
                    //    验证幂等服务事务 await-using 会整体回滚所有 DELETE/UPDATE。
                    //    用法：MockRuntimeResetService.TestFailureFactory = () => new InvalidOperationException("boom");
                    //          断言响应 code = mock_runtime_reset_failed 且运行期表内容未变。
                    var injected = TestFailureFactory?.Invoke();
                    if (injected is not null)
                    {
                        throw injected;
                    }

                    // ⑦ 新审计（§2.5）— Message 含 actor/时间/统计，供回溯。
                    dbContext.AuditLogs.Add(new AuditLog
                    {
                        ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
                        Action = "mock_runtime.reset",
                        EntityType = "MockRuntime",
                        Message = JsonSerializer.Serialize(new
                        {
                            actorUserId = actor.UserId,
                            occurredAtUtc = DateTimeOffset.UtcNow,
                            deletedRows = deleted,
                            resetStateRows = reset,
                        }, JsonOptions),
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    });
                }
                catch (Exception ex)
                {
                    // SQL/恢复失败：幂等服务事务 await-using 将回滚所有 DELETE/UPDATE。
                    // 模式/活动运行守卫异常已在 try 之前抛出，此处不会捕获到 BusinessRuleException。
                    throw new BusinessRuleException(
                        "mock_runtime_reset_failed",
                        $"Mock 运行时重置失败，事务已回滚：{ex.Message}",
                        StatusCodes.Status500InternalServerError);
                }

                var response = new MockRuntimeResetResponse(
                    true,
                    request.CommandId,
                    false,
                    $"Mock 运行时已重置。删除运行期记录 {deleted} 行，恢复设备状态记录 {reset} 行。",
                    deleted,
                    reset,
                    "保留：用户/角色、抽屉/槽位、试剂架位、流程定义/版本/步骤/需求、一抗映射、试剂定义、坐标/液体类别/串口/精度校准/扫描配置、设备档案、DAB 摆放位/清洗位、Mock LIS、Mock demo seed、非运行期 audit。");

                return new CommandExecutionResult<MockRuntimeResetResponse>(
                    response,
                    "MockRuntime",
                    "reset");
            },
            cancellationToken);

        // §2.7：事务提交成功后发布刷新通知（在 lambda 外、RunAsync 返回后）。
        if (response.Ok)
        {
            eventPublisher.Publish(MachineEventMessage.Create(
                MachineEventTypes.MockRuntimeReset,
                runId: null,
                entityType: "MockRuntime",
                entityId: response.CommandId,
                requiredRole: null,
                new Dictionary<string, object?>
                {
                    ["commandId"] = response.CommandId,
                    ["deletedRows"] = response.DeletedRows,
                    ["resetStateRows"] = response.ResetStateRows,
                }));
        }

        return response;
    }

    // ============== W3 测试缝隙 ==============
    /// <summary>
    /// 测试钩子：若返回非 null，将在设备状态恢复完成、audit 写入之前抛出该异常，
    /// 用于验证事务整体回滚（§3 W3 场景 8）。
    /// 测试用例结束必须置 null 以免污染其它用例。
    /// </summary>
    internal static Func<Exception?>? TestFailureFactory;

    // ============== 设备状态恢复（§2.4；值见 MockDeviceBaseline，注明 seeder 出处）==============

    private Task<int> RestoreThermalPointStatesAsync(CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE thermal_point_states
            SET current_temperature_deci_c = @p0,
                target_temperature_deci_c = @p1,
                is_enabled = @p2,
                is_connected = @p3,
                status = @p4,
                fault_code = NULL,
                fault_message = NULL,
                updated_at_utc = @p5
            """,
            [
                MockDeviceBaseline.ThermalCurrentTemperatureDeciC,
                MockDeviceBaseline.ThermalTargetTemperatureDeciC,
                MockDeviceBaseline.ThermalIsEnabled,
                MockDeviceBaseline.ThermalIsConnected,
                MockDeviceBaseline.ThermalStatus,
                DateTimeOffset.UtcNow,
            ],
            cancellationToken);
    }

    private Task<int> RestoreCoolingUnitStateAsync(CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE cooling_unit_states
            SET current_temperature_deci_c = @p0,
                target_temperature_deci_c = @p1,
                is_enabled = @p2,
                is_connected = @p3,
                status = @p4,
                fault_code = NULL,
                fault_message = NULL,
                updated_at_utc = @p5
            """,
            [
                MockDeviceBaseline.CoolingCurrentTemperatureDeciC,
                MockDeviceBaseline.CoolingTargetTemperatureDeciC,
                MockDeviceBaseline.CoolingIsEnabled,
                MockDeviceBaseline.CoolingIsConnected,
                MockDeviceBaseline.CoolingStatus,
                DateTimeOffset.UtcNow,
            ],
            cancellationToken);
    }

    private Task<int> RestorePumpChannelStatesAsync(CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE pump_channel_states
            SET speed_percent = @p0,
                direction = @p1,
                status = @p2,
                is_connected = @p3,
                target_point_code = NULL,
                duration_ms = NULL,
                current_command_id = NULL,
                machine_run_id = NULL,
                workflow_step_execution_id = NULL,
                device_command_execution_id = NULL,
                fault_code = NULL,
                fault_message = NULL,
                updated_at_utc = @p4
            """,
            [
                MockDeviceBaseline.PumpSpeedPercent,
                MockDeviceBaseline.PumpDirection,
                MockDeviceBaseline.PumpStatus,
                MockDeviceBaseline.PumpIsConnected,
                DateTimeOffset.UtcNow,
            ],
            cancellationToken);
    }

    private Task<int> RestoreMixerChannelStatesAsync(CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE mixer_channel_states
            SET status = @p0,
                is_connected = @p1,
                current_round_key = NULL,
                current_command_id = NULL,
                machine_run_id = NULL,
                workflow_step_execution_id = NULL,
                device_command_execution_id = NULL,
                fault_code = NULL,
                fault_message = NULL,
                updated_at_utc = @p2
            """,
            [
                MockDeviceBaseline.MixerStatus,
                MockDeviceBaseline.MixerIsConnected,
                DateTimeOffset.UtcNow,
            ],
            cancellationToken);
    }

    private async Task<int> RestoreLiquidContainerStatesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var total = 0;
        foreach (var liquid in MockDeviceBaseline.Liquids)
        {
            total += await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE liquid_container_states
                SET display_name = @p0,
                    is_waste = @p1,
                    capacity_ul = @p2,
                    current_volume_ul = @p3,
                    low_threshold_ul = @p4,
                    full_threshold_ul = @p5,
                    level_status = @p6,
                    is_connected = @p7,
                    fault_code = NULL,
                    fault_message = NULL,
                    updated_at_utc = @p8
                WHERE source_type = @p9
                """,
                [
                    liquid.DisplayName,
                    liquid.IsWaste,
                    liquid.CapacityUl,
                    liquid.CurrentVolumeUl,
                    liquid.LowThresholdUl,
                    liquid.FullThresholdUl,
                    MockDeviceBaseline.LiquidLevelStatus,
                    MockDeviceBaseline.LiquidIsConnected,
                    now,
                    liquid.SourceType,
                ],
                cancellationToken);
        }
        return total;
    }

    private Task<int> RestoreWaterSupplyChannelStatesAsync(CancellationToken cancellationToken)
    {
        // 4 个通道（CH1..CH4）基线相同：温度/水量/流速/开关/状态恢复为 seeder 初始值，
        // channel_no/channel_code 不变，fault/command 字段清空。
        return dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE water_supply_channel_states
            SET inlet_temperature_deci_c = @p0,
                outlet_target_temperature_deci_c = @p1,
                outlet_temperature_deci_c = @p2,
                outlet_volume_ml = @p3,
                outlet_flow_rate_ml_per_minute = @p4,
                outlet_enabled = @p5,
                status = @p6,
                is_connected = @p7,
                current_command_id = NULL,
                fault_code = NULL,
                fault_message = NULL,
                updated_at_utc = @p8
            """,
            [
                MockDeviceBaseline.WaterInletTemperatureDeciC,
                MockDeviceBaseline.WaterOutletTargetTemperatureDeciC,
                MockDeviceBaseline.WaterOutletTemperatureDeciC,
                MockDeviceBaseline.WaterOutletVolumeMl,
                MockDeviceBaseline.WaterOutletFlowRateMlPerMinute,
                MockDeviceBaseline.WaterOutletEnabled,
                MockDeviceBaseline.WaterStatus,
                MockDeviceBaseline.WaterIsConnected,
                DateTimeOffset.UtcNow,
            ],
            cancellationToken);
    }

    private Task<int> RestoreRobotArmStatesAsync(CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE robot_arm_states
            SET is_homed = @p0,
                is_connected = @p1,
                status = @p2,
                current_target_point_code = NULL,
                current_x_um = NULL,
                current_y_um = NULL,
                current_z_um = NULL,
                coordinate_profile_version_id = NULL,
                current_command_id = NULL,
                machine_run_id = NULL,
                workflow_step_execution_id = NULL,
                device_command_execution_id = NULL,
                last_error_code = NULL,
                last_error_message = NULL,
                updated_at_utc = @p3
            """,
            [
                MockDeviceBaseline.RobotArmIsHomed,
                MockDeviceBaseline.RobotArmIsConnected,
                MockDeviceBaseline.RobotArmStatus,
                DateTimeOffset.UtcNow,
            ],
            cancellationToken);
    }

    private Task<int> RestoreNeedleStatesAsync(CancellationToken cancellationToken)
    {
        // §2.4：loaded_source_type='None' 是旧实现的拼写错误——seeder 写入的实体默认值为
        // NeedleLoadSourceTypes.Empty='Empty'（MotionEntities.cs:30），此处按 seeder 基线修正。
        return dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE needle_states
            SET loaded_source_type = @p0,
                loaded_reagent_code = NULL,
                source_bottle_id = NULL,
                dab_batch_id = NULL,
                system_liquid_source_type = NULL,
                source_position_code = NULL,
                volume_ul = @p1,
                liquid_class_version_id = NULL,
                liquid_class_version_no = NULL,
                liquid_class_parameters_json = @p2,
                needs_wash = @p3,
                status = @p4,
                is_connected = @p5,
                current_command_id = NULL,
                machine_run_id = NULL,
                workflow_step_execution_id = NULL,
                device_command_execution_id = NULL,
                last_error_code = NULL,
                last_error_message = NULL,
                updated_at_utc = @p6
            """,
            [
                MockDeviceBaseline.NeedleLoadedSourceType,
                MockDeviceBaseline.NeedleVolumeUl,
                MockDeviceBaseline.NeedleLiquidClassParametersJson,
                MockDeviceBaseline.NeedleNeedsWash,
                MockDeviceBaseline.NeedleStatus,
                MockDeviceBaseline.NeedleIsConnected,
                DateTimeOffset.UtcNow,
            ],
            cancellationToken);
    }

    /// <summary>
    /// §2.5 删除运行期审计。主判据：EntityType 属于运行期实体；辅判据：Action 属于运行期前缀
    /// （覆盖 EntityType 可能为空/非标准的行）。两判据取并集，确保运行期审计清干净、无残留，
    /// 同时不误删账号/权限/流程配置/坐标/液体类别/设备档案/串口/精度/数据库/mock_demo seed 等配置类审计。
    /// </summary>
    private Task<int> DeleteRuntimeAuditAsync(CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM audit_logs
            WHERE entity_type IN (
                'MachineRun','ChannelBatch','StainingTask','SlideTask',
                'WorkflowExecution','WorkflowStepExecution','DeviceCommandExecution',
                'DabBatch','DabBatchTask','DabBatchUsage','DabRepreparationPlan',
                'ReagentBottle','ReagentScanSession','ReagentScanItem','ReagentRackPlacement',
                'ReagentReservation','ReagentConsumption','SystemLiquidUsage',
                'DispenseExecution','PipettingOperation','SampleScanSession','SampleScanItem',
                'Alarm','AlarmAction','DeviceCommunicationRecord','DevicePrecheckRun',
                'DeviceInitializationRun','DeviceInitializationCheck','MachineResourceLease',
                'LisQueryLog','MockRuntime','ThermalPointState','CoolingUnitState',
                'PumpChannelState','MixerChannelState','LiquidContainerState',
                'RobotArmState','NeedleState','DabMixPosition','DeviceFault',
                'TemperatureTelemetry','FluidicsTelemetry',
                'WaterSupplyChannelState','WaterSupplyTelemetry')
               OR action LIKE 'run.%'
               OR action LIKE 'resource.%'
               OR action LIKE 'sample.%'
               OR action LIKE 'reagent.scan%'
               OR action LIKE 'reagent.confirm%'
               OR action LIKE 'reagent.rescan%'
               OR action LIKE 'mock.dab_source.scan%'
               OR action LIKE 'channel.workflow.%'
               OR action LIKE 'channel.experiment_type.%'
               OR action LIKE 'channel_batch.%'
               OR action LIKE 'channelbatch.%'
               OR action LIKE 'task.create%'
               OR action LIKE 'dab.%'
               OR action LIKE 'fluidics.%'
               OR action LIKE 'water_supply.%'
               OR action LIKE 'thermal.point.%'
               OR action LIKE 'alarm.%'
               OR action LIKE 'device.communication.%'
               OR action LIKE 'device.mock_fault.%'
               OR action LIKE 'device.precheck.%'
               OR action LIKE 'device.initialization.%'
               OR action LIKE 'startup.%'
               OR action LIKE 'mock_runtime.%'
               OR action LIKE 'lis.%'
               OR action LIKE 'motion.%'
            """,
            cancellationToken);
    }

    private Task<int> RestoreDabMixPositionsAsync(CancellationToken cancellationToken)
    {
        // §2.4：仅清 active_dab_batch_id；Status/IsEnabled 不变（dab_mix_positions → dab_batches
        // 在快照里只是 HasIndex().IsUnique() 非 FK，但仍需在 dab_batches 删除前/或同事务内 NULL 化以保语义干净）。
        return dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE dab_mix_positions SET active_dab_batch_id = NULL",
            cancellationToken);
    }
}
