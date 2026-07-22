# 代码索引

> DropStaining_2 = 全自动冰冻切片染色机上位机。主线是 `backend/Stainer.Web`（ASP.NET Core Minimal API + EF Core + SQLite，前端是 `wwwroot/twin` 单页孪生）；`bridges/` 下挂独立的 SOCON 桥接进程；`tools/` 下挂数据导入与运维工具。
>
> 本文对每个代码目录与代码文件逐个给一句话简介，便于快速定位。已排除 `bin/`、`obj/` 及 EF Core 生成代码（`*.Designer.cs`、`ModelSnapshot.cs`）。粒度：一个目录一句话、一个文件一句话。

---

## 根目录

Visual Studio 解决方案入口。主线代码在 `backend/`，桥接进程在 `bridges/`，工具在 `tools/`，文档/规约/数据/坐标基线各自独立成目录。

- `Stainer.sln` — 主解决方案，聚合 `backend/Stainer.Web`、`backend/Stainer.Tests`、`tools/Stainer.LegacyImporter`、`tools/Stainer.Operations`（**不含** `bridges/Stainer.SoconBridge`，桥独立构建）。
- `README.md` — 项目主说明：主线介绍、阶段边界、启动/验证命令、文档索引。
- `.gitignore` — 忽略 .NET/Node/Python 构建、IDE、测试产物、SQLite 临时文件，以及桥本地配置 `SoconBridge.config.local.json`。
- `.config/dotnet-tools.json` — .NET 本地工具清单，仅声明 `dotnet-ef` 9.0.5。
- `package.json` — npm 清单，声明 `playwright-core` 与三个浏览器验收脚本。
- `position_coordinates_with_engineering_tabs.csv` — 玻片/试剂区坐标表（与 `coordinate-baseline/` 同源的根目录副本，附工程标签页）。
- `cc.html` — 约 740KB 的导出 HTML 页面快照（验收归档产物，非源码）。
- `old.html`、`.tmp-c.txt`、`tmp-sheet-inspect/inspect.mjs` — 空/临时/调试文件，非主干。

### 子目录一句话

- `backend/` — 解决方案主线代码（`Stainer.Web` 主项目 + `Stainer.Tests` 测试）。
- `bridges/` — 仅 `Stainer.SoconBridge` 一个独立 x86 桥接进程。
- `tools/` — `Stainer.LegacyImporter`（旧数据导入）、`Stainer.Operations`（运维工具）、`browser-acceptance/`（Playwright 验收）、`verify-runtime-git-policy.ps1`。
- `docs/` — 项目文档聚合（本文件所在目录）。
- `project_context/` — 架构规约与待办（`PROJECT_CONTEXT.md` 是项目宪法级文档）。
- `real-hardware/` — 真实设备取证与安全门禁文档。
- `coordinate-baseline/` — 坐标基线 CSV。
- `data/` — 运行时数据（`stainer.db`、备份、日志、种子脚本）。

---

## backend/Stainer.Web

染片机后端主项目（.NET 9）。分层：`Application`（业务权威）→ `Domain`（实体）→ `Infrastructure`（持久化/设备/Web）。设备适配器不直接写业务库；`System.IO.Ports` 只出现在 `Infrastructure/Devices`。

### 根文件

- `Program.cs` — 应用入口：配置、DI（`AddStainerInfrastructure`）、中间件、登录、SignalR、端点映射、数据库初始化。
- `Stainer.Web.csproj` — 工程文件：EF Core Sqlite 9.0.5 + System.IO.Ports 8.0.0。
- `appsettings.json` / `appsettings.Development.json` — 运行配置：`Database`、`Device`（Mode/主控串口/DCR55/试剂硬件开关）、日志、安全。
- `wwwroot/twin/index.html` — 数字孪生单页前端（HTML+CSS+JS 一体，约 8500 行），`/control-console` 的 UI。

### Application

应用层（业务权威）：编排持久化、幂等、设备调用、事件发布与硬件边界。

#### Application/Devices

设备抽象与协议层：定义统一 `IDeviceAdapter`、串口/协议常量与帧编解码，不写业务库。

- `IDeviceAdapter.cs` — 统一设备适配器接口（状态/健康/扫码/温控/泵/移液/洗针/DAB/故障等）+ `DeviceOperationRequest`/`DeviceCommandResult` 等 record 与命令常量。
- `MainControllerProtocol.cs` — 主控命令字节常量（系统/制冷/加热/光耦/PWM/QR/混匀父类与子命令）+ 各请求帧 builder + ACK 解析，含量程校验。
- `IceImmunoSerialProtocol.cs` — 冰免串口二进制帧（头 `0xA5`/尾 `0x5A`、CRC16-Modbus）构造/解码 + 流式解码器。
- `Dcr55Protocol.cs` — DCR55 触发命令文本/编码器 + 应答条码解析（要求 `\r\n` 结尾）。
- `Dcr55TolerantProtocol.cs` — DCR55 容错解析变体，兼容 `\r`/`\r\n`/`\n`/无结束符。
- `StandaloneCoolingProtocol.cs` — 独立制冷模块 4 字节帧（`FF 00 xx 校验`）builder 与解析。
- `Dcr55DeviceBoundary.cs` — DCR55/主控连接参数 record + `IDcr55Adapter` 边界接口。
- `RealDeviceReadBoundary.cs` — 真机读侧契约：`IDeviceByteTransport`（字节交换/接收）+ `IRealDeviceReadAdapter`（结构化只读）+ 状态/端点常量。

#### Application/Services

业务服务层（约 80 个文件）。按职责分组：

- **幂等/事务/执行器**：`CommandIdempotencyService.cs`（命令幂等收据）、`CommandExecutionResult.cs`（响应+实体记账）、`MachineExecutor.cs`（后台串行执行器）、`MachineExecutorLeaseService.cs`（文件锁租约）、`RunControlService.cs`（运行启停/重做）、`StartupRecoveryService.cs`（启动恢复）、`StartupDeviceInitializationHostedService.cs`（启动初始化后台服务）。
- **运行/任务/工作流**：`MachineRunService.cs`/`MachineRunQueryService.cs`（运行创建/查询）、`TaskCreationService.cs`（HE/IHC 任务创建）、`WorkflowWriteService.cs`/`WorkflowMaintenanceService.cs`/`WorkflowQueryService.cs`/`WorkflowSnapshotFactory.cs`/`WorkflowPrimaryAntibodyResolver.cs`（工作流 CRUD/发布/快照/一抗解析）、`ChannelBatchWorkflowService.cs`/`ChannelBatchWorkflowBackfillService.cs`（通道批次工作流绑定与回填）。
- **试剂/扫码**：`ReagentScanWriteService.cs`（扫码会话写）、`ReagentQrScannerDeviceOperationService.cs`（试剂 QR 设备命令）、`ReagentScannerMockService.cs`（Mock 扫码）、`ReagentQueryService.cs`（试剂读）、`ReagentBarcodeParser.cs`/`ReagentBarcodeParseResult.cs`/`IReagentBarcodeParser.cs`（17 字符试剂条码解析）、`SampleScanWriteService.cs`（样本扫码写）、`HospitalBarcodeNormalizer.cs`（医院条码规范化）、`FriendlyPointCodeResolver.cs`（友好孔位码翻译）。
- **试剂硬件旁挂（试剂→硬件通信框架）**：`IReagentHardwareSink.cs`（Sink 接口+事件 record）、`ReagentHardwareEventDecorator.cs`（装饰事件总线）、`ReagentHardwareDispatcher.cs`（后台消费驱动设备）。
- **坐标**：`CoordinateProfileLifecycleService.cs`（坐标档案版本生命周期）、`CoordinateSnapshotFactory.cs`（快照工厂）、`ReagentCoordinateAnchorService.cs`/`ReagentCoordinateGenerationService.cs`（试剂列锚点与插值）、`DigitalTwinCoordinateImportService.cs`（孪生 CSV 导入）。
- **液路/运动/温控/供水**：`FluidicsControlService.cs`（泵/混匀/液位/清洗/洗针）、`MotionControlService.cs`（机械臂/针/移液）、`ThermalControlService.cs`（温控点/制冷）、`WaterSupplyControlService.cs`（每通道供水）。
- **DAB**：`DabLifecycleService.cs`（DAB 批次全生命周期）、`DabExpiryHostedService.cs`（过期扫描后台服务）。
- **工程**：`EngineeringSessionService.cs`（工程会话）、`EngineeringWriteService.cs`/`EngineeringConfigService.cs`/`EngineeringQueryService.cs`/`EngineeringDiagnosticService.cs`（工程写/配置/查询/诊断）、`EngineeringPipettingService.cs`（工程移液测试）。
- **设备/预检/模式**：`DeviceControlService.cs`（设备状态/故障控制）、`DeviceInitializationService.cs`（设备初始化步骤）、`DevicePrecheckService.cs`（11 项预检）、`DeviceModeService.cs`（Mock/Real 模式）、`PreHardwareReadinessService.cs`（启动就绪门禁）、`PreflightValidationService.cs`（预检报告聚合）、`ScannerControlService.cs`/`ScannerConfigurationService.cs`（扫码器控制/配置）。
- **追溯/操作台**：`TraceabilityQueryService.cs`（历史运行/消耗追溯）、`OperatorSnapshotQueryService.cs`（操作台快照聚合）、`OperatorAlarmPresentation.cs`（告警码→中文摘要）。
- **配置档案**：`AppSettingsConfigService.cs`/`SerialConnectionConfigService.cs`/`PrecisionCalibrationConfigService.cs`/`MixerParameterConfigService.cs`/`WashValveConfigService.cs`（各类工程配置 upsert）。
- **身份/安全/Mock**：`UserSessionService.cs`（Cookie 会话）、`UserManagementService.cs`/`UserQueryService.cs`（用户 CRUD/查询）、`PasswordHashService.cs`（PBKDF2 哈希）、`MockRuntimeResetService.cs`/`MockDemoDataSeeder.cs`/`MockDeviceBaseline.cs`（Mock 重置/演示数据/基线）、`MockLisAdapter.cs`/`MockLisQueryService.cs`（Mock LIS）。
- **基础设施服务**：`RuntimeEventPublisher.cs`（事件总线 Channel 实现）、`DeviceCommunicationPersistenceService.cs`（设备通信记录四步范式）、`SafetyLogWriter.cs`（JSONL 安全日志）、`AuthenticatedUser.cs`/`BusinessRuleException.cs`/`BusinessSelectionRequiredException.cs`（身份/异常）、`LiquidClassSnapshotFactory.cs`（液类快照）。

#### Application/ReadModels

API/服务返回的响应 DTO（sealed record，按领域聚合）。

- `AuthReadModels.cs`、`UserReadModels.cs` — 登录/当前用户、用户与角色列表。
- `BusinessWriteReadModels.cs` — 通用写响应 + 工作流草稿/通道批次/Mock 重置等变更响应。
- `RunReadModels.cs`、`OperatorReadModels.cs`、`SafetyReadModels.cs` — 运行/通道/告警、操作台快照、设备模式/租约/恢复。
- `WorkflowReadModels.cs`、`ReagentReadModels.cs`、`DabReadModels.cs` — 工作流、试剂目录/架位/会话、DAB 源瓶/批次。
- `DeviceReadModels.cs`、`ScannerConfigurationReadModels.cs` — 设备初始化/故障/试剂 QR、扫码器档案/区域。
- `EngineeringReadModels.cs`、`PrecheckReadModels.cs`、`TraceabilityReadModels.cs` — 工程布局/坐标/液类、预检项/报告、追溯列表（带分页）。
- `AppSettingsReadModels.cs`、`SerialConnectionReadModels.cs`、`PrecisionCalibrationReadModels.cs`、`MixerParameterReadModels.cs`、`WashValveConfigReadModels.cs` — 各类工程配置响应 DTO。

#### Application/Repositories

仓储接口，抽象 EF Core 持久化与查询。

- `IReferenceDataRepository.cs` — 角色/抽屉引用数据 + 当前激活坐标档案。
- `IUserReadRepository.cs` — 用户/角色只读。
- `IWorkflowRepository.cs`/`IWorkflowReadRepository.cs` — 工作流写（新增/按一抗查）/读（列表/详情）。
- `IReagentRepository.cs`/`IReagentReadRepository.cs` — 试剂写（定义/瓶/会话）/读（目录/架位/会话）。
- `IEngineeringReadRepository.cs` — 工程布局/坐标/液类只读。

#### Application/Requests

API 入参请求 DTO（sealed record，按领域聚合）。

- `BusinessWriteRequests.cs` — 用户/角色/工作流草稿/通道批次/Mock 重置等请求。
- `RunRequests.cs`、`SafetyRequests.cs` — 创建运行/运行命令/故障注入/重做、设备模式/备份恢复。
- `DeviceRequests.cs`、`ScannerConfigurationRequests.cs` — 设备初始化/预检/Mock 故障/试剂 QR、扫码器档案/触发/读码。
- `DabRequests.cs`、`EngineeringRequests.cs` — DAB 批次/制备/消耗、工程会话/配置导入/坐标导入。
- `AppSettingsRequests.cs`、`SerialConnectionRequests.cs`、`PrecisionCalibrationRequests.cs`、`MixerParameterRequests.cs`、`WashValveConfigRequests.cs` — 各类工程配置保存请求。

### Domain

领域层：仅 EF Core 实体（POCO）与状态/类型字符串常量，不含行为。

#### Domain/Entities

EF Core 实体；同族聚合放同一文件。

- **身份/审计/命令**：`User.cs`、`UserRole.cs`、`Role.cs`、`AuditLog.cs`、`CommandReceipt.cs`。
- **物理布局**：`Drawer.cs`、`PhysicalSlot.cs`、`WashPosition.cs`、`DabMixPosition.cs`、`ReagentRackPosition.cs`。
- **坐标**：`CoordinateProfile.cs`（档案+版本）、`CoordinatePoint.cs`、`CoordinateCalibrationHistory.cs`、`ReagentCoordinateAnchor.cs`。
- **试剂**：`ReagentDefinition.cs`、`ReagentBottle.cs`、`ReagentRackPlacement.cs`、`ReagentScanSession.cs`、`ReagentScanItem.cs`、`ReagentScanResult.cs`、`HospitalBarcodeMapping.cs`。
- **工作流**：`WorkflowDefinition.cs`、`WorkflowVersion.cs`、`WorkflowStep.cs`、`WorkflowReagentRequirement.cs`、`WorkflowVersionStatus.cs`、`PrimaryAntibodyWorkflowMapping.cs`。
- **染色任务**：`StainingTask.cs`、`StainingTaskStatus.cs`、`StainingTaskType.cs`。
- **运行账本**：`RuntimeLedgerEntities.cs`（ChannelBatch/SlideTask/MachineRun/WorkflowExecution/ReagentConsumption/DabBatch/Alarm 等聚合）、`RuntimeLedgerStatus.cs`（状态常量）。
- **设备**：`DeviceProfile.cs`、`DeviceInitialization.cs`、`DevicePrecheckEntities.cs`、`EngineeringEntities.cs`（EngineeringSession + DeviceCommunicationRecord）。
- **硬件 Mock 遥测**：`FluidicsEntities.cs`、`MotionEntities.cs`、`ThermalEntities.cs`、`WaterSupplyEntities.cs`。
- **液类/混匀/精度/串口/清洗/扫码器/应用设置**：`LiquidClassProfile.cs`、`MixerParameterProfile.cs`、`PrecisionCalibrationProfile.cs`、`SerialConnectionProfile.cs`、`WashValveConfigProfile.cs`、`ScannerConfigurationEntities.cs`、`AppSettingsProfile.cs`。
- **样本扫码/LIS/遗留导入**：`SampleScanEntities.cs`、`LisEntities.cs`、`LegacyImportRun.cs`、`LegacyImportIssue.cs`、`LegacyRuntimeSnapshot.cs`。

### Infrastructure

基础设施层：SQLite/EF Core 数据访问、串口设备通信与 Mock/Real 适配器、EF 仓储、Minimal API 端点、SignalR、数字孪生、健康检查。

#### Infrastructure（根）

- `ServiceCollectionExtensions.cs` — `AddStainerInfrastructure` 统一入口：注册 DbContext/种子/仓储/设备服务/运行时消息/SignalR 等全部依赖。

#### Infrastructure/Data

SQLite 持久化基础设施。

- `StainerDbContext.cs` — 核心 DbContext，暴露 80+ 实体 DbSet。
- `DatabaseInitializer.cs` — 确保库目录存在并启用 `foreign_keys/WAL/busy_timeout` PRAGMA。
- `DatabasePathResolver.cs` — 连接串解析（Testing→环境变量→配置→默认，归一化 `sqlite:///`）。
- `DatabaseOptions.cs` — 绑定 `Database:ConnectionString` 的强类型选项。
- `SqlitePragmaConnectionInterceptor.cs` — 每次开连接统一应用 PRAGMA 的拦截器。
- `StainerDbContextFactory.cs` — 设计期工厂（供 `dotnet ef migrations`）。
- `ReferenceDataSeeder.cs` — 引用数据幂等播种（角色/用户/抽屉槽位/试剂架/DAB/清洗位/坐标剖面/HE·IHC 工作流/Ab 液类）。

#### Infrastructure/Data/Migrations

EF Core 迁移历史（`*.Designer.cs` 与 `StainerDbContextModelSnapshot.cs` 为生成代码，不展开）。时间线覆盖：初始 schema → 通道批/工作流分派 → 设备初始化与通信跟踪 → DAB 生命周期 → 坐标剖面版本化与唯一约束 → 液体类版本化 → 硬件 Mock 模型（温控/流体/运动/水源）→ 扫码器与样本区 → 工程后端配置与预检 → 工程配置 Profile 系列（串口/精度/混匀/清洗阀/应用设置）。代表节点：`InitialInfrastructure`、`DabLifecycleModel`、`CoordinateProfileVersioningAndRunSnapshots`、`LiquidClassVersioningAndSnapshots`、`AddWaterSupplyMock`。

#### Infrastructure/Devices

设备通信层；`System.IO.Ports` 仅在此子层。

- `MainControllerSerialTransport.cs` — 主控真实串口 Transport；发送前校验已审核命令白名单与写命令 payload 量程，单次开→交换→关，不自动 fallback。
- `Dcr55SerialTransport.cs` — DCR55 扫码器真实串口 Transport，单次触发→读 `\r\n` 帧→关闭。
- `CompositeDeviceByteTransport.cs` — 按 endpoint 路由到主控或 DCR55 的组合 Transport。
- `SerialPortAbstractions.cs` — Transport 内部 `ISerialPort` 接口 + `System.IO.Ports.SerialPort` 适配（隔离串口 API）。
- `SerialPortEnumerator.cs` — 只读枚举本机串口名（`SerialPort.GetPortNames`，不开端口）。
- `MockDeviceAdapter.cs` — Mock 模式 `IDeviceAdapter`：基于 `MockDeviceStateStore` 模拟模块状态与各类操作。
- `MockDeviceOperations.cs` — 继承 `MockDeviceAdapter` 的薄包装（标识"操作型 Mock"）。
- `MockDeviceStateStore.cs` — Mock 设备状态机（线程安全，模块状态/故障计划/试剂 QR/版本）。
- `UnavailableRealDeviceAdapter.cs` — Real/离线模式 `IDeviceAdapter`+`IRealDeviceReadAdapter`：写动作 fail-closed，只读镜像字节 Transport；制冷/试剂扫码/清洗泵 PWM写真发字节。
- `Dcr55RealAdapter.cs`/`Dcr55TolerantRealAdapter.cs` — `IDcr55Adapter` 实现：触发扫码并解析（严格 / 容错 `\r` 结尾）。
- `ReagentHardwareSink.cs` — `IReagentHardwareSink` 实现：试剂状态变更经四步范式驱动 `IDeviceAdapter` 并落通信记录。

#### Infrastructure/Health

- `DatabaseHealthChecker.cs` — 直查 SQLite 版本/PRAGMA/完整性 + 临时表读写探测。
- `DatabaseHealthReport.cs` — 健康检查结果 record。
- `DatabaseMaintenanceService.cs` — 体检报告 + 带 SHA256 校验的备份/恢复。

#### Infrastructure/Repositories

EF Core 仓储实现（对接 Application 仓储接口）。

- `EfReferenceDataRepository.cs`、`EfUserReadRepository.cs` — 引用数据/用户只读。
- `EfWorkflowRepository.cs`/`EfWorkflowReadRepository.cs` — 工作流写/读。
- `EfReagentRepository.cs`/`EfReagentReadRepository.cs` — 试剂写/读。
- `EfEngineeringReadRepository.cs` — 工程布局只读。

#### Infrastructure/Twin

数字孪生快照子层（移植自早期 Python `stainer_twin_fastapi`，"表/字段缺失回退 null"）。

- `TwinSnapshotService.cs` — 单例：用嵌入资源加载注册表/CSV，基于 SQLite 现状构造孪生快照与子视图。
- `TwinSqlite.cs` — 底层直接 SQLite 读取（`TableExists/Columns/One/Many`，出错即返回空）。
- `Twin/Assets/frontend_registry.json`、`frontend_db_mapping.csv` — 孪生快照引用的前端控件注册表与字段映射（非 `.cs`）。

#### Infrastructure/Web

Minimal API 端点与 SignalR 推送层。`WebHostEndpointExtensions.*` 是同一 partial 类按业务域拆分。

- `WebHostEndpointExtensions.cs` — partial 主入口：`MapStainerWebHostEndpoints` 统一注册 + `ExecuteBusinessAsync`（业务异常→HTTP）等公共工具。
- `WebHostEndpointExtensions.Identity.cs` — 登录/登出/当前用户/用户角色。
- `WebHostEndpointExtensions.DeviceOperations.cs` — 系统信息/设备模式/设备与温控模块状态控制。
- `WebHostEndpointExtensions.Engineering.cs` — 工程会话/诊断/坐标剖面/液类 CRUD。
- `WebHostEndpointExtensions.Workflows.cs` — 工作流/版本/草稿查询与维护。
- `WebHostEndpointExtensions.TasksAndReagents.cs` — HE/IHC 任务、LIS 模拟、试剂扫码会话起止确认。
- `WebHostEndpointExtensions.Runs.cs` — 运行 preflight/创建/查询/控制。
- `WebHostEndpointExtensions.Traceability.cs` — 追溯历史过滤查询。
- `WebHostEndpointExtensions.Dab.cs` — DAB 位置/源瓶/批次查询与创建。
- `WebHostEndpointExtensions.Scanners.cs` — 扫码器档案/区域 CRUD。
- `WebHostEndpointExtensions.Serial.cs` — 工程调试：枚举 COM 口、读写串口配置（admin-only）。
- `WebHostEndpointExtensions.Precision.cs`/`.Mixer.cs`/`.WashValve.cs`/`.AppSettings.cs` — 精度/混匀/清洗阀/应用设置配置读写（admin-only）。
- `WebHostEndpointExtensions.Compatibility.cs` — 旧版兼容端点（仅 Dev/Testing 开 `/api/state` 等，`/api/system/reset` 返 410）。
- `WebHostEndpointExtensions.Twin.cs` — 数字孪生端点（重定向 `/control-console`、返回孪生 HTML、`/api/twin/*`）。
- `MachineHub.cs` — SignalR Hub `/hubs/machine`（连接校验 + 按角色分组）。
- `MachineEventSignalRDispatcher.cs` — 后台订阅事件总线并转发到 SignalR 组。
- `MockRuntimeStore.cs` — Mock 运行时内存状态（用户/日志/run 兼容实现）。
- `RuntimePageBridgeService.cs` — 桥接 MockRuntimeStore 与正式 MachineRun 服务。

---

## backend/Stainer.Tests

xUnit 测试项目（根目录平铺）。大量用 `WebApplicationFactory<Program>` 做端到端 HTTP 集成，临时 SQLite 库落在 `TestPaths.TempRoot` 下。

- `TestPaths.cs` — 临时根定位到测试输出盘的 `\tmp\stainer-tests`。
- `WebHostIntegrationTests.cs`/`FormalPageAccessIntegrationTests.cs` — WebHost 基础契约与旧版兼容路由。
- `DatabaseInfrastructureTests.cs`/`ReferenceDataSeederTests.cs` — 数据库初始化/PRAGMA/种子。
- `BusinessWriteApiIntegrationTests.cs`/`TraceabilityApiIntegrationTests.cs` — 业务写入 API 与追溯 API 集成。
- `DeviceAdapterInitializationTests.cs`/`DeviceCommunicationPersistenceTests.cs`/`DevicePrecheckTests.cs` — 设备适配器/通信持久化四步范式/预检契约。
- `OfflineRealDeviceAdapterTests.cs`/`OfflineHardwareProtocolTests.cs` — 真机适配器只读边界与协议帧离线测试。
- `MainControllerSerialTransportTests.cs`/`Dcr55SerialTransportTests.cs`/`Dcr55RealAdapterTests.cs`/`Dcr55TolerantProtocolTests.cs`/`Dcr55TolerantRealAdapterTests.cs` — 串口 Transport / DCR55 adapter / 协议容错（注入假 ISerialPort，不开真 COM）。
- `ReagentHardwareDispatcherTests.cs` — 试剂→硬件旁挂全链路（扫码确认→事件→Dispatcher→Sink→通信记录落库）。
- `RuntimeLedgerExecutorTests.cs`/`MockBackendEndToEndAcceptanceTests.cs`/`MockScannerLisDemoTests.cs`/`MockRuntimeResetIntegrationTests.cs` — Mock 执行器/端到端验收/扫码 LIS/重置。
- `WorkflowReagentScanModelTests.cs`/`WorkflowAssignmentHistoryTests.cs`/`ChannelBatchWorkflowBackfillServiceTests.cs`/`DefaultWorkflowSelectionIntegrationTests.cs`/`IhcWorkflowDrivenAntibodyTests.cs` — 工作流模型/历史/回填/默认选择/IHC 一抗契约。
- `CoordinateProfileVersioningTests.cs`/`DigitalTwinCoordinateImportTests.cs`/`LiquidClassVersioningTests.cs` — 坐标版本化/CSV 导入/液类版本化。
- `DabLifecycleTests.cs` — DAB 配比/批次/源瓶/过期重配。
- `PrecisionCalibrationConfigServiceTests.cs`/`ScannerControlServiceTests.cs`/`EngineeringBackendConfigManagementTests.cs`/`EngineeringPipettingApiTests.cs` — 精度/扫码器控制/工程配置/移液测试。
- `PreHardwareSafetyTests.cs`/`MotionControlMockTests.cs`/`ThermalControlMockTests.cs`/`FluidicsControlMockTests.cs`/`FluidicsWashStopAndThresholdTests.cs`/`WaterSupplyMockTests.cs` — 硬件安全/运动/温控/流体/水源 Mock 契约。
- `LegacyJsonImporterTests.cs` — 旧数据导入 dry-run/apply/幂等。
- `UnitTest1.cs`/`TestTempRedirection.cs` — 模板遗留/废弃占位。

---

## bridges/Stainer.SoconBridge

独立 .NET Framework 4.5.2 x86 控制台进程，通过 Windows 命名管道对外提供**受三重门禁的只读 IPC**（仅 7 类命令：Ping/GetBridgeStatus/ValidateSdkDeployment/OpenReadOnlySession/GetNodeBasicStatus/GetAxisPositions/CloseSession），运动/移液/液面显式 NotSupported；反射加载厂商 `SOCON.API.dll`，主项目 Stainer.Web 未接入。

- `Program.cs` — 进程入口：解析 `--self-test`/`--enable-real-read-only`，单实例 mutex，构造 processor 并运行 BridgeHost。
- `BridgeHost.cs` — 命名管道服务器循环（ACL 仅当前用户/LocalSystem，长度前缀 UTF-8 JSON 帧）。
- `BridgeRequestProcessor.cs` — 命令分派核心：七条命令处理 + 三重 fail-closed 门禁 + session 状态机 + 去敏感化。
- `BridgeContracts.cs` — DataContract JSON 契约（BridgeRequest/Response/Config/AxisMapping 等，仅 axis 字段，拒 COM/NodeID/path）。
- `BridgeStatus.cs` — 桥状态枚举（11 态）与告警码常量。
- `ISoconReadOnlyAdapter.cs` — 只读适配器抽象（仅 Open/ReadBasicStatus/ReadAxisPosition/Close，禁 Init/Move/移液/IO）。
- `ReflectionBasedSoconReadOnlyAdapter.cs` — 反射加载 `SOCON.API.dll` 的真实适配器（绑定 8 个 SDK 操作）。
- `RealReadOnlySessionGate.cs` — 双开门禁（本地配置 `realReadOnlyEnabled=true` 且 `--enable-real-read-only` 启动）。
- `SoconReadOnlyConfig.cs` — 本地只读配置视图（白名单/轴映射/校验/连接预检）。
- `SdkDeploymentValidator.cs` — SDK 部署校验（x86 进程/核心文件/PE 头/托管类型/运行时依赖，含 PathScrubber）。
- `PeArchitectureInspector.cs` — PE 头解析判定 native DLL 是否 x86（machine=0x014C）。
- `SelfTestRunner.cs` — `--self-test` 20+ 项离线自检（不连 SDK/硬件）。
- `SoconBridgeProtocol.md`/`README.md`/`SoconBridge.config.example.json` — IPC 协议文档/说明/配置示例。

---

## tools/Stainer.LegacyImporter

.NET 9 控制台工具，把旧版 JSON（用户/液类/协议/试剂/位置/运行时快照）导入 Stainer.Web 的 SQLite，支持 `--dry-run`/`--apply`、SHA-256 指纹、报告。

- `Program.cs` — 顶层语句入口：解析参数/连接串、构造 DbContext、运行导入并输出结果码。
- `LegacyJsonImporter.cs` — 导入核心：发现 JSON/算指纹/迁移/播种/逐类导入/写报告与 `LegacyImportRun`/`LegacyImportIssue`。
- `LegacyImportOptions.cs`/`ParseResult.cs` — 命令行选项与解析结果。
- `LegacyImportReport.cs`/`LegacyImportResult.cs`/`LegacyImportStatistics.cs`/`LegacyImportIssueDto.cs` — 报告/结果常量/统计/问题 DTO。
- `JsonElementExtensions.cs` — JsonElement 容错读取 + 旧版单位换算（×10 deciC、×1000 μL）。
- `LegacyPasswordHasher.cs` — PBKDF2-SHA256 密码哈希器。

### tools/ 其他

- `tools/Stainer.Operations/` — 运维工具项目（在主 sln 中，按需构建）。
- `tools/browser-acceptance/` — Playwright 浏览器验收脚本（`mock-stage-acceptance.mjs`、`control-console-acceptance.mjs` 等）。
- `tools/verify-runtime-git-policy.ps1` — 运行时 Git 策略校验脚本。

---

## 文档与数据目录

非代码目录，简要说明。

- `docs/` — 项目文档：`PROJECT_HANDOFF.md`（交接）、`backend-mock-contract-v1.md`（Mock 契约）、`control-console-mapping.md`（控件→API 映射）、`reagent-hardware-bridge.md`（试剂硬件旁挂框架）、`engineer-diagnostics-guide.md`、`he-dry-run-preflight-checklist.md`、`mock-runtime-validation-guide.md`、`mock-stage-completion-report.md`、`aspnet-core-web-host.md`、`ui-api-mapping.md`、`index.md`（本文件）。
- `project_context/` — 架构规约与待办：`PROJECT_CONTEXT.md`（项目宪法级文档，约 105KB）、`TODO.md`（当前任务清单）、`CODEX_GUIDE.md`（与 GPT/Codex 协作规范）。
- `real-hardware/` — 真实设备取证与门禁：`socon-sdk-compatibility-report.md`（SOCON SDK 兼容性取证）、`真实设备接口参数清单.md`（P0-03）、`真实运动坐标参数与现场校准方案.md`（P0-05）、`真实运行安全门禁与异常恢复规则.md`（P0-04 门禁草案）。
- `coordinate-baseline/` — 坐标基线 CSV（全部 SVG 控件 ID 的毫米坐标与尺寸）。
- `data/` — 运行时数据：`stainer.db` 主库、`stainer.db.bak.*` 备份、`machine-executor.lock` 执行器锁、`_seed_test_data.py` 种子脚本、`logs/`（按日期分片 `device-*.jsonl`/`runtime-*.jsonl`）、`backup-attempts/`。
- `.config/` — dotnet 本地工具清单（`dotnet-ef` 9.0.5）。
