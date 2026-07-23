using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;
using Xunit;

namespace Stainer.Tests;

// 单元测试：验证机械臂原子动作按工艺规定的顺序驱动底层原语。
// 直接构造 MockRobotMotionPrimitives（记录调用顺序）+ 显式高度，不启动 Web host / DB，
// 聚焦“动作顺序”这一核心契约。
public sealed class RobotArmAtomicActionTests
{
    // 用一组确定的、互不相同的高度，便于断言每一步传入的 Z 值。
    private static readonly RobotArmAtomicHeights Heights = new()
    {
        AspirateZUm = 1_000,
        MixZUm = 2_000,
        DispenseZUm = 3_000,
        WashInnerZUm = 4_000,
        WashOuterZUm = 5_000,
        SafeZUm = 90_000
    };

    private static (RobotArmAtomicActionService service, MockRobotMotionPrimitives primitives) BuildSut()
    {
        var primitives = new MockRobotMotionPrimitives();
        var service = new RobotArmAtomicActionService(primitives, Heights);
        return (service, primitives);
    }

    [Fact]
    public async Task TakeLiquid_moves_to_aspirate_height_then_aspirates_then_returns_to_safe()
    {
        var (service, primitives) = BuildSut();
        var result = await service.TakeLiquidAsync(new TakeLiquidRequest("cmd-take", "Needle1", 100));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(RobotAtomicActions.TakeLiquid, result.Action);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.AspirateZUm),
            RobotPrimitiveCall.Aspirate(100),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.SafeZUm)
        ], primitives.Calls);
    }

    [Fact]
    public async Task PrepareMix_moves_to_mix_height_then_dispenses_then_returns_to_safe()
    {
        var (service, primitives) = BuildSut();
        var result = await service.PrepareMixAsync(new PrepareMixRequest("cmd-mix", "Needle1", 80));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.MixZUm),
            RobotPrimitiveCall.Dispense(80),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.SafeZUm)
        ], primitives.Calls);
    }

    [Fact]
    public async Task DispenseLiquid_moves_to_dispense_height_then_dispenses_then_returns_to_safe()
    {
        var (service, primitives) = BuildSut();
        var result = await service.DispenseLiquidAsync(new DispenseLiquidRequest("cmd-disp", "Needle1", 60));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.DispenseZUm),
            RobotPrimitiveCall.Dispense(60),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.SafeZUm)
        ], primitives.Calls);
    }

    [Fact]
    public async Task WashInner_moves_to_wash_height_then_aspirates_wash_then_dispenses_waste_then_returns_to_safe()
    {
        var (service, primitives) = BuildSut();
        var result = await service.WashInnerAsync(new WashInnerRequest("cmd-wash-inner", "Needle1", 200, 200));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.WashInnerZUm),
            RobotPrimitiveCall.Aspirate(200),
            RobotPrimitiveCall.Dispense(200),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.SafeZUm)
        ], primitives.Calls);
    }

    [Fact]
    public async Task WashOuter_moves_to_outer_wash_height_then_washes_then_returns_to_safe()
    {
        var (service, primitives) = BuildSut();
        var result = await service.WashOuterAsync(new WashOuterRequest("cmd-wash-outer", "Needle1"));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.WashOuterZUm),
            RobotPrimitiveCall.WashOuter(),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.SafeZUm)
        ], primitives.Calls);
    }

    [Fact]
    public async Task Each_action_records_semantic_step_trace_in_order()
    {
        var (service, _) = BuildSut();
        var result = await service.TakeLiquidAsync(new TakeLiquidRequest("cmd-trace", "Needle2", 50, "load ABC"));
        var names = result.Steps.Select(x => x.Name).ToArray();
        Assert.Equal(["MoveZ→吸液高度", "Aspirate", "MoveZ→安全高度"], names);
        Assert.Contains("Needle2", result.Message);
        Assert.Contains("load ABC", result.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task TakeLiquid_rejects_non_positive_volume(int volumeUl)
    {
        var (service, primitives) = BuildSut();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.TakeLiquidAsync(new TakeLiquidRequest("cmd-bad", "Needle1", volumeUl)));
        Assert.Equal("atomic_action_volume_invalid", ex.Code);
        Assert.Empty(primitives.Calls); // 校验失败不应触发任何原语
    }

    [Fact]
    public async Task WashInner_rejects_non_positive_waste_volume_without_calling_primitives()
    {
        var (service, primitives) = BuildSut();
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.WashInnerAsync(new WashInnerRequest("cmd-bad-waste", "Needle1", 200, 0)));
        Assert.Empty(primitives.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task PrepareMix_rejects_non_positive_volume(int volumeUl)
    {
        var (service, primitives) = BuildSut();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.PrepareMixAsync(new PrepareMixRequest("cmd-bad-mix", "Needle1", volumeUl)));
        Assert.Equal("atomic_action_volume_invalid", ex.Code);
        Assert.Empty(primitives.Calls); // 校验失败不应触发任何原语
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task DispenseLiquid_rejects_non_positive_volume(int volumeUl)
    {
        var (service, primitives) = BuildSut();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.DispenseLiquidAsync(new DispenseLiquidRequest("cmd-bad-disp", "Needle1", volumeUl)));
        Assert.Equal("atomic_action_volume_invalid", ex.Code);
        Assert.Empty(primitives.Calls);
    }

    [Fact]
    public async Task Actions_require_command_id()
    {
        var (service, primitives) = BuildSut();
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.DispenseLiquidAsync(new DispenseLiquidRequest("   ", "Needle1", 10)));
        Assert.Empty(primitives.Calls);
    }

    // [P2] 任一原语抛异常时，安全高度回零仍必须执行（动作闭环强保证）。
    [Fact]
    public async Task Safe_height_return_runs_even_when_a_primitive_throws()
    {
        var primitives = new ThrowingRobotMotionPrimitives();
        var service = new RobotArmAtomicActionService(primitives, Heights);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TakeLiquidAsync(new TakeLiquidRequest("cmd-throw", "Needle1", 100)));

        // 期望序列：MoveZ(吸液) -> Aspirate(抛) -> MoveZ(安全)（finally 兜底）。
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.AspirateZUm),
            RobotPrimitiveCall.Aspirate(100),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.SafeZUm)
        ], primitives.Calls);
    }

    // [异常-下降 MoveZ 失败] 下降 MoveZ 失败时，Dispense 不执行；finally 仍尝试回安全高度（第二次 MoveZ）。
    [Fact]
    public async Task DispenseLiquid_skips_dispense_when_descent_movez_fails_but_still_attempts_safe_return()
    {
        var primitives = new SelectivelyThrowingRobotMotionPrimitives { ThrowOnFirst = "MoveZ" };
        var service = new RobotArmAtomicActionService(primitives, Heights);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DispenseLiquidAsync(new DispenseLiquidRequest("cmd-movez-fail", "Needle1", 60)));

        // 第一次 MoveZ(下降) 抛 -> Dispense 未执行；finally 的 MoveZ(安全) 仍执行。
        Assert.DoesNotContain(primitives.Calls, c => c.Kind == "Dispense");
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.DispenseZUm),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.SafeZUm)
        ], primitives.Calls);
    }

    // [异常-Dispense 失败] Dispense 失败时仍回安全高度（动作闭环）。
    [Fact]
    public async Task DispenseLiquid_returns_to_safe_height_when_dispense_fails()
    {
        var primitives = new SelectivelyThrowingRobotMotionPrimitives { ThrowOnFirst = "Dispense" };
        var service = new RobotArmAtomicActionService(primitives, Heights);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DispenseLiquidAsync(new DispenseLiquidRequest("cmd-dispense-fail", "Needle1", 60)));

        // MoveZ(下降) -> Dispense(抛) -> MoveZ(安全)。
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.DispenseZUm),
            RobotPrimitiveCall.Dispense(60),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, Heights.SafeZUm)
        ], primitives.Calls);
    }

    // [P1] 注入 MockStateAtomicActionRecorder 后，原子动作应更新现有 Mock 运行状态与流水账。
    [Fact]
    public async Task TakeLiquid_updates_mock_runtime_state_and_ledger_via_recorder()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var primitives = new MockRobotMotionPrimitives();
        var recorder = new MockStateAtomicActionRecorder(dbContext);
        var service = new RobotArmAtomicActionService(primitives, Heights, recorder);

        var result = await service.TakeLiquidAsync(new TakeLiquidRequest("cmd-take-record", "Needle1", 100, "load ABC"));
        Assert.True(result.Ok, result.Message);

        var arm = await dbContext.RobotArmStates.SingleAsync();
        Assert.Equal(Heights.SafeZUm, arm.CurrentZUm);
        Assert.Equal(MotionStatuses.Idle, arm.Status);
        Assert.Equal("cmd-take-record", arm.CurrentCommandId);

        var needle = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle1);
        Assert.Equal(100, needle.VolumeUl);
        Assert.True(needle.NeedsWash);
        Assert.Equal(MotionStatuses.Completed, needle.Status);

        var operation = await dbContext.PipettingOperations.SingleAsync(x => x.DeviceCommandExecutionId == "cmd-take-record");
        Assert.Equal(PipettingOperationTypes.Aspirate, operation.OperationType);
        Assert.Equal(100, operation.VolumeUl);
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "atomic.action.takeliquid" && x.EntityId == operation.Id));
    }

    // [P1] 清洗类动作应清空针头，并写 WashNeedle 流水。
    [Fact]
    public async Task WashOuter_clears_needle_and_records_wash_ledger()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        dbContext.NeedleStates.Add(new NeedleState { NeedleCode = NeedleCodes.Needle1, NeedleNo = 1, VolumeUl = 80, NeedsWash = true, LoadedSourceType = NeedleLoadSourceTypes.ReagentBottle, LoadedReagentCode = "ABC", UpdatedAtUtc = DateTimeOffset.UtcNow });
        dbContext.RobotArmStates.Add(new RobotArmState { IsHomed = false, Status = MotionStatuses.Idle, UpdatedAtUtc = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();

        var primitives = new MockRobotMotionPrimitives();
        var recorder = new MockStateAtomicActionRecorder(dbContext);
        var service = new RobotArmAtomicActionService(primitives, Heights, recorder);

        var result = await service.WashOuterAsync(new WashOuterRequest("cmd-wash-record", "Needle1"));
        Assert.True(result.Ok, result.Message);

        var needle = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle1);
        Assert.Equal(0, needle.VolumeUl);
        Assert.False(needle.NeedsWash);
        Assert.Equal(NeedleLoadSourceTypes.Empty, needle.LoadedSourceType);

        var operation = await dbContext.PipettingOperations.SingleAsync(x => x.DeviceCommandExecutionId == "cmd-wash-record");
        Assert.Equal(PipettingOperationTypes.WashNeedle, operation.OperationType);
    }

    // [TakeLiquid 契约] 按调用指定吸液 / 安全高度时覆盖配置；顺序固定为 下降 -> 吸液 -> 回安全高度；并复用 Mock 状态记录。
    [Fact]
    public async Task TakeLiquid_honors_per_call_heights_descends_aspirates_then_returns_safe_and_records()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var primitives = new MockRobotMotionPrimitives();
        var recorder = new MockStateAtomicActionRecorder(dbContext);
        var service = new RobotArmAtomicActionService(primitives, Heights, recorder);

        // 配置 Heights 中 AspirateZUm=1_000、SafeZUm=90_000；这里用调用参数覆盖为 7_777 / 88_888。
        var result = await service.TakeLiquidAsync(new TakeLiquidRequest(
            "cmd-takeliquid-contract", "Needle1", 100, "load ABC",
            AspirateZUm: 7_777, SafeZUm: 88_888));
        Assert.True(result.Ok, result.Message);

        // 顺序契约：先下降到指定吸液高度 -> 再吸液 -> 最后回指定安全高度。
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 7_777),
            RobotPrimitiveCall.Aspirate(100),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 88_888)
        ], primitives.Calls);

        // 复用现有 Mock 状态：机械臂停在本次安全高度，流水账落库。
        var arm = await dbContext.RobotArmStates.SingleAsync();
        Assert.Equal(88_888, arm.CurrentZUm);
        Assert.Equal("cmd-takeliquid-contract", arm.CurrentCommandId);
        var operation = await dbContext.PipettingOperations.SingleAsync(x => x.DeviceCommandExecutionId == "cmd-takeliquid-contract");
        Assert.Equal(PipettingOperationTypes.Aspirate, operation.OperationType);
        Assert.Equal(100, operation.VolumeUl);
    }

    // [DispenseLiquid 契约] 按调用指定滴液 / 安全高度覆盖配置；顺序固定为 下降 -> 排液 -> 回安全高度；复用 Mock 状态记录。
    [Fact]
    public async Task DispenseLiquid_honors_per_call_heights_descends_dispenses_then_returns_safe_and_records()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var primitives = new MockRobotMotionPrimitives();
        var recorder = new MockStateAtomicActionRecorder(dbContext);
        var service = new RobotArmAtomicActionService(primitives, Heights, recorder);

        // 配置 Heights 中 DispenseZUm=3_000、SafeZUm=90_000；这里用调用参数覆盖为 6_666 / 88_888。
        var result = await service.DispenseLiquidAsync(new DispenseLiquidRequest(
            "cmd-dispenseliquid-contract", "Needle1", 60, "drop",
            DispenseZUm: 6_666, SafeZUm: 88_888));
        Assert.True(result.Ok, result.Message);

        // 顺序契约：先下降到指定滴液高度 -> 再排液 -> 最后回指定安全高度。
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 6_666),
            RobotPrimitiveCall.Dispense(60),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 88_888)
        ], primitives.Calls);

        // 复用现有 Mock 状态：机械臂停在本次安全高度，流水账落库。
        var arm = await dbContext.RobotArmStates.SingleAsync();
        Assert.Equal(88_888, arm.CurrentZUm);
        Assert.Equal("cmd-dispenseliquid-contract", arm.CurrentCommandId);
        var operation = await dbContext.PipettingOperations.SingleAsync(x => x.DeviceCommandExecutionId == "cmd-dispenseliquid-contract");
        Assert.Equal(PipettingOperationTypes.Dispense, operation.OperationType);
        Assert.Equal(60, operation.VolumeUl);
    }

    // [PrepareMix 契约] 按调用指定配液 / 安全高度覆盖配置；顺序固定为 下降 -> 排液 -> 回安全高度；复用 Mock 状态记录。
    [Fact]
    public async Task PrepareMix_honors_per_call_heights_descends_dispenses_then_returns_safe_and_records()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var primitives = new MockRobotMotionPrimitives();
        var recorder = new MockStateAtomicActionRecorder(dbContext);
        var service = new RobotArmAtomicActionService(primitives, Heights, recorder);

        // 配置 Heights 中 MixZUm=2_000、SafeZUm=90_000；这里用调用参数覆盖为 5_555 / 88_888。
        var result = await service.PrepareMixAsync(new PrepareMixRequest(
            "cmd-preparemix-contract", "Needle1", 80, "mix",
            MixZUm: 5_555, SafeZUm: 88_888));
        Assert.True(result.Ok, result.Message);

        // 顺序契约：先下降到指定配液高度 -> 再排液 -> 最后回指定安全高度。
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 5_555),
            RobotPrimitiveCall.Dispense(80),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 88_888)
        ], primitives.Calls);

        var arm = await dbContext.RobotArmStates.SingleAsync();
        Assert.Equal(88_888, arm.CurrentZUm);
        Assert.Equal("cmd-preparemix-contract", arm.CurrentCommandId);
        var operation = await dbContext.PipettingOperations.SingleAsync(x => x.DeviceCommandExecutionId == "cmd-preparemix-contract");
        Assert.Equal(PipettingOperationTypes.Dispense, operation.OperationType);
        Assert.Equal(80, operation.VolumeUl);
    }

    // [WashInner 契约] 按调用指定内壁清洗 / 安全高度覆盖配置；Z 顺序固定为 下降 -> 吸清洗液 -> 排废液 -> 回安全高度；复用 Mock 状态记录。
    [Fact]
    public async Task WashInner_honors_per_call_heights_descends_aspirates_dispenses_then_returns_safe_and_records()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var primitives = new MockRobotMotionPrimitives();
        var recorder = new MockStateAtomicActionRecorder(dbContext);
        var service = new RobotArmAtomicActionService(primitives, Heights, recorder);

        // 配置 Heights 中 WashInnerZUm=4_000、SafeZUm=90_000；这里用调用参数覆盖为 4_444 / 88_888。
        var result = await service.WashInnerAsync(new WashInnerRequest(
            "cmd-washinner-contract", "Needle1", 200, 200, "inner wash",
            WashInnerZUm: 4_444, SafeZUm: 88_888));
        Assert.True(result.Ok, result.Message);

        // Z 顺序契约：先下降到指定内壁清洗高度 -> 吸清洗液 -> 排废液 -> 最后回指定安全高度。
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 4_444),
            RobotPrimitiveCall.Aspirate(200),
            RobotPrimitiveCall.Dispense(200),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 88_888)
        ], primitives.Calls);

        var arm = await dbContext.RobotArmStates.SingleAsync();
        Assert.Equal(88_888, arm.CurrentZUm);
        Assert.Equal("cmd-washinner-contract", arm.CurrentCommandId);
        var operation = await dbContext.PipettingOperations.SingleAsync(x => x.DeviceCommandExecutionId == "cmd-washinner-contract");
        Assert.Equal(PipettingOperationTypes.WashNeedle, operation.OperationType);
    }

    // [WashOuter 契约] 按调用指定外壁清洗 / 安全高度覆盖配置；Z 顺序固定为 下降 -> 外壁清洗 -> 回安全高度；复用 Mock 状态记录。
    [Fact]
    public async Task WashOuter_honors_per_call_heights_descends_washes_then_returns_safe_and_records()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var primitives = new MockRobotMotionPrimitives();
        var recorder = new MockStateAtomicActionRecorder(dbContext);
        var service = new RobotArmAtomicActionService(primitives, Heights, recorder);

        // 配置 Heights 中 WashOuterZUm=5_000、SafeZUm=90_000；这里用调用参数覆盖为 5_555 / 88_888。
        var result = await service.WashOuterAsync(new WashOuterRequest(
            "cmd-washouter-contract", "Needle1", "outer wash",
            WashOuterZUm: 5_555, SafeZUm: 88_888));
        Assert.True(result.Ok, result.Message);

        // Z 顺序契约：先下降到指定外壁清洗高度 -> 执行外壁清洗 -> 最后回指定安全高度。
        Assert.Equal(
        [
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 5_555),
            RobotPrimitiveCall.WashOuter(),
            RobotPrimitiveCall.MoveZ(RobotZAxis.Z1, 88_888)
        ], primitives.Calls);

        var arm = await dbContext.RobotArmStates.SingleAsync();
        Assert.Equal(88_888, arm.CurrentZUm);
        Assert.Equal("cmd-washouter-contract", arm.CurrentCommandId);
        var operation = await dbContext.PipettingOperations.SingleAsync(x => x.DeviceCommandExecutionId == "cmd-washouter-contract");
        Assert.Equal(PipettingOperationTypes.WashNeedle, operation.OperationType);
    }

    private static async Task<StainerDbContext> CreateMigratedContextAsync()
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-atomic-action-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        var connectionString = $"Data Source={databasePath}";
        var options = new DbContextOptionsBuilder<StainerDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaConnectionInterceptor())
            .Options;
        var dbContext = new StainerDbContext(options);
        DatabaseInitializer.EnsureDatabaseDirectory(connectionString);
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }

    // 仅用于 [P2]：Aspirate 抛异常，其余原语正常记录。
    private sealed class ThrowingRobotMotionPrimitives : IRobotMotionPrimitives
    {
        public List<RobotPrimitiveCall> Calls { get; } = new();

        public Task<RobotArmPositionUm> GetPositionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RobotArmPositionUm(0, 0, 0, 0));

        public Task MoveXYAsync(long xUm, long yUm, CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.MoveXY(xUm, yUm));
            return Task.CompletedTask;
        }

        public Task MoveZAsync(RobotZAxis axis, long zUm, CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.MoveZ(axis, zUm));
            return Task.CompletedTask;
        }

        public Task AspirateAsync(int volumeUl, CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.Aspirate(volumeUl));
            throw new InvalidOperationException("aspirate failed");
        }

        public Task DispenseAsync(int volumeUl, CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.Dispense(volumeUl));
            return Task.CompletedTask;
        }

        public Task WashOuterAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.WashOuter());
            return Task.CompletedTask;
        }
    }

    // 可配置：仅在指定原语"第一次"调用时抛异常（用于下降 MoveZ 失败 / Dispense 失败等场景，
    // 第二次同名调用——如 finally 的回安全高度——仍正常执行，便于断言动作闭环）。
    private sealed class SelectivelyThrowingRobotMotionPrimitives : IRobotMotionPrimitives
    {
        public List<RobotPrimitiveCall> Calls { get; } = new();
        public string ThrowOnFirst { get; set; } = ""; // "MoveZ" | "Aspirate" | "Dispense" | "WashOuter"
        private int _moveZCount;
        private int _aspirateCount;
        private int _dispenseCount;
        private int _washOuterCount;

        public Task<RobotArmPositionUm> GetPositionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RobotArmPositionUm(0, 0, 0, 0));

        public Task MoveXYAsync(long xUm, long yUm, CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.MoveXY(xUm, yUm));
            return Task.CompletedTask;
        }

        public Task MoveZAsync(RobotZAxis axis, long zUm, CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.MoveZ(axis, zUm));
            if (ThrowOnFirst == "MoveZ" && _moveZCount++ == 0)
            {
                throw new InvalidOperationException("movez failed");
            }

            return Task.CompletedTask;
        }

        public Task AspirateAsync(int volumeUl, CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.Aspirate(volumeUl));
            if (ThrowOnFirst == "Aspirate" && _aspirateCount++ == 0)
            {
                throw new InvalidOperationException("aspirate failed");
            }

            return Task.CompletedTask;
        }

        public Task DispenseAsync(int volumeUl, CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.Dispense(volumeUl));
            if (ThrowOnFirst == "Dispense" && _dispenseCount++ == 0)
            {
                throw new InvalidOperationException("dispense failed");
            }

            return Task.CompletedTask;
        }

        public Task WashOuterAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add(RobotPrimitiveCall.WashOuter());
            if (ThrowOnFirst == "WashOuter" && _washOuterCount++ == 0)
            {
                throw new InvalidOperationException("washouter failed");
            }

            return Task.CompletedTask;
        }
    }
}
