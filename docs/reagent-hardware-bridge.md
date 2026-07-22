# 试剂区：后端→硬件通信旁挂框架（一期）

> 新增能力：试剂被扫码确认 / 状态变更时，后端在**不改动前端→后端接口、不改动后端→数据库逻辑**的前提下，自动把"试剂状态变更"作为设备命令驱动到 `IDeviceAdapter`（串口硬件通道）。硬件暂未连接，先用开关控制、代码就绪待接。

## 1. 背景与目标

- 前端点击试剂位 → 右侧栏「状态」Tab 显示该试剂的状态/余量/试剂类别（源自数据库）。
- 当后端因试剂操作而写库时，需要**同步**通知硬件（一期：写方向——试剂状态变更 → 硬件）；二期再从硬件传感器回读。
- 硬件协议 / 串口 / adapter 代码已存在于 `Infrastructure/Devices/` 与 `Application/Devices/`，本次只做"接上试剂业务"。
- 硬约束：前端一行不改、后端→数据库一行不改、尽可能低耦合。

**核心洞察**：试剂业务在写库后**已经在广播事件**（`ReagentChanged` / `ReagentBottleChanged`），项目也已有完整的「设备命令四步范式」+ 后台 worker 范式。因此采用**旁挂**而非改业务。

## 2. 数据流

```
[现有·零改] ReagentScanWriteService.ConfirmActiveSessionPositionAsync
   └─ 写库 + eventPublisher.Publish(ReagentChanged / ReagentBottleChanged)   ← 注入 IRuntimeEventPublisher 接口
        │  (DI 把 IRuntimeEventPublisher 解析为装饰器)
        ▼
[新增] ReagentHardwareEventDecorator : IRuntimeEventPublisher
   ├─ inner.Publish(msg)  → 原 InMemoryRuntimeEventPublisher → MachineEventSignalRDispatcher（零改，照常推 SignalR）
   └─ 若 Enabled 且为试剂事件 → 镜像到自有 hardwareChannel
        ▼
[新增] ReagentHardwareDispatcher : BackgroundService   (hardwareChannel 唯一读者)
   └─ 初始延迟 + 提交确认 → 每事件新建 scope → IReagentHardwareSink.NotifyReagentStateChangedAsync
        ▼
[新增] ReagentHardwareSink : IReagentHardwareSink  (Scoped)
   └─ 复用四步范式: Begin → SaveChanges → deviceAdapter.ScanReagentAsync → TryPersistCompletionAsync
        ▼
[现有·零改] IDeviceAdapter (Mock→MockDeviceOperations 安全返回 / Real→UnavailableRealDeviceAdapter fail-closed)
[现有·零改] DeviceCommunicationPersistenceService（Pending/Complete + SQLite 锁降级）
[现有·零改] DeviceCommunicationRecord 表（复用，未加列/迁移）
```

## 3. 改动文件清单

### 新增（4 个文件）

| 文件 | 职责 |
|---|---|
| `backend/Stainer.Web/Application/Services/IReagentHardwareSink.cs` | 接口 + `ReagentHardwareEvent`/`ReagentHardwareResult`/`ReagentHardwareSensorReadout` record；`DeriveCommandId` 用业务键派生稳定命令 id |
| `backend/Stainer.Web/Application/Services/ReagentHardwareEventDecorator.cs` | 装饰 `IRuntimeEventPublisher`：转发 inner + 试剂事件镜像到独立 channel；读 `Device:ReagentHardware:Enabled` |
| `backend/Stainer.Web/Application/Services/ReagentHardwareDispatcher.cs` | `BackgroundService`：单读 hardwareChannel，初始延迟+提交确认后调 sink；异常吞掉不 crash host |
| `backend/Stainer.Web/Infrastructure/Devices/ReagentHardwareSink.cs` | Scoped 实现：幂等预检 + 四步范式调 `IDeviceAdapter.ScanReagentAsync`；语义化 action `reagent.stateChanged` / `reagent.bottleChanged` / `reagent.bottleDepleted` |

### 修改（2 个文件，均为装配/配置层）

| 文件 | 改动 |
|---|---|
| `backend/Stainer.Web/Infrastructure/ServiceCollectionExtensions.cs` | 3 处 DI：①`AddRuntimeMessagingServices` 注册装饰器并把 `IRuntimeEventPublisher` 指向它；②`AddApplicationServices` 注册 `IReagentHardwareSink`（Scoped）；③`AddHostedRuntimeServices` 注册 `ReagentHardwareDispatcher` |
| `backend/Stainer.Web/appsettings.json` | `Device` 节加 `"ReagentHardware": { "Enabled": false }` |

## 4. 零改动边界（PR diff 不应出现）

前端（`wwwroot/**`）、所有 `WebHostEndpointExtensions.*.cs` 端点、`ReagentScanWriteService`、`ReagentQrScannerDeviceOperationService`、`RuntimeEventPublisher.cs`（接口/实现/事件类型常量）、`MachineEventSignalRDispatcher`、`MachineExecutor`、`StainerDbContext`、所有 Entity、所有 Migration、`IDeviceAdapter.cs`、`MockDeviceAdapter`、`UnavailableRealDeviceAdapter`、所有 Transport/Protocol、既有测试。

## 5. 配置与开关

```jsonc
"Device": {
  "Mode": "Mock",            // Mock/Real 切换（既有）
  "ReagentHardware": {
    "Enabled": false         // 试剂区硬件旁挂总开关，默认关；硬件接上再设 true
  }
}
```

- `Enabled=false`：装饰器完全不写 hardwareChannel，dispatcher 空转，**零副作用、零开销**。
- `Enabled=true` + `Mode=Mock`：完整链路跑通，`DeviceCommunicationRecord` 落库 `Succeeded`（用于验证框架）。
- `Enabled=true` + `Mode=Real`：`UnavailableRealDeviceAdapter` reject → 落库 `NotSupported` / `real_adapter_not_implemented`，**不发任何真实字节**（fail-closed）。

## 6. Mock / Real 行为对照

| 模式 | `deviceAdapter.ScanReagentAsync("reagent.stateChanged")` | 一期效果 |
|---|---|---|
| Mock | `MockDeviceAdapter` 默认分支安全返回 | record 落库 `Ok=true`，证明装饰器→dispatcher→sink→adapter→四步范式全通 |
| Real | `UnavailableRealDeviceAdapter` reject | record 落库 `Ok=false`、`real_adapter_not_implemented`，无真实字节（符合 PROJECT_CONTEXT §8.15） |
| Real + 真实 adapter 实装（未来） | 在真实 adapter 的 action 路由表识别 `reagent.*` 并映射到主控/DCR55 字节 | 真正驱动硬件 |

## 7. 验证方法

### 自动化测试
新增 `backend/Stainer.Tests/ReagentHardwareDispatcherTests.cs`：Mock + `Enabled=true` 下走 `/api/device/reagent-scanner/qr/reset→start→report`（report 内部触发 `ConfirmScanAsync` 并发布 `ReagentChanged`），轮询断言 `DeviceCommunicationRecords` 出现 `Action='reagent.stateChanged'/'reagent.bottleChanged'`、`Ok=true`、`AdapterName=MockDeviceOperations`。运行 `dotnet test backend/Stainer.Tests/Stainer.Tests.csproj`。

### 手动验证
1. `appsettings.json` 设 `Device:Mode=Mock` + `Device:ReagentHardware:Enabled=true`。
2. `dotnet run --project backend/Stainer.Web`，浏览器登录后对某试剂位扫码确认。
3. 查 SQLite：
   ```sql
   SELECT action, command_id, ok, persistence_status, adapter_name
   FROM device_communication_records WHERE action LIKE 'reagent.%' ORDER BY created_at_utc;
   ```
   应见 `reagent.stateChanged` + `reagent.bottleChanged` 两行，`ok=1, persistence_status=Complete, adapter_name=MockDeviceOperations`。

## 8. 关键设计决策与陷阱

| 决策/陷阱 | 处理 |
|---|---|
| `System.Threading.Channels` 是**竞争消费非广播** | 装饰器 + 独立 channel 物理隔离；不碰 inner 的 `SingleReader=true`，SignalR 链路零影响 |
| 事件在源事务 Commit **之前**发布（`ReagentScanWriteService.cs:497`） | dispatcher 初始延迟 150ms + 提交确认（新 scope 查 `ReagentScanItems` 是否落库），回滚事件丢弃并审计 |
| 重复发送（rescan/dispatcher 重启） | `DeriveCommandId` 用 `scanSessionId+position`（缺则 `reagentCode+bottleId`）派生稳定 id + sink 幂等预检 |
| Singleton/Scoped 陷阱 | 装饰器/dispatcher Singleton；sink Scoped；dispatcher 每事件 `CreateAsyncScope` 解析 sink/DbContext |
| 后台异常 crash webhost | 仿 `MachineExecutor.RecordExecutorExceptionAsync` 全 catch + AuditLog，绝不 rethrow |
| SQLite 锁 | 复用 `DeviceCommunicationPersistenceService.TryPersistCompletionAsync` 降级；不加外部事务 |
| 不绕过 adapter | 一期复用 `IDeviceAdapter.ScanReagentAsync` 承载语义化 action，保留 Real fail-closed 门禁 |

## 9. 二期路线（不在本次范围）

- `ReadReagentSensorsAsync` 真实实现：经 `IRealDeviceReadAdapter` 读主控光耦/IO（到位/入口感应），可能需扩 `MainControllerSerialTransport` 只读白名单。
- 在 `IDeviceAdapter` 加 `NotifyReagentStateAsync` 专用方法（接口加方法向后兼容），消除一期"用 ScanReagentAsync 承载非扫码动作"的语义错位。
- 真实 adapter 对 `reagent.*` action 的字节映射（待硬件协议确定/接上）。
- 严格事务一致性的 outbox 表（若 Real 模式要求绝不漏发）。
