namespace Stainer.Web.Application.Services;

using Stainer.Web.Domain.Entities;

/// <summary>
/// Mock 运行时重置使用的唯一设备状态基线值来源。
/// 所有值都来自初始化时的 seeder 实际写入逻辑（与"Mock 刚初始化、未入库/扫码/运行/预检"
/// 的运行时状态保持一致），禁止在 <see cref="MockRuntimeResetService"/> 中散落硬编码默认值。
/// 任何字段值的变更必须先确认对应 seeder 已更新，并同步修改此处的出处注释。
/// </summary>
/// <remarks>
/// 关键约定：
/// - seeder 仅创建实体并填充外键/标识字段，其余字段使用实体类属性初始化器（entity defaults）。
///   因此"基线值"= entity defaults（除非 seeder 显式覆盖，例如液体容器的容量）。
/// - §2.4 中描述的个别字段（如 thermal_point_states.Status=''、IsConnected=false）与
///   seeder 实际写入不一致时，按红线"以 seeder 实际为准"取实体默认值，并在 reset 汇报中列出。
/// </remarks>
internal static class MockDeviceBaseline
{
    // ---------- thermal_point_states ----------
    // Seeder: ThermalControlService.EnsureSeededCoreAsync (ThermalControlService.cs:412-441)
    // Seeder 仅 set DrawerCode/BoardNo/SlotNo/PointNo/UpdatedAtUtc，其余字段走 ThermalPointState 实体默认值
    // (ThermalEntities.cs:10-14)：Current/TargetTemperatureDeciC=250, IsEnabled=false, IsConnected=true,
    // Status=ThermalStatuses.Off, FaultCode/FaultMessage=NULL。
    public const int ThermalCurrentTemperatureDeciC = 250;
    public const int ThermalTargetTemperatureDeciC = 250;
    public const bool ThermalIsEnabled = false;
    public const bool ThermalIsConnected = true;
    public const string ThermalStatus = ThermalStatuses.Off;
    // FaultCode/FaultMessage 基线为 NULL（直接在 SQL 里 SET NULL，无需常量）。

    // ---------- cooling_unit_states ----------
    // Seeder: ThermalControlService.EnsureSeededCoreAsync (ThermalControlService.cs:443-446)
    // 直接 new CoolingUnitState()，全部使用实体默认值 (ThermalEntities.cs:25-31)
    public const int CoolingCurrentTemperatureDeciC = 80;
    public const int CoolingTargetTemperatureDeciC = 80;
    public const bool CoolingIsEnabled = true;
    public const bool CoolingIsConnected = true;
    public const string CoolingStatus = ThermalStatuses.Stable;

    // ---------- pump_channel_states ----------
    // Seeder: FluidicsControlService.EnsureSeededCoreAsync (FluidicsControlService.cs:886-899)
    // 仅 set PwmChannelCode/PwmChannelNo/DrawerCode/UpdatedAtUtc，其余走 PumpChannelState 实体默认值
    // (FluidicsEntities.cs:9-21)：SpeedPercent=0, Direction=Stopped, Status=Idle, IsConnected=true,
    // 各在途/错误字段 NULL。
    public const int PumpSpeedPercent = 0;
    public const string PumpDirection = PumpDirections.Stopped;
    public const string PumpStatus = FluidicsStatuses.Idle;
    public const bool PumpIsConnected = true;

    // ---------- mixer_channel_states ----------
    // Seeder: FluidicsControlService.EnsureSeededCoreAsync (FluidicsControlService.cs:901-913)
    // 仅 set DrawerCode/ChannelNo/UpdatedAtUtc，其余走 MixerChannelState 实体默认值
    // (FluidicsEntities.cs:29-37)：Status=Idle, IsConnected=true, 各在途/错误字段 NULL。
    public const string MixerStatus = FluidicsStatuses.Idle;
    public const bool MixerIsConnected = true;

    // ---------- water_supply_channel_states ----------
    // Seeder: WaterSupplyControlService.EnsureSeededCoreAsync（4 个通道 CH1..CH4，每通道基线相同）。
    // inlet=25.0℃(250), outlet target=45.0℃(450), outlet=25.0℃(250), volume=0ml, flow=250ml/min,
    // outletEnabled=false, status=Idle, isConnected=true, fault/command=NULL。
    public const int WaterInletTemperatureDeciC = 250;
    public const int WaterOutletTargetTemperatureDeciC = 450;
    public const int WaterOutletTemperatureDeciC = 250;
    public const int WaterOutletVolumeMl = 0;
    public const int WaterOutletFlowRateMlPerMinute = 250;
    public const bool WaterOutletEnabled = false;
    public const string WaterStatus = WaterSupplyStatuses.Idle;
    public const bool WaterIsConnected = true;

    // ---------- liquid_container_states ----------
    // Seeder: FluidicsControlService.EnsureSeededCoreAsync (FluidicsControlService.cs:915-922) 通过
    // NewLiquid(...) 工厂显式注入容量与阈值。LevelStatus/IsConnected 走 LiquidContainerState 实体默认值
    // (FluidicsEntities.cs:51-54)：LevelStatus=Normal, IsConnected=true, FaultCode/FaultMessage=NULL。
    public const string LiquidLevelStatus = LiquidLevelStatuses.Normal;
    public const bool LiquidIsConnected = true;

    public sealed record LiquidBaseline(
        string SourceType,
        string DisplayName,
        bool IsWaste,
        int CapacityUl,
        int CurrentVolumeUl,
        int LowThresholdUl,
        int FullThresholdUl);

    /// <summary>四种液体的 seeder 容量/余量基线。来源：FluidicsControlService.cs:918-922 的 NewLiquid 调用。</summary>
    public static readonly IReadOnlyList<LiquidBaseline> Liquids =
    [
        new LiquidBaseline(LiquidSourceTypes.SystemWater, "Water",    false, 1_000_000, 850_000, 100_000, 900_000),
        new LiquidBaseline(LiquidSourceTypes.Pbs,         "PBS",      false, 1_000_000, 850_000, 100_000, 900_000),
        new LiquidBaseline(LiquidSourceTypes.Waste,       "Waste",    true,  1_000_000, 100_000, 100_000, 900_000),
        new LiquidBaseline(LiquidSourceTypes.ToxicWaste,  "Toxic waste", true,  500_000,  50_000,  50_000, 450_000),
    ];

    // ---------- robot_arm_states ----------
    // Seeder: MotionControlService.EnsureSeededCoreAsync (MotionControlService.cs:422-425)
    // 显式 set IsHomed=false, Status=Idle；位置/命令/错误字段走 RobotArmState 实体默认值 NULL
    // (MotionEntities.cs:7-19)；IsConnected 走实体默认 true。
    public const bool RobotArmIsHomed = false;
    public const string RobotArmStatus = MotionStatuses.Idle;
    public const bool RobotArmIsConnected = true;

    // ---------- needle_states ----------
    // Seeder: MotionControlService.EnsureSeededCoreAsync (MotionControlService.cs:427-432) 仅 set
    // NeedleCode/NeedleNo/UpdatedAtUtc。LoadedSourceType 走 NeedleState 实体默认
    // (MotionEntities.cs:30) = NeedleLoadSourceTypes.Empty（**注意：旧 reset 写入的 'None' 是 bug**）。
    // 其余 loaded/FK/错误字段走实体默认 NULL，VolumeUl=0, NeedsWash=false, Status=Idle, IsConnected=true,
    // LiquidClassParametersJson='{}'。
    public const string NeedleLoadedSourceType = NeedleLoadSourceTypes.Empty;
    public const string NeedleStatus = MotionStatuses.Idle;
    public const bool NeedleIsConnected = true;
    public const int NeedleVolumeUl = 0;
    public const bool NeedleNeedsWash = false;
    public const string NeedleLiquidClassParametersJson = "{}";

    // ---------- dab_mix_positions ----------
    // Seeder: ReferenceDataSeeder.cs:240-252 仅 set Code/PositionNo/CreatedAtUtc。
    // ActiveDabBatchId 实体默认 NULL (DabMixPosition.cs:10)。§2.4 明确"Status 默认 'Available' 不变"——
    // reset 不触碰 Status/IsEnabled，只清 ActiveDabBatchId。
    // 此处仅作注释说明，无新增常量（仅 SET active_dab_batch_id = NULL）。
}
