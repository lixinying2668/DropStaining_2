using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;

namespace Stainer.Web.Application.Services;

// 机械臂业务原子操作服务：封装染色机工艺中的基础动作。
// 每个原子动作按工艺规定的顺序编排底层 IRobotMotionPrimitives，并在结束时抬升回安全高度。
// 第一阶段只做业务封装（Mock），不接真实 SOCON，不改 SOCON Bridge。
public interface IRobotArmAtomicActionService
{
    /// <summary>吸液：MoveZ 到吸液高度 -> Aspirate -> MoveZ 回安全高度。</summary>
    Task<RobotArmAtomicActionResult> TakeLiquidAsync(TakeLiquidRequest request, CancellationToken cancellationToken = default);

    /// <summary>配液 / 混匀：MoveZ 到配液高度 -> Dispense -> MoveZ 回安全高度。</summary>
    Task<RobotArmAtomicActionResult> PrepareMixAsync(PrepareMixRequest request, CancellationToken cancellationToken = default);

    /// <summary>滴液：MoveZ 到滴液高度 -> Dispense -> MoveZ 回安全高度。</summary>
    Task<RobotArmAtomicActionResult> DispenseLiquidAsync(DispenseLiquidRequest request, CancellationToken cancellationToken = default);

    /// <summary>内壁清洗：MoveZ 到内壁清洗高度 -> Aspirate 清洗液 -> Dispense 废液 -> MoveZ 回安全高度。</summary>
    Task<RobotArmAtomicActionResult> WashInnerAsync(WashInnerRequest request, CancellationToken cancellationToken = default);

    /// <summary>外壁清洗：MoveZ 到外壁清洗高度 -> 执行外壁清洗 -> MoveZ 回安全高度。</summary>
    Task<RobotArmAtomicActionResult> WashOuterAsync(WashOuterRequest request, CancellationToken cancellationToken = default);
}

public sealed class RobotArmAtomicActionService(
    IRobotMotionPrimitives primitives,
    RobotArmAtomicHeights heights,
    IRobotArmAtomicActionRecorder? recorder = null) : IRobotArmAtomicActionService
{
    private readonly IRobotMotionPrimitives _primitives = primitives;
    private readonly RobotArmAtomicHeights _heights = heights;
    // 可选的 Mock 运行状态 / 流水账记录器。单测不传（保持纯顺序断言）；运行时由 DI 注入，
    // 让原子动作复用现有 RobotArmState / NeedleState / PipettingOperations 等 Mock 状态。
    private readonly IRobotArmAtomicActionRecorder? _recorder = recorder;

    public Task<RobotArmAtomicActionResult> TakeLiquidAsync(TakeLiquidRequest request, CancellationToken cancellationToken = default)
    {
        RequireCommandId(request.CommandId);
        RequirePositiveVolume(request.VolumeUl, nameof(request.VolumeUl));
        // 支持按调用指定吸液高度 / 安全高度（未指定则回退到 RobotArmAtomicHeights 配置）；XY 由调用方负责。
        var aspirateZUm = request.AspirateZUm ?? _heights.AspirateZUm;
        var safeZUm = request.SafeZUm ?? _heights.SafeZUm;
        var zAxis = ResolveZAxis(request.NeedleCode);
        // MoveZ(吸液高度) -> Aspirate；安全高度回零由 RunAsync 用本次 safeZUm 统一保证。
        return RunAsync(RobotAtomicActions.TakeLiquid, request.CommandId, request.NeedleCode, request.Reason,
            netVolumeUl: request.VolumeUl, clearsNeedle: false, zAxis, safeZUm, cancellationToken, async steps =>
        {
            await _primitives.MoveZAsync(zAxis, aspirateZUm, cancellationToken); steps.Add(Step("MoveZ→吸液高度", aspirateZUm));
            await _primitives.AspirateAsync(request.VolumeUl, cancellationToken); steps.Add(Step("Aspirate", request.VolumeUl));
        });
    }

    public Task<RobotArmAtomicActionResult> PrepareMixAsync(PrepareMixRequest request, CancellationToken cancellationToken = default)
    {
        RequireCommandId(request.CommandId);
        RequirePositiveVolume(request.VolumeUl, nameof(request.VolumeUl));
        // 支持按调用指定配液高度 / 安全高度（未指定则回退到 RobotArmAtomicHeights 配置）；XY 由调用方负责。
        var mixZUm = request.MixZUm ?? _heights.MixZUm;
        var safeZUm = request.SafeZUm ?? _heights.SafeZUm;
        var zAxis = ResolveZAxis(request.NeedleCode);
        // MoveZ(配液高度) -> Dispense；安全高度回零由 RunAsync 用本次 safeZUm 统一保证。
        return RunAsync(RobotAtomicActions.PrepareMix, request.CommandId, request.NeedleCode, request.Reason,
            netVolumeUl: -request.VolumeUl, clearsNeedle: false, zAxis, safeZUm, cancellationToken, async steps =>
        {
            await _primitives.MoveZAsync(zAxis, mixZUm, cancellationToken); steps.Add(Step("MoveZ→配液高度", mixZUm));
            await _primitives.DispenseAsync(request.VolumeUl, cancellationToken); steps.Add(Step("Dispense", request.VolumeUl));
        });
    }

    public Task<RobotArmAtomicActionResult> DispenseLiquidAsync(DispenseLiquidRequest request, CancellationToken cancellationToken = default)
    {
        RequireCommandId(request.CommandId);
        RequirePositiveVolume(request.VolumeUl, nameof(request.VolumeUl));
        // 支持按调用指定滴液高度 / 安全高度（未指定则回退到 RobotArmAtomicHeights 配置）；XY 由调用方负责。
        var dispenseZUm = request.DispenseZUm ?? _heights.DispenseZUm;
        var safeZUm = request.SafeZUm ?? _heights.SafeZUm;
        var zAxis = ResolveZAxis(request.NeedleCode);
        // MoveZ(滴液高度) -> Dispense；安全高度回零由 RunAsync 用本次 safeZUm 统一保证。
        return RunAsync(RobotAtomicActions.DispenseLiquid, request.CommandId, request.NeedleCode, request.Reason,
            netVolumeUl: -request.VolumeUl, clearsNeedle: false, zAxis, safeZUm, cancellationToken, async steps =>
        {
            await _primitives.MoveZAsync(zAxis, dispenseZUm, cancellationToken); steps.Add(Step("MoveZ→滴液高度", dispenseZUm));
            await _primitives.DispenseAsync(request.VolumeUl, cancellationToken); steps.Add(Step("Dispense", request.VolumeUl));
        });
    }

    public Task<RobotArmAtomicActionResult> WashInnerAsync(WashInnerRequest request, CancellationToken cancellationToken = default)
    {
        RequireCommandId(request.CommandId);
        RequirePositiveVolume(request.WashVolumeUl, nameof(request.WashVolumeUl));
        RequirePositiveVolume(request.WasteVolumeUl, nameof(request.WasteVolumeUl));
        // 支持按调用指定内壁清洗高度 / 安全高度（未指定则回退到 RobotArmAtomicHeights 配置）；XY 由调用方负责。
        var washInnerZUm = request.WashInnerZUm ?? _heights.WashInnerZUm;
        var safeZUm = request.SafeZUm ?? _heights.SafeZUm;
        var zAxis = ResolveZAxis(request.NeedleCode);
        // MoveZ(内壁清洗高度) -> Aspirate 清洗液 -> Dispense 废液；安全高度回零由 RunAsync 用本次 safeZUm 统一保证。
        return RunAsync(RobotAtomicActions.WashInner, request.CommandId, request.NeedleCode, request.Reason,
            netVolumeUl: 0, clearsNeedle: true, zAxis, safeZUm, cancellationToken, async steps =>
        {
            await _primitives.MoveZAsync(zAxis, washInnerZUm, cancellationToken); steps.Add(Step("MoveZ→内壁清洗高度", washInnerZUm));
            await _primitives.AspirateAsync(request.WashVolumeUl, cancellationToken); steps.Add(Step("Aspirate 清洗液", request.WashVolumeUl));
            await _primitives.DispenseAsync(request.WasteVolumeUl, cancellationToken); steps.Add(Step("Dispense 废液", request.WasteVolumeUl));
        });
    }

    public Task<RobotArmAtomicActionResult> WashOuterAsync(WashOuterRequest request, CancellationToken cancellationToken = default)
    {
        RequireCommandId(request.CommandId);
        // 支持按调用指定外壁清洗高度 / 安全高度（未指定则回退到 RobotArmAtomicHeights 配置）；XY 由调用方负责。
        var washOuterZUm = request.WashOuterZUm ?? _heights.WashOuterZUm;
        var safeZUm = request.SafeZUm ?? _heights.SafeZUm;
        var zAxis = ResolveZAxis(request.NeedleCode);
        // MoveZ(外壁清洗高度) -> 执行外壁清洗；安全高度回零由 RunAsync 用本次 safeZUm 统一保证。
        return RunAsync(RobotAtomicActions.WashOuter, request.CommandId, request.NeedleCode, request.Reason,
            netVolumeUl: 0, clearsNeedle: true, zAxis, safeZUm, cancellationToken, async steps =>
        {
            await _primitives.MoveZAsync(zAxis, washOuterZUm, cancellationToken); steps.Add(Step("MoveZ→外壁清洗高度", washOuterZUm));
            await _primitives.WashOuterAsync(cancellationToken); steps.Add(Step("外壁清洗", null));
        });
    }

    // 统一执行壳：按 body 给定的顺序驱动原语；无论 body 是否抛异常，finally 都抬升回安全高度（动作闭环强保证）。
    // 成功后若注入了记录器，则把该原子动作的净效果写入现有 Mock 运行状态 / 流水账。
    // body 抛出的 BusinessRuleException（体积/CommandId 校验）在进入本方法前已校验；
    // 底层原语未来在真实/半真实 adapter 下若抛异常，异常会继续上抛，调用方可按现有异常约定处理。
    private async Task<RobotArmAtomicActionResult> RunAsync(
        string action,
        string commandId,
        string? needleCode,
        string? reason,
        int netVolumeUl,
        bool clearsNeedle,
        RobotZAxis zAxis,
        long safeZUm,
        CancellationToken cancellationToken,
        Func<List<RobotArmAtomicStep>, Task> body)
    {
        var steps = new List<RobotArmAtomicStep>();
        try
        {
            await body(steps);
        }
        finally
        {
            await _primitives.MoveZAsync(zAxis, safeZUm, cancellationToken);
            steps.Add(Step("MoveZ→安全高度", safeZUm));
        }

        if (_recorder is not null)
        {
            await _recorder.RecordAsync(
                new RobotArmAtomicActionContext(commandId, needleCode, action, netVolumeUl, clearsNeedle, reason, safeZUm),
                cancellationToken);
        }

        var needle = string.IsNullOrWhiteSpace(needleCode) ? "auto" : needleCode;
        var message = $"{action} 完成（command={commandId}, needle={needle}{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $", reason={reason}")}）";
        return RobotArmAtomicActionResult.Succeeded(action, steps, message);
    }

    private static RobotArmAtomicStep Step(string name, object? detail) =>
        new(name, detail is null ? null : detail.ToString());

    private static void RequireCommandId(string? commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            throw new BusinessRuleException("atomic_action_command_id_required", "CommandId is required.");
        }
    }

    private static void RequirePositiveVolume(int volumeUl, string fieldName)
    {
        if (volumeUl <= 0)
        {
            throw new BusinessRuleException("atomic_action_volume_invalid", $"{fieldName} must be a positive volume in uL.");
        }
    }

    private static RobotZAxis ResolveZAxis(string? needleCode)
    {
        var normalized = needleCode?.Trim();
        return normalized is not null
            && (normalized.Equals(NeedleCodes.Needle2, StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("2", StringComparison.OrdinalIgnoreCase))
            ? RobotZAxis.Z2
            : RobotZAxis.Z1;
    }
}

public static class RobotAtomicActions
{
    public const string TakeLiquid = "TakeLiquid";
    public const string PrepareMix = "PrepareMix";
    public const string DispenseLiquid = "DispenseLiquid";
    public const string WashInner = "WashInner";
    public const string WashOuter = "WashOuter";
}
