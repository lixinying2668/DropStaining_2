# PROJECT_CONTEXT.md

# 全自动冰冻切片染色机上位机（Stainer）项目上下文

> 本文件用于持续维护项目的技术、业务、设备、数据和开发阶段上下文。  
> 它描述的是**项目当前确认采用的方案与事实状态**，不是聊天纪要。  
> 未经现场验证的真实硬件能力必须明确标为“未完成 / 待确认”，不得被视为已具备能力。

---

## 1. 项目简介

### 1.1 项目名称与定位

- **项目名称**：全自动冰冻切片染色机上位机（Stainer）。
- **本地项目根目录**：`D:\Stainer`
- **产品形态**：运行在 Windows 本机上的染色设备主机软件。
- **界面形态**：本地 Web HMI，面向触屏/本地浏览器操作。
- **当前业务范围**：
  - HE 染色；
  - IHC 染色；
  - 样本、试剂、DAB、工作流、设备状态、报警、审计、工程诊断与运行追溯。
- **当前阶段定位**：
  - Mock 业务能力、正式数据模型和正式 Web HMI 接入已形成提交基线；历史浏览器验收曾完成，但当前 HEAD 后端完整回归有 61 项失败，单页浏览器验收仍待重跑；
  - 真实设备接入仍处在“离线协议、接口取证、真实动作前基础设施”阶段；
  - 尚未进入真实机械臂动作、真实湿实验或真实性能验收。

### 1.2 项目状态记录与事实优先级

本文件记录的项目状态截至 **2026-07-20，主仓库 HEAD `97d8539`**。P0-01 SOCON 兼容性报告、P0-02 Bridge 正式 net452 x86 构建验证和 P1-01 SDK 运行时部署验证均已完成；主控真实串口只读软件边界与 SOCON Bridge 受门禁控制的真实只读会话代码已经提交。**尚未连接真实硬件、未打开真实 COM/CAN、未执行真实扫码、未回零、未运动、未执行任何真实液路或温控动作。**当前新增能力表示“真实只读软件基础已进一步完成”，不代表 P1-02 / P1-03 的现场验收已经通过。

为避免后续维护时将旧资料、Mock 行为或现场猜测混入正式结论，事实优先级如下：

1. 已经确认并进入当前正式代码/数据库模型的实现与测试结果；
2. 后续确认的硬件接口资料、现场事实与正式变更记录；
3. 数字孪生坐标 CSV 对于 XY 坐标冲突的裁决；
4. 《全自动冰冻切片染色机上位机软件需求规格说明书（完整修订版 v2.3）》作为业务与验收基线；
5. 历史原型、早期文档和未验证推测，仅可作为参考。

当第 2、3 项与旧需求文档或旧原型冲突时，应以已确认的后续事实为准，并在对应配置版本、审计或变更文档中留下追溯记录。

SOCON 兼容性报告的技术结论已经完成（P0-01 已收口，已提交至 HEAD `e7c127df`），23D Bridge 骨架已完成并提交，P0-02 Bridge 正式 net452 x86 构建验证已完成（VS2022 MSBuild Release|x86，59 checks passed）；后续仍必须以当前仓库内文件内容和 `git status --short` 为准，不能仅凭本文件推定。

### 1.2.1 2026-07-20 最新提交与验证进展

- 当前 Git HEAD 为 `97d8539`（`feat: add gated SOCON read-only sessions`），`master` 与 `origin/master` 指向同一提交；本轮文档同步前跟踪文件干净，当前 `README.md`、`project_context/PROJECT_CONTEXT.md`、`project_context/TODO.md` 为计划内修改；另有未跟踪的 `tmp-bridge-build/`、`tmp-hardware-tests-build/`，均视为待核查临时产物，不得直接提交。
- 7 月 17 日未提交的 Workflow 规划规则、Mock 运行态重置、通道/玻片状态、DAB/预检/数字孪生运行衔接及浏览器验收修订已经分批提交；`PersistWorkflowPlanningRules` 迁移也已进入提交历史。后续又补充了测试临时目录治理、Mock 基线重置、工作流选择约束、调试模块接线、混匀参数和清洗阀配置持久化。
- 主控只读软件边界已在 `b100217` 完善：只允许白名单读取工作状态、节点状态及 0–3 板温控当前值/目标值/开关状态；补齐真实串口单请求打开/关闭、关闭失败诊断、超时/断连语义和离线测试。该提交不代表真实 COM 已打开或主控状态已经现场读取。
- SOCON Bridge 已在 `97d8539` 增加受控只读会话：`OpenConfiguredReadOnlySession`、`GetConfiguredNodeBasicStatus`、`GetConfiguredAxisPositions`、`CloseConfiguredReadOnlySession`；采用命令行 + 本机配置双门禁、SDK 新鲜部署检查、USB/白名单/代表节点/轴映射校验，并保持无初始化、无回零、无运动、无泵和无液路命令。当前只完成代码和离线自检结构，尚未连接 USB2CAN 或真实节点。
- 当前完整后端测试已能编译并运行，不再存在 7 月 17 日的 10 个 CS0117 编译阻塞。2026-07-20 实际执行 `dotnet test backend/Stainer.Tests/Stainer.Tests.csproj --no-restore`：**总计 313，252 通过、61 失败、0 跳过**。失败主要集中在新增种子数据后测试仍假定查询结果唯一（`Sequence contains more than one element` / `Assert.Single`），以及 Workflow 已发布版本可修改等契约预期差异；当前完整回归明确为失败，不是权限或环境阻塞。
- 2026-07-20 对当前 HEAD 执行正式 `dotnet build bridges/Stainer.SoconBridge/Stainer.SoconBridge.csproj --configuration Release --no-restore -p:Platform=x86`：**0 warning / 0 error**；随后从 `bin/x86/Release` 运行 `--self-test`：**93 checks passed**。自检使用模拟部署/适配器覆盖门禁和只读会话，没有连接 SDK 或真实硬件；诊断段的 SDK `Result: FAIL` 表示本轮未配置 SDK，不能与 P1-01 的受控 SDK 部署验证混为一谈。
- 真实 COM/CAN/TCP、真实扫码、真实温控/混匀/液路动作仍未执行；真实设备第一次测试应先完成只读连接、状态读取和正常断开，机械臂继续禁止回零和运动。DCR55 可在连接阶段通过后另设受控单次扫码阶段，但不得自动重试、移动机械臂或绑定真实患者样本。

### 1.3 项目目标

项目最终应实现一套可追溯、可审计、可配置、可维护且以安全为前提的冰冻切片自动染色上位机，支持：

1. 以四个抽屉通道完成 HE / IHC 批次管理；
2. 以患者/样本、试剂、抗体、工作流和玻片位为基础形成完整运行上下文；
3. 对试剂有效性、扫码结果、余量、DAB 生命周期、工作流快照、设备状态和安全互锁进行启动前预检；
4. 管理温控、制冷、液路、双针、机械臂、洗针和资源锁；
5. 实现运行记录、试剂账本、审计日志、报警、设备通信记录、配置版本与历史追溯；
6. 先在 Mock 模式验证业务闭环，再以 fail-closed 方式逐步接入真实设备；
7. 真实设备接入必须按“部署验证 → 只读验证 → 安全 IO → 校准 → 空跑 → 湿实验”的顺序推进，不允许跳级。

---

## 2. 业务范围与核心约束

### 2.0 需求基线与尚待确认的工程参数

业务需求基线为《全自动冰冻切片染色机上位机软件需求规格说明书（完整修订版 v2.3）》。该原始 DOCX 当前不在 Git 工作树中；本文件仅记录此前已确认并落入代码/文档的需求结论，如需重新逐条追溯原文，必须另行取得受控版本，不能假定仓库内存在该文件。

已由后续确认资料覆盖或进一步限定的内容包括：

- XY 坐标基线以 `coordinate-baseline/position_coordinates_with_slide_rectangles_numbered.csv` 为准；
- 当前正式范围仅 HE 与 IHC；
- 实际设备接口以 `real-hardware/真实设备接口参数清单.md` 和后续现场确认事实为准；
- Mock 完成不等于真实设备能力完成。

需求中仍需在真实设备阶段现场确认、配置或验证的关键工程参数包括：

- 温控稳定判据；
- 加热超时；
- 过温阈值与响应；
- 制冷稳定与异常判据；
- 断电恢复和运行中断恢复；
- 真实安全 IO 逻辑；
- 现场性能与稳定性验收；
- Kiosk/全屏、开机启动和部署恢复流程。

### 2.1 当前实验类型

当前正式支持的实验类型只有：

- `HE`
- `IHC`

不在当前正式范围内的类型不应通过“临时工作流”绕过业务模型接入。

### 2.2 通道、抽屉与玻片位

设备包含四个物理通道/抽屉：

| 通道编号 | 抽屉 |
|---|---|
| Channel 1 | A |
| Channel 2 | B |
| Channel 3 | C |
| Channel 4 | D |

每个抽屉有四个玻片位：

| 槽位 | 温控点 |
|---|---|
| Slot1 | point 0 |
| Slot2 | point 1 |
| Slot3 | point 2 |
| Slot4 | point 3 |

总玻片位数：**16**。

### 2.3 样本码、通道批次与工作流规则

样本码规则：

- 样本二维码采用 16 位编码，按 4×4 分组表达；
- IHC 流程可通过扫码和 LIS/映射结果完成样本信息确认；
- HE 当前没有二维码时，允许人工确认；
- LIS 的真实接口尚未接入，当前业务验证使用 Mock LIS。

通道批次与工作流规则：

1. 一个通道是一个独立运行批次边界。
2. 一个通道只能选择一种实验类型：`HE` 或 `IHC`。
3. 操作员只选择实验类型，不选择具体工作流版本。
4. 后端自动将通道绑定到该实验类型对应的**默认、已发布的工作流快照**。
5. 同一通道内所有玻片共享相同工作流快照。
6. 不同通道之间可以配置不同实验脚本/工作流快照；同一个通道内的 4 个玻片共享同一种实验脚本。
7. IHC 通道中，玻片可保留各自确认的一抗代码；一抗代码不改变通道共享工作流。
8. HE 当前允许无二维码的人工确认流程。
9. 一个通道内即使只有 1–3 张玻片，也视为完整批次；不要求必须放满四张。
10. 批次开始后禁止继续向该通道追加玻片。

### 2.4 运行与暂停规则

- 启动前必须执行预检；预检不通过不得启动正式运行。
- 暂停后不继续加热。
- 暂停导致孵育计时归零。
- 恢复后不需要重新加一抗；从允许的后续步骤继续。
- 故障时整机停止，不自动恢复。
- 恢复或重做必须从当前大步骤的起点开始，并保留可审计记录。
- 已加载针内液体、资源锁、运行状态和异常状态不得在重启、暂停或未知状态后被静默清除。

### 2.5 真实设备动作前的最低门禁

真实模式下，任何会导致物理设备动作的命令至少必须同时受以下约束：

- 已确认设备模式为 Real；
- 已确认设备 Transport 可用；
- 已确认对应设备连接、通信和状态读取；
- 已确认真实安全 IO；
- 已确认坐标版本、坐标快照和校准状态；
- 已确认 Z、安全高度、接近/离开参数；
- 已确认工作流、液类、试剂和 DAB 运行快照；
- 已确认资源锁、针状态和液路状态；
- 已确认权限和运行上下文；
- 已完成启动前预检；
- 任一关键条件未知、失效、断连、超时或无法验证时，必须 fail-closed。

---

## 3. 当前开发阶段

### 3.1 阶段总览

| 阶段 | 状态 | 说明 |
|---|---|---|
| 正式后端、SQLite、Mock 基础 | 已完成 | .NET 9 / EF Core / SQLite / Mock 设备基础 |
| DAB 生命周期 | 已完成 | M1–M8、有效期、重配、清洗与账本 |
| 坐标与液类版本化 | 已完成 | 发布、启用、冻结快照、差异管理 |
| 温控、制冷、液路 Mock | 已完成 | 正式领域状态、API、SignalR、fail-closed Real |
| 双针、资源锁、执行器 | 已完成 | 正式状态机、针内液体与资源保守调度 |
| 工程诊断、管理与审计 | 已完成 | 工程会话、CSV、配置管理、通信记录 |
| Mock 端到端后端验收 | 历史基线已完成；当前回归失败 | HE/IHC、DAB、跨瓶、暂停恢复等能力已实现；当前 HEAD 完整测试为 252/313 通过、61 失败，发布前须修复 |
| 操作员 UI 正式数据接入 | 已完成 | `/control-console` 单页中的样本、试剂、DAB 与运行视图 |
| 工程/管理/历史/报警 UI 接入 | 已完成 | 正式追溯与敏感信息分层 |
| 浏览器验收与 Mock 阶段收口 | 历史基线已完成；当前待复核 | 既有 Playwright Core、重连、多窗口、权限验收曾通过；当前 UI 已收敛到单页且后端回归为红，需重新执行浏览器验收 |
| 数字孪生 XY 基线导入 | 已完成 | DigitalTwinXY-v1、真实动作硬门禁 |
| 主控/DCR55/制冷离线协议 | 已完成 | 主控协议、真实通讯软件基础设施、只读通讯边界与离线验证已完成，真实硬件连接和状态验证未开始、真实控制未开放；制冷状态不变；DCR55 离线协议与离线 Real Adapter 读取边界已完成，真实 Serial Transport 代码已完成，真实单次扫码验证未执行 |
| Real 读取边界 | 已完成 | 只读边界、真实写入 fail-closed |
| 扫码器相关后端配置能力 | 已完成 | 扫码器配置管理、扫码区域配置、试剂坐标锚点生成、DCR55 控制服务抽象；已确认样本 DCR55 与试剂主控扫码模块职责分离；不代表真实设备已接入 |
| 坐标 Z 语义补齐 | 已完成 | Z-Travel→SafeZUm、Z-Start→LiquidDetectZUm、Z-End→AspirateEndZUm、Z-Dispense→DispenseZUm；仅补齐配置表达与冻结快照，未接入 Motion 执行逻辑 |
| LiquidClass 参数补齐 | 已完成 | LiquidClassVersion 新增 LiquidFollowingDepthUm / RetractSpeedUmPerSecond / ConditioningVolumeUl / BreakoffSpeedUlPerSecond / PostDispenseAirGapUl；仅补齐配置表达与快照，未接入执行 |
| 工程手动移液测试 API | 已完成 | LiquidDetect / Aspirate / Dispense / Wash / Flush；engineer/admin 权限 + EngineeringSession + CommandIdempotency + AuditLog；Real 模式 fail-closed；未连接真实设备、未执行真实移液动作 |
| Workflow 通道关联模型评估 | 已完成（评估） | 现有 WorkflowDefinition / WorkflowVersion / ChannelBatch 已满足“同通道 4 玻片共享单一 Workflow、不同通道可选不同 Workflow”，无需另建关联模型；PlanningRulesJson 字段、迁移和相关修订已提交 |
| SOCON SDK 兼容性取证（23D-0） | 已完成 | 已确认独立 x86 Bridge 架构；不代表主项目可直接引用 SDK 或设备可动作 |
| SOCON 独立 x86 Bridge 骨架（23D） | 已完成并已提交 | 独立 net452 x86 Console；未加入 Stainer.sln；仅离线 IPC 与部署前检查 |
| Bridge 正式 net452 x86 构建验证（P0-02） | 已完成 | VS2022 MSBuild Release|x86 成功；输出 bin\x86\Release\Stainer.SoconBridge.exe；--self-test 59 checks passed；未加载 SDK/未连接硬件/未扫描总线 |
| SDK 运行时部署验证（P1-01） | 已完成 / ready-to-close | 进程级 `STAINER_SOCON_SDK_DIR` 设置后 Release|x86 Rebuild + `--self-test` 60 checks PASS；核心文件（SOCON.API/SOCON.Utility/can_bootloader）齐、`can_bootloader.dll` PE=x86(0x014C)、SOCON.API/Utility 托管元数据加载 PASS、5 类型 available（SCDevice/SCDeviceMA/Utility+e_ConnectType/Utility+DeviceTypeEnum/Utility+ProtocolTypeEnum）；optional warning（ScEventBus/C1.C1Zip）缺失但不阻塞；未连接硬件/未打开 COM/CAN/TCP/未实例化 SDK/未调用真实动作 |
| SOCON 受门禁真实只读会话软件基础（P1-02 前置实现） | 已完成并已提交，待现场验证 | Bridge 支持受双门禁和白名单约束的会话打开、代表节点初始化/回零状态读取、X/Y/Z1/Z2 静态位置读取和确认关闭；不支持初始化、回零、运动或液路动作；真实 USB2CAN/节点尚未连接 |
| 真实运行安全门禁与异常恢复规则（P0-04 草案） | 规则草案编制中 / 待评审 / 未完成 | 已产出 `real-hardware/真实运行安全门禁与异常恢复规则.md`（草案 v0.1）：覆盖真实动作前总门禁、安全 IO 异常规则矩阵、通讯异常规则、运行中断与恢复规则、温控/制冷安全规则、真实能力分级门禁、P1 阶段阻塞关系、待现场确认清单；所有未现场确认内容标注「待现场确认」，未含真实敏感现场参数；**不代表**真实安全 IO 已接入，**不代表**可进入 P1-02 |
| 真实运动坐标参数与现场校准方案（P0-05 草案） | 校准方案草案编制中 / 待评审 / 未完成 | 已产出 `real-hardware/真实运动坐标参数与现场校准方案.md`（草案 v0.1）：仅以 DigitalTwinXY-v1 作为 Draft / MockOnly / Unverified 的 XY 设计基线；真实 Z、高度、速度、加速度、软限位、轴映射和现场校准值均待现场确认；**不代表**真实坐标已校准或机械臂可运动，**不放行 P1-08** |
| 真实设备连接/只读 | 软件基础进行中，现场验证未开始 | 主控和 SOCON 只读边界已加强；仍需真实硬件、显式端口/NodeID/映射、安全状态和现场授权 |
| 真实动作、校准、湿实验 | 未完成 | 禁止提前实施 |
| 实际 LIS 接入 | 未完成 | 仅 Mock LIS 已完成 |

### 3.2 当前最新已知验证状态

- Mock 后端验收：历史基线曾完成；当前 HEAD 完整回归为 252/313 通过、61 失败，当前验收未通过。
- 操作员 UI 正式数据接入：已完成。
- 工程/管理/历史/报警 UI 正式数据接入：已完成。
- 浏览器验收：历史基线曾完成；当前 `/control-console` 单页最新回归待重跑。
- 数字孪生 XY 导入及门禁验证：已完成。
- 主控、DCR55、制冷离线协议测试：已完成。
- 离线 Real Adapter 读取边界测试：已完成。
- Main Controller：主控真实通讯软件基础设施完成；白名单读取已覆盖工作状态、节点状态和 0–3 板温控当前值/目标值/开关状态，并补齐单请求串口打开/关闭、关闭失败、超时和断连语义；真实主控连接、真实状态读取与真实控制均未开始。
- SOCON Bridge 真实只读软件基础：已支持受双门禁、SDK部署检查、USB连接类型、端口/速率、NodeID白名单、代表节点和轴映射校验约束的只读会话；允许读取代表节点初始化/回零状态和配置轴静态位置，并要求确认关闭后清除缓存。真实 USB2CAN、NodeID和位置读取均未现场验证。
- DCR55 Serial Transport：代码已完成；`SerialPort` 隔离在 Infrastructure Transport 层，Mock / Real 隔离未受影响，Protocol、Real Adapter 与 Serial Transport 离线测试均已通过；真实设备验证仍未开始。
- 扫码器相关后端配置能力：`ScannerProfile`、`ScannerRegion`、`scanner_profiles`、`scanner_regions` 与配置管理 API 已完成；支持扫码器基础配置、通信参数、ROI 配置字段、校验光状态字段和扩展参数；当前表示配置管理能力完成，不代表真实设备参数已生效。
- 扫码区域配置能力：`ScannerRegion` 已扩展区域编号、扫码顺序、坐标档案关联和坐标版本关联；后端已具备样本区域扫码配置表达能力，不代表前端页面或机械臂自动扫码流程完成。
- 试剂区坐标配置生成能力：`ReagentCoordinateAnchor`、试剂列首尾坐标配置和坐标插值生成服务已完成；生成规则为首尾坐标作为锚点，X/Y/Z 独立线性插值，并使用既有 `CoordinateProfileVersion` 版本体系保存；不代表机械臂实际校准完成。
- DCR55 扫码控制服务能力：`ScannerControlService`、Restart、Calibration Light 和 ROI 后端接口已完成；当前表示 DCR55 后端控制服务抽象和命令封装完成，不代表真实串口控制、真实 ROI 效果、真实校验光效果或硬件联调已完成。
- 扫码设备职责与坐标语义：DCR55 / `ArmCamera` 当前按机械臂末端样本扫码头理解，后续建议统一命名为 `DCR55Scanner` 或 `SampleScanner`；它是工具设备 / 工具参考，不是运动目标点；样本扫码应由对应 Slot 的扫码坐标或 `ScannerRegion` 表达，机械臂到位并静止后触发 DCR55；`SampleScan` 不应默认表示固定扫码工位，如保留应作为 `SampleScannerCalibrationPoint` 一类校准 / 测试 / 参考点。
- 试剂扫码职责：试剂瓶二维码由主控板固定多通道扫码模块 `0x08` 负责，不使用机械臂末端 DCR55，也不使用 `SampleScan` 位置；`ch1–ch5` 与 5 列试剂架、R 位编号之间的实际映射仍需现场确认，不能写死。
- 坐标 Z 语义补齐：`CoordinatePoint.AspirateEndZUm`（Z-End）已新增；Z 语义映射为 Z-Travel→SafeZUm、Z-Start→LiquidDetectZUm（别名 AspirateZUm）、Z-End→AspirateEndZUm、Z-Dispense→DispenseZUm；`AspirateEndZUm` 为 nullable 且未纳入 Real readiness 门禁，旧坐标数据兼容；仅补齐配置表达与冻结快照，未接入 Motion 执行。
- LiquidClass 参数补齐：`LiquidClassVersion` 新增 `LiquidFollowingDepthUm`、`RetractSpeedUmPerSecond`、`ConditioningVolumeUl`、`BreakoffSpeedUlPerSecond`、`PostDispenseAirGapUl`；System Trailing Air Gap 复用既有 `TrailingAirGapUl`；完整接入配置写入、校验、差异、冻结快照与读取；未接入执行。
- 工程手动移液测试能力：`EngineeringPipettingService` 支持 LiquidDetect / Aspirate / Dispense / Wash / Flush；Purge / ClearChannel 明确列为 `UnsupportedOperations`，不凭空模拟；具备 engineer/admin 权限、EngineeringSession、CommandIdempotency、AuditLog，Real 模式 fail-closed；未连接真实设备、未执行真实移液动作。
- Workflow 通道关联模型评估：`WorkflowDefinition`（脚本定义）+ `WorkflowVersion`（脚本版本）+ `WorkflowStep` / `WorkflowReagentRequirement` + `ChannelBatch.SelectedWorkflowVersionId` 已满足实验流程加载和通道级绑定需求；无需另建关联模型。当前为规划约束新增的 `PlanningRulesJson` 是既有 `WorkflowVersion` 的扩展，不推翻通道关联结论，但确实引入新的数据库字段和 Migration。
- 当前阶段结论：7 月 17 日的 Workflow、DAB、预检和运行衔接修订已经提交；主控与 SOCON 的真实只读软件边界进一步完成。项目正处于“首次真实设备只读测试准备”阶段，仍未进入真实硬件验收或真实动作阶段。
- 历史主项目测试基线：曾有 **218/218 tests passed，TEST_EXIT=0**；该结果不代表当前 HEAD。2026-07-20 当前 HEAD 完整回归为 **252/313 通过、61 失败、0 跳过**，必须修复并重跑后才能恢复通过结论。
- Bridge 历史基线：P0-02 于 2026-07-08 正式 net452 x86 构建并通过 59 checks；P1-01 受控 SDK 部署验证通过 60 checks。当前 HEAD 于 2026-07-20 重新正式构建为 **0 warning / 0 error**，离线 `--self-test` **93 checks passed**；本轮未配置 SDK、未连接硬件。
- Bridge 正式构建状态：**已完成**（P0-02 已关闭）。
- P1-01 SDK 运行时部署验证：**已完成 / ready-to-close**——已配置 SDK 目录后完成运行时验证：核心文件（SOCON.API/SOCON.Utility/can_bootloader）齐、`can_bootloader.dll` PE=x86(0x014C)、SOCON.API/Utility 托管元数据 `ReflectionOnlyLoadFrom` 加载 PASS、5 类型 available（SCDevice/SCDeviceMA/Utility+e_ConnectType/Utility+DeviceTypeEnum/Utility+ProtocolTypeEnum）、`--self-test` 60 checks PASS；optional warning（SOCON.ScEventBus.dll、C1.C1Zip.4.dll）缺失但不阻塞；未连接真实硬件、未打开 COM/CAN/TCP、未实例化 SDK 类型、未调用真实动作。
- **风险备注**：`ReflectionOnlyLoadFrom()` 仅做元数据反射验证，不执行设备动作；P1-01 通过**不代表**真实机械臂已连接或可运动。
- 之前 testhost 异常：未稳定复现，不能作为已定位根因记录。
- 正式数据库：`PersistWorkflowPlanningRules`、`AddSerialConnectionProfile`、`AddPrecisionCalibrationProfile`、`AddMixerParameterProfile`、`AddWashValveConfigProfile` 等迁移已进入提交历史；当前仍需在隔离数据库重新验证迁移应用、旧数据兼容和无 pending migration，不能仅沿用旧结论。
- 真实机械臂、真实串口、真实 USB2CAN、真实安全 IO、真实湿实验：未开始。

> 说明：测试数量是已知阶段性结果，不应被视为未来每次提交后的固定测试基线。每次新阶段必须重新记录实际构建与测试结果。

---

## 4. 技术栈

### 4.1 正式主系统

| 层级 | 技术/方案 |
|---|---|
| 后端 | .NET 9 ASP.NET Core |
| 数据访问 | Entity Framework Core |
| 正式数据库 | SQLite |
| 实时通信 | SignalR |
| Web HMI | 本地 Web 前端 |
| 前端实现 | 静态 HTML / JavaScript / CSS 与服务端页面渲染基础 |
| 设备模式 | DI 注入 Mock / Real Adapter |
| 浏览器验收 | Playwright Core |
| 数据库运行模式 | SQLite WAL、Foreign Keys、Busy Timeout |
| 审计与追溯 | 正式数据库实体与应用服务 |
| 配置治理 | 版本化、发布、启用、冻结快照 |
| 真实设备原则 | Real 模式默认 fail-closed |

### 4.2 SOCON Bridge

| 项目 | 约束 |
|---|---|
| 进程类型 | 独立 Console Process |
| 目标框架 | .NET Framework 4.5.2 |
| 平台目标 | x86 |
| 是否加入 `Stainer.sln` | 否 |
| IPC | Windows Named Pipe |
| SDK 文件 | 仅本机部署，不进入 Git |
| 当前允许能力 | 离线状态/部署检查；在双启用、SDK新鲜部署检查和本机白名单配置全部通过后，受控打开USB2CAN只读会话、读取代表节点初始化/回零状态与配置轴静态位置、确认关闭会话 |
| 当前禁止能力 | 初始化、回零、运动、等待动作、液面检测、吸液、加液、泵动作、自动扫描端口/NodeID，以及任何未列入白名单的通用SDK调用 |

---

## 5. 系统整体架构

### 5.1 当前正式架构

```text
┌─────────────────────────────────────────────────────────┐
│                         本地 Web HMI                     │
│ /control-console 唯一正式页面                             │
│ 总览 / 样本 / 试剂 / DAB / 运行 / 历史 / 工程 / 管理 / 报警 │
└─────────────────────────────────────────────────────────┘
                           │ HTTP / SignalR
                           ▼
┌─────────────────────────────────────────────────────────┐
│                  ASP.NET Core Web Host                   │
│                     backend/Stainer.Web                  │
│ API / SignalR Hub / 页面入口                             │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│                   Application Services                   │
│ Workflow / Batch / Samples / Reagent / DAB               │
│ Runtime / Motion / Thermal / Cooling / Fluidics          │
│ Coordinate / Liquid Class / Snapshot / Audit / Alarm     │
│ Diagnostics / Backup / Configuration                     │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│                     EF Core + SQLite                     │
│                   data/stainer.db                        │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│                    Device Adapter Boundary               │
│ Mock Adapter / Offline Real Read Boundary / Real Adapter │
└─────────────────────────────────────────────────────────┘
                   │                               │
                   ▼                               ▼
         Mock Runtime Simulation         Future Real Device Transport
                                          Main Controller / DCR55 /
                                          Cooling / SOCON / USB2CAN
```

### 5.2 未来 SOCON 接入架构

```text
┌─────────────────────────────────────────────────────┐
│ Stainer 主项目                                       │
│ .NET 9；平台目标不改变；不直接依赖 SOCON SDK         │
└─────────────────────────────────────────────────────┘
                        │ Named Pipe IPC
                        ▼
┌─────────────────────────────────────────────────────┐
│ Stainer.SoconBridge                                  │
│ .NET Framework 4.5.2 x86                             │
│ 未来隔离 SOCON SDK、原生 DLL 和厂商运行时依赖        │
└─────────────────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────┐
│ SOCON API SDK / can_bootloader.dll / USB2CAN / 机械臂│
└─────────────────────────────────────────────────────┘
```

### 5.3 正式业务权威

以下层级是唯一业务权威：

```text
ASP.NET Core Application Services + EF Core + SQLite
```

以下内容只能用于模拟、兼容、测试或历史参考，不得成为正式业务权威：

- `MockRuntimeStore`；
- 浏览器内存状态；
- Mock 页面状态；
- 临时测试数据库；
- 测试适配器；
- 历史 FastAPI/Jinja/Python 原型（已于步骤 6 删除）；
- 旧控制台接口；
- 任何直接以 UI 或设备适配器写入数据库的实现。

---

## 6. 已知目录结构

> 下列是已确认的重要目录，不代表完整仓库文件树。

```text
D:\Stainer\
│
├─ Stainer.sln
│
├─ backend\
│  └─ Stainer.Web\
│     ├─ Stainer.Web.csproj
│     └─ wwwroot\
│        └─ twin\
│           └─ index.html              # /control-console 唯一正式页面
│
├─ data\
│  ├─ stainer.db
│  ├─ stainer.db-wal
│  ├─ stainer.db-shm
│  ├─ backups\
│  └─ corrupt-backups\
│
├─ docs\
│  ├─ backend-mock-contract-v1.md
│  ├─ mock-runtime-validation-guide.md
│  ├─ engineer-diagnostics-guide.md
│  ├─ he-dry-run-preflight-checklist.md
│  ├─ mock-stage-completion-report.md
│  └─ ...
│
├─ tools\
│  └─ browser-acceptance\
│     └─ mock-stage-acceptance.mjs
│
├─ coordinate-baseline\
│  └─ position_coordinates_with_slide_rectangles_numbered.csv
│
├─ real-hardware\
│  ├─ 真实设备接口参数清单.md
│  ├─ 真实运行安全门禁与异常恢复规则.md   # P0-04 草案 v0.1（规则草案编制中 / 待评审 / 未完成）
│  └─ socon-sdk-compatibility-report.md
│
├─ bridges\
│  └─ Stainer.SoconBridge\            # 23D 已创建并提交
│
└─ package.json
```

### 6.1 数据库目录规则

正式数据库：

```text
D:\Stainer\data\stainer.db
```

SQLite WAL 辅助文件：

```text
D:\Stainer\data\stainer.db-wal
D:\Stainer\data\stainer.db-shm
```

规则：

1. 数据库运行期间不得单独删除 `.db-wal` 或 `.db-shm`。
2. 正式备份目录为 `data/backups/`。
3. 可疑或损坏历史备份隔离在 `data/corrupt-backups/`。
4. 不得盲目从 `corrupt-backups/` 恢复。
5. 临时备份尝试文件不得污染正式备份目录。
6. 恢复前必须执行完整性、迁移、表结构和基础查询验证。

### 6.2 历史原型规则

已删除的 `src/` FastAPI/Jinja/Python 实现原仅作历史参考；该目录不属于当前仓库入口：

- 不用于正式部署；
- 不作为正式 Web Host；
- 不作为正式数据库权威；
- 不承载当前设备接入；
- 不得与当前 `.NET 9` 正式实现混用。

---

## 7. 模块职责

### 7.1 样本与批次模块

职责：

- 管理 A–D 通道和 16 个玻片位；
- 管理样本录入、扫码、LIS 查询和人工确认；
- 管理通道 HE/IHC 类型；
- 绑定默认已发布工作流快照；
- 管理 IHC 单玻片一抗代码；
- 防止批次启动后追加玻片；
- 提供操作员样本页与运行前基础数据。

### 7.2 工作流与运行模块

职责：

- 管理工作流默认版本与发布状态；
- 在批次创建时冻结工作流快照；
- 执行 HE/IHC 染色流程；
- 管理大步骤、原子步骤、暂停、恢复、停止和异常；
- 管理运行命令生命周期；
- 管理预检、资源锁、耗材账本和执行轨迹；
- 保证历史运行不静默引用最新配置。

### 7.3 试剂与库存模块

职责：

- 管理 R1–R40 试剂架；物理试剂架为 5×8；试剂扫码通道 / 列式位置标记与既有 R1–R40 业务编号的映射仍需现场统一确认；
- 管理试剂扫码、状态、有效期、余量和替换；
- 管理试剂目录、试剂码、抗体映射；
- 管理多瓶顺序使用、跨瓶预约和跨瓶消耗；
- 管理必要试剂门禁；
- 将试剂预约和消耗写入正式账本与审计。

### 7.4 DAB 生命周期模块

职责：

- 管理 DAB A/B/水配比；
- 管理 M1–M8 位置；
- 管理配制、使用、过期、重配、清洗确认；
- 管理跨瓶预约与水源；
- 管理三小时有效期；
- 管理未知状态与资源保护；
- 提供运行、管理、审计和 DAB UI 数据。

### 7.5 坐标与运动模块

职责：

- 管理坐标配置版本；
- 管理 Draft / Publish / Enable / Disable；
- 管理坐标快照；
- 管理坐标导入与数字孪生基线；
- 管理真实动作坐标门禁；
- 管理双针几何、软限位、资源锁和移液操作生命周期；
- 未来管理真实 SOCON / 机械臂映射，但当前不得实际动作。

### 7.6 温控、制冷与液路模块

职责：

- 管理 16 个温控点；
- 管理制冷状态和遥测；
- 管理泵、混合模块、液位和液路遥测；
- 在 Mock 中提供完整模拟；
- 在 Real 模式中保持 fail-closed；
- 未来接入主控、独立制冷、PWM 和混合模块真实通讯。

### 7.7 设备通信与 Real Adapter 模块

职责：

- 定义设备抽象和 Mock / Real 边界；
- 在调用设备动作前记录 Pending；
- 记录 Complete / Failed / Pending / Unknown 结果；
- 防止数据库异常导致重复真实动作；
- 提供离线协议和只读边界；
- Main Controller 已具备真实通讯软件基础设施和 Transport 路由能力，但尚未连接真实主控、打开真实 COM 或开放真实控制命令；
- DCR55 已具备真实 Serial Transport 实现，并已补齐后端 `ScannerControlService` 命令封装（Restart / Calibration Light / ROI）；但尚未完成真实串口控制验证、真实 ROI 效果验证、真实校验光效果验证，也尚未开放样本/LIS 业务扫码闭环；
- 在真实硬件未验证前，后端服务抽象不等同于真实硬件写入放行；未配置或不支持的真实写入仍必须失败闭合。

### 7.8 工程、管理、审计与报警模块

职责：

- 管理工程会话和工程师二次认证；
- 管理用户、角色、默认工作流、抗体映射、试剂目录；
- 管理坐标和液类版本；
- 提供诊断、通信记录、错误记录和 CSV 导出；
- 提供报警、审计、运行历史和友好操作员信息；
- 将原始技术信息限制在工程/管理权限范围内。

### 7.9 Web HMI 与 SignalR 模块

职责：

- 在 `/control-console` 单页中为操作员提供总览、样本、试剂、DAB 与运行视图；
- 为工程师/管理员提供 Engineering、Management、History、Alarms 页面；
- 通过 SignalR 发送状态变化；
- 重连后从正式服务获取完整状态快照；
- 不将浏览器内存或 SignalR 消息作为权威状态；
- 在操作员端隐藏路径、状态哈希、原始数据库错误、原始通信和内部错误码。

---

## 8. 已完成模块与阶段性成果

### 8.1 基础架构与正式数据基础（00–07）

已完成：

- .NET 9 ASP.NET Core 正式后端；
- EF Core + SQLite；
- Mock 设备适配器；
- Real 模式 fail-closed 基础；
- Scanner Mock；
- LIS Mock；
- 正式 API、迁移、DI 与基础测试。

设备模式：

```text
Device:Mode=Mock
Device:Mode=Real
```

原则：

- Mock 可执行模拟；
- Real 未验证时不得执行真实动作；
- Real 不允许静默回退为 Mock 成功。

### 8.2 DAB 生命周期（08–09B）

已完成：

- `DabLifecycleService`；
- M1–M8 管理；
- 三小时有效期；
- 重配计划；
- 清洗确认；
- 跨瓶预约和消耗；
- SystemWater 来源；
- 历史旧 DAB 记录标记：
  - `LegacyUnverified`
  - `NeedsManualResolution`
- 未知状态不自动释放资源。

### 8.3 坐标、快照与 Liquid Class（10–12）

已完成：

- 坐标版本化；
- 坐标发布、启用、差异和快照；
- 运行绑定坐标快照；
- 活动坐标唯一性保护；
- Liquid Class 版本化；
- Liquid Class 发布、启用、差异和快照；
- 并发启用保护；
- 运行不应自动取最新版本替代冻结版本。

### 8.4 温控、制冷与液路 Mock（13–14）

已完成：

- 16 点温控状态；
- 制冷状态与遥测；
- 泵、混合、液位与液路遥测；
- 初始化与预检接入；
- 正式 API 与 SignalR；
- Mock 模拟；
- Real fail-closed。

### 8.5 双针、资源锁和执行器（15–16）

已完成：

- `RobotArm`、`Needle1`、`Needle2`；
- `PipettingOperation`；
- `MachineResourceLease`；
- 操作状态：
  - `Planned`
  - `Sent`
  - `Acknowledged`
  - `Completed`
  - `Failed`
  - `Unknown`
- 针内液体状态跨暂停、停止、未知状态和重启保留；
- 避免重复库存扣减；
- 双针 25 mm 几何约束；
- 保守串行化资源调度；
- 软限位基础。

### 8.6 工程会话、诊断与配置管理（17）

已完成：

- 工程师二次认证；
- 设备状态、命令日志、错误、通信、CSV；
- 坐标和液类详情、差异、发布、启用、停用；
- 配置导入、导出、预览；
- 设备通信持久化；
- Pending / Complete / Failed 通信结果；
- Pending 不自动重试真实动作；
- 适配器不直接写数据库；
- 调用方持久化结构化设备结果。

### 8.7 Mock 后端端到端验收（18）

已完成：

- HE 端到端；
- IHC 端到端；
- LIS 单结果/多结果；
- 跨瓶预约与消耗；
- DAB；
- 双针；
- 外设状态；
- 暂停、恢复、历史、审计、CSV；
- 修复多玻片跨瓶预约；
- 修复 Wash 操作误把玻片位当洗针点的问题；
- 修复测试资源锁隔离问题。

相关文档：

```text
docs/backend-mock-contract-v1.md
```

### 8.8 操作员 UI 正式数据接入（19）

已完成：

- A–D 通道与 16 位展示；
- 最近事件改为按需弹窗；
- 通道级 HE/IHC 选择；
- LIS 与 IHC 兼容；
- HE 人工确认；
- R1–R40 正式试剂数据；
- M1–M8 正式 DAB 数据；
- 正式运行状态、步骤、命令、温控、制冷、液路、双针、资源锁、报警；
- SignalR 重连拉取完整正式快照；
- 操作员 UI 不依赖旧 `MockRuntimeStore`；
- 原生 `alert/confirm/prompt` 替换为内部对话框；
- “启动前预检”通过后才显示“启动运行”；
- 隐藏 MockDeviceState、state hash 等技术信息。

### 8.9 数据库备份修复（19）

已完成：

- 备份尝试文件转到 `%TEMP%\stainer-backup-attempts\`；
- 成功备份才提升到 `data/backups/`；
- 相关 SQLite 备份连接使用 `Pooling=False`；
- Backup API / VACUUM 失败但 checkpoint-copy 成功时生成：
  - `database_backup_degraded` Warning；
- 不再把这种情况一律标为 `database_backup_failed` Critical；
- 失败方法、原因、尝试路径写入响应、审计、安全日志和运行日志；
- 历史 `database_backup_failed` 通过正式流程 Acknowledged / Resolved；
- 历史尝试产物隔离到 `data/backups/.attempts/...`；
- 已完成一份备份恢复验证：完整性、迁移、表数、基础审计/报警查询均正常。

> 已知一次恢复验证快照为 26 个迁移、73 张表；该数字仅代表当时备份验证结果，未来数据库演进后必须重新以实际结果为准。

### 8.10 工程/管理/历史/报警 UI（20）

已完成：

- 工程页面；
- 管理页面；
- 历史页面；
- 报警页面；
- 用户与角色；
- 默认 HE/IHC 工作流；
- 抗体映射；
- 试剂目录；
- 报警、审计、诊断、通信记录和 CSV；
- 角色信息分层：
  - 操作员仅看友好摘要；
  - 工程/管理权限可看原始技术信息。

操作员端不得显示：

- 数据库路径；
- SQLite 原始错误；
- AttemptDirectory；
- 原始通信；
- 状态哈希；
- 内部错误码；
- 原始诊断快照。

### 8.11 正式单页入口与旧接口收紧（20，后续已收敛）

以下旧接口只允许 Development：

```text
/api/dab
/api/logs
/api/slides/configure
/api/run/add-slide
/api/engineer/command
```

以下旧 API 只允许 Development 或 Testing，不得在 Staging / Production 映射：

```text
/api/state
/api/run/*
```

当前所有环境的唯一正式 Web 页面及登录后入口为：

```text
/control-console
```

旧 `/dashboard`、`/login`、其余旧正式页面以及 `/mock-timeline` 已删除，访问应返回 404；根路径 `/` 重定向到 `/control-console`。

### 8.12 浏览器验收与 Mock 阶段收口（21，历史基线）

历史阶段已完成以下验收；因当前 UI 已收敛到 `/control-console` 且完整后端回归有 61 项失败，最新 HEAD 仍须重新执行浏览器回归：

- Playwright Core 浏览器验收；
- 使用独立临时数据库、锁、日志和备份目录；
- 不污染正式数据；
- 验证正式页面不依赖旧 `/api/state`、`/api/run/*`、`/api/dab`；
- 验证 HE/IHC 操作员流程；
- 验证权限；
- 验证双窗口同步；
- 验证 SignalR 重连；
- 相关文档与验收脚本已建立。

### 8.13 数字孪生 XY 基线（22）

已完成：

- 导入坐标 CSV：
  - `coordinate-baseline/position_coordinates_with_slide_rectangles_numbered.csv`
- 坐标版本：
  - `DigitalTwinXY-v1`
- 导入结果：
  - 总行数：90；
  - ExecutableTarget：70；
  - ReferenceOnly：20；
  - Rejected：0。
- 当前坐标状态：
  - Draft；
  - MockOnly；
  - Unverified。
- P0-05 已形成 `real-hardware/真实运动坐标参数与现场校准方案.md` 草案 v0.1，当前状态为“校准方案草案编制中 / 待评审 / 未完成”；草案未写入真实现场参数，不代表真实坐标已校准，也不放行 P1-08。
- 建立真实动作硬门禁；
- 未配置/验证 Z、安全高度、进出路径、探液/吸液/加液高度、速度、加速度、软限位和现场校准前，真实机械臂动作不得执行。

### 8.14 主控、DCR55、制冷离线协议（23A）

已完成：

- `IceImmunoSerialProtocol.cs`；
- `MainControllerProtocol.cs`；
- `Dcr55Protocol.cs`；
- `StandaloneCoolingProtocol.cs`；
- `OfflineHardwareProtocolTests.cs`；
- `real-hardware/真实设备接口参数清单.md`；
- 不打开端口，不扫描设备，不连接硬件，不调用真实设备动作。

### 8.15 离线 Real Adapter 读取边界（23C）

已完成：

- `RealDeviceReadBoundary.cs`；
- `UnavailableRealDeviceAdapter.cs`；
- `IDeviceAdapter.cs`；
- `OfflineRealDeviceAdapterTests.cs`；
- 相关 DI 接入。

已实现：

- 主控只读命令边界；
- ACK、PUT、分片、拼包、CRC、超时、断连处理；
- DCR55 终止符、单码、多码、无码解析；
- 不发送 DCR55 物理触发；
- 制冷仅温度读取；
- 无 Transport：`NotConfigured`；
- Fake Transport：`Offline` 且 `Ready=false`；
- 所有真实写操作/动作返回 `NotSupported`。

明确禁止的真实写操作包括：

- reset；
- 设备写入；
- 加热；
- PWM；
- 混合；
- IO；
- 排液；
- 有毒废液操作；
- 试剂二维码 start/reset；
- DCR55 物理触发；
- 制冷 set/start/stop；
- 正式工作流物理执行。

> 说明：以上禁止项记录的是 23C 离线 Real Adapter 读取边界。后续已完成的 `ScannerControlService` 仅表示 DCR55 后端控制服务抽象和命令封装完成，不代表真实设备串口控制、真实 ROI 效果、真实校验光效果或硬件联调已完成。

### 8.16 坐标 Z 语义与 LiquidClass 参数补齐

已完成：

- 坐标 Z 语义补齐（`CoordinatePoint` / `CoordinateCalibrationHistory`）：
  - Z-Travel → `SafeZUm`；
  - Z-Start → `LiquidDetectZUm`（别名 `AspirateZUm`）；
  - Z-End → `AspirateEndZUm`（新增，nullable）；
  - Z-Dispense → `DispenseZUm`。
- LiquidClass 参数补齐（`LiquidClassVersion`）：
  - `LiquidFollowingDepthUm`（液面跟随深度）；
  - `RetractSpeedUmPerSecond`（回撤速度）；
  - `ConditioningVolumeUl`（Conditioning Volume）；
  - `BreakoffSpeedUlPerSecond`（Breakoff Speed）；
  - `PostDispenseAirGapUl`（排液后尾随空气间隙）。
- System Trailing Air Gap 复用既有 `TrailingAirGapUl`，未重复创建。

接入范围：

- 配置写入（`SaveLiquidClass` / `CreateCoordinateVersion` / `Calibrate` / 导入预览与差异）；
- 校验、版本差异、审计；
- 冻结快照（`CoordinateSnapshotFactory` / `LiquidClassSnapshotFactory` / `LiquidClassParameterSummary`）与读取 ReadModel。

边界说明：

- `AspirateEndZUm` 为 nullable，未纳入 Real readiness 门禁，旧坐标数据兼容。
- 本次仅补齐配置表达与冻结快照能力；**未接入 Motion 执行逻辑**，未改变执行链路。
- 新增数据库列与迁移（`coordinate_points.aspirate_end_z_um`、`coordinate_calibration_history.aspirate_end_z_um`、`liquid_class_versions` 5 个新列）；未修改历史 Migration。

### 8.17 工程手动移液测试 API

已完成：

- `EngineeringPipettingService` 与 `/api/engineering/pipetting-tests/*` 端点；
- 支持操作：LiquidDetect、Aspirate、Dispense、Wash、Flush；
- `Purge`、`ClearChannel` 明确列为 `UnsupportedOperations`，不凭空模拟领域不存在的操作；
- Flush 复用既有 `WashNeedle` 领域操作记录。

安全与治理：

- engineer/admin 权限；
- EngineeringSession（`RequireWriteSessionAsync`）；
- CommandIdempotency（命令幂等）；
- AuditLog；
- Real 模式 fail-closed（未连接真实设备、未执行真实移液动作）。

边界说明：

- 工程测试与 `MotionControlService` 互不调用，各自独立模拟，共享 Mock 设备状态表；
- 不影响 Workflow 执行、Motion 正式执行、DCR55、SOCON、MainController；
- 代码已完成并提交。

### 8.18 Workflow 配置模型评估

已完成（评估，非代码开发）：

- 经代码评估，现有模型已表达“通道选择单一 Workflow、同通道玻片共享该 Workflow”的关联需求，**无需另建 Workflow 配置/关联模型**。
- 2026-07-17 修订补充了此前未覆盖的规划规则持久化需求：已在既有 `WorkflowVersion` 上增加 `PlanningRulesJson` 并提交对应 Migration；因此本节原有“无需新增任何字段或 Migration”仅是历史判断，已被后续需求扩展修正。

模型对应关系：

- `WorkflowDefinition` = 实验脚本定义；
- `WorkflowVersion` = 实验脚本版本（Draft / Published / Retired，支持按实验类型设默认）；
- `WorkflowStep` = 步骤；
- `WorkflowReagentRequirement` = 试剂需求；
- `PrimaryAntibodyWorkflowMapping` = IHC 一抗→脚本映射。

通道绑定与业务规则：

- 通道绑定：`ChannelBatch.SelectedWorkflowVersionId`（单一、运行开始后锁定、带 `WorkflowAssignmentHistory`）。
- 同一 Channel 内 4 个玻片共享同一 Workflow：`SlideTask` 无独立 Workflow 字段；运行期 `WorkflowExecution` 由通道单一 `SelectedWorkflowVersionId` 实例化（`MachineRunService` 已确认）。
- 不同 Channel 可选择不同 Workflow：每个 `ChannelBatch` 独立 `SelectedWorkflowVersionId`。
- 禁止的反模式（Slide1→ScriptA / Slide2→ScriptB）在数据模型与运行实例化两层均无法发生。

语义说明：

- `StainingTask.WorkflowVersionId`：任务意图 / 兼容校验。
- `ChannelBatch.SelectedWorkflowVersionId`：运行执行权威。

后续：

- Workflow 定义/版本/通道关联基础模型无需重建；
- 配置模板、时间线编辑和规划规则持久化已进入提交基线；Mock Workflow 执行器、运行状态机、运行账本和资源租约也已实现。当前完整后端回归仍有 61 项失败，迁移与浏览器验收需复核；Real 设备执行编排仍是独立且未完成的能力。

---

## 9. 数据库分析与规则

### 9.1 数据库权威范围

正式数据库是以下内容的权威存储：

- 用户、角色、权限；
- 样本、通道批次、玻片位；
- 工作流与工作流快照；
- 试剂目录、试剂库存、预约、消耗；
- DAB 生命周期和 DAB 位置；
- 坐标、坐标快照；
- Liquid Class、液类快照；
- 扫码器配置、扫码区域配置和试剂坐标锚点配置；
- 温控、制冷、液路、机械臂和针状态；
- 运行记录、命令幂等性记录、运行账本；
- 资源锁；
- 报警、审计；
- 工程会话、诊断、设备通信记录；
- 配置导入导出记录。

### 9.2 写入与设备通信规则

1. 所有业务写入必须经正式应用服务。
2. 设备适配器不得直接写数据库。
3. 设备动作前必须先写入 Pending。
4. 设备动作后由调用方写入结构化结果。
5. 数据库锁、网络问题或异常不得造成真实动作重复发送。
6. Pending 默认不自动重试真实动作。
7. 运行过程使用已冻结快照，不能静默回退到“最新配置”。
8. Unknown 状态不得自动视为安全或自动释放资源。
9. 非工程角色不得通过 API 获取原始技术通信和诊断细节。

### 9.3 WAL、备份与恢复

数据库运行规则：

- 使用 WAL；
- 启用 foreign keys；
- 设置 busy timeout；
- `stainer.db-wal` 和 `stainer.db-shm` 不可在运行时单独删除；
- 成功备份才进入正式备份目录；
- 失败尝试文件留在临时目录或隔离目录；
- 恢复前必须验证：
  - SQLite integrity；
  - migrations；
  - 表结构/数量；
  - 基础报警查询；
  - 基础审计查询。

---

## 10. Web API 与 HMI 实现情况

### 10.1 正式 API 覆盖范围

当前正式 API 已覆盖或支撑：

- 样本、通道、玻片位；
- HE/IHC 类型选择；
- LIS Mock 查询；
- 样本人工确认；
- 试剂扫描与 R1–R40 管理；
- DAB 生命周期；
- 运行预检、启动、暂停、恢复、停止；
- 运行状态、步骤、命令生命周期；
- 温控、制冷、液路、双针、资源锁；
- 报警、审计、历史；
- 用户与角色；
- 默认工作流、抗体映射、试剂目录；
- 工程诊断；
- 坐标和 Liquid Class 版本管理；
- 配置导入/导出；
- 设备通信记录；
- 扫码器配置与扫码区域配置管理；
- 试剂坐标锚点与插值生成；
- DCR55 Restart / Calibration Light / ROI 后端控制服务接口（服务抽象，不代表真实硬件验证）；
- CSV 导出。

### 10.2 SignalR 规则

- SignalR 用于状态通知和多窗口同步；
- SignalR 不是业务权威；
- 重连后必须重新从正式服务读取完整快照；
- 客户端不应以断线前内存恢复正式状态；
- 运行、温控、制冷、液路、双针、报警均需支持状态同步。

### 10.3 UI 设计决策

已确认：

- 首页移除复杂工作流卡片和常显状态汇总卡；
- 最近事件改为按需打开；
- 标准登录界面只显示：
  - `OP` 操作员；
  - `AD` 管理员；
- `ENG` 工程师角色不在登录界面展示，但后台权限和工程二次会话可以保留；
- 当前 UI 以数据与流程正确性为优先，不以最终视觉设计为优先；
- 后续完整视觉替换不应影响正式后端权威与运行逻辑。

---

## 扫码器配置能力状态

当前已完成的是“扫码器配置持久化、扫码区域配置表达、试剂坐标配置生成与 DCR55 后端控制服务抽象”，不是“真实扫码器接入能力”或“真实扫码器控制能力”。

### 1. 扫码器配置管理能力

已完成：

- `ScannerProfile` 配置模型；
- `ScannerRegion` 配置模型；
- 扫码器配置数据库持久化；
- 扫码器配置管理 API；
- 新增数据库表：
  - `scanner_profiles`；
  - `scanner_regions`；
- 已支持后端配置管理 API：
  - `GET /api/scanners`；
  - `GET /api/scanners/{id}`；
  - `POST /api/scanners`；
  - `PUT /api/scanners/{id}`；
  - `GET /api/scanner-regions`；
  - `POST /api/scanner-regions`。

支持表达：

- 扫码器基础配置；
- 通信参数配置；
- ROI 配置字段；
- 校验光状态字段；
- 扩展参数。

边界说明：

- 当前为“配置管理能力完成”；
- 不代表数据库配置已经接管 DCR55 运行参数；
- 不代表真实设备参数已经生效。

### 2. 扫码区域配置能力

已完成：

- `ScannerRegion` 配置模型与扩展；
- 区域编号；
- 扫码顺序；
- 坐标档案关联；
- 坐标版本关联。

说明：

- 后端已经具备样本区域扫码配置表达能力；
- 不代表前端页面完成；
- 不代表机械臂自动扫码流程完成。

### 3. 试剂区坐标配置生成能力

已完成：

- `ReagentCoordinateAnchor` 模型；
- `reagent_coordinate_anchors` 数据库表；
- 试剂列首尾坐标配置；
- 坐标插值生成服务。

生成规则：

- 使用首尾坐标作为锚点；
- X / Y / Z 独立线性插值；
- 使用已有 `CoordinateProfileVersion` 版本体系保存。

说明：

- 当前完成的是“坐标配置生成能力”；
- 不代表机械臂实际校准完成。

### 4. DCR55 扫码控制服务能力

已完成：

- `ScannerControlService`；
- Restart 控制接口；
- 校验光控制接口；
- ROI 配置接口。

说明：

- 当前完成的是“DCR55 后端控制服务抽象和命令封装”；
- 不代表 DCR55 已经完成真实接入。

未完成：

- 真实设备串口控制验证；
- 真实 ROI 效果验证；
- 真实校验光效果验证；
- 真实硬件联调；
- 数据库配置接管 DCR55 运行参数；
- 扫码器配置页面；
- 扫码区域配置页面。

### 5. 扫码设备职责与坐标语义（当前确认）

- DCR55 安装在机械臂末端，负责样本 / 玻片二维码扫码；
- `ArmCamera` 若实际指该硬件，应定义为机械臂安装的样本二维码扫码头，后续建议统一命名为 `DCR55Scanner` 或 `SampleScanner`；
- `ArmCamera` / `DCR55Scanner` 不应作为运动目标点，它是工具设备或工具参考；
- 必须标定 DCR55 扫码中心相对于机械臂坐标基准或 Needle1 基准的 X / Y / Z 工具偏移；
- 样本扫码目标应由 Slot 扫码坐标或 `ScannerRegion` 表达：机械臂携带 DCR55 到位、确认运动完成并保持静止后，再触发扫码；
- `SampleScan` 不应默认表示每张玻片都必须经过的固定扫码工位；如现场存在统一校准 / 测试 / 参考点，建议命名为 `SampleScannerCalibrationPoint`；
- 试剂扫码由主控板固定多通道扫码模块 `0x08` 负责，不随机械臂运动，不使用 DCR55，也不使用 `SampleScan`。

---

## 11. 物理坐标、空间与双针模型

### 11.1 数字孪生坐标权威

当前坐标权威来源：

```text
coordinate-baseline/position_coordinates_with_slide_rectangles_numbered.csv
```

如与旧版需求文件中的坐标描述冲突，以该 CSV / 数字孪生基线为准。

### 11.2 坐标轴与针偏移

```text
Needle1 / Z1 = (0, 0)
Needle2 / Z2 = (0, +25000 µm)
```

含义：

- Needle2 相对 Needle1 的 Y 轴偏移为 +25 mm；
- 数字孪生 X = 实际机械 X；
- 数字孪生 Y = 实际机械 Y；
- 禁止额外镜像、旋转、坐标变换或人为偏移。

### 11.3 试剂架映射

当前软件业务编号仍以 `R1–R40` 管理，既有数字孪生 / 坐标映射为：

```text
S11–S18 → R1–R8
S21–S28 → R9–R16
S31–S38 → R17–R24
S41–S48 → R25–R32
S51–S58 → R33–R40
```

现场试剂扫码的列式位置标记当前倾向为：

```text
第 1 列：R11–R18
第 2 列：R21–R28
第 3 列：R31–R38
第 4 列：R41–R48
第 5 列：R51–R58
```

以上列式标记不得直接替代既有 `R1–R40` 业务编号；后续必须建立主控 `0x08` 扫码通道、列式位置标记和 `R1–R40` 业务编号之间的明确映射。

### 11.4 玻片位映射

```text
R11 → A-04
R12 → A-03
R13 → A-02
R14 → A-01

R21 → B-04
R22 → B-03
R23 → B-02
R24 → B-01

R31 → C-04
R32 → C-03
R33 → C-02
R34 → C-01

R41 → D-04
R42 → D-03
R43 → D-02
R44 → D-01
```

### 11.5 洗针与 DAB 映射

洗针：

- 左侧外针；
- 右侧内针；
- 上组对应 Z1；
- 下组对应 Z2。

DAB：

```text
P11 = M1
P12 = M2
P21 = M3
P22 = M4
P31 = M5
P32 = M6
P41 = M7
P42 = M8
```

DAB A 液在 B 液上方。

注意：

- `Mixer1–Mixer4` 是混合模块命名；
- `M1–M8` 是 DAB 配液位置；
- 两者不得混淆。

### 11.6 当前坐标限制

当前仅完成 XY 基线。

真实动作前仍必须由**工程受控配置**补齐并验证：

- Z 高度；
- 安全高度；
- 接近高度；
- 离开高度；
- 液面检测高度；
- 吸液高度；
- 加液高度；
- 速度；
- 加速度；
- 软限位；
- 碰撞/路径规则；
- 现场校准结果；
- 真实验收状态。

普通管理员不能以常规业务配置绕过工程坐标门禁。

### 11.7 样本扫码坐标语义

当前样本扫码的推荐语义为：

```text
机械臂携带 DCR55
→ 移动到对应 Slot 的扫码位置
→ 机械臂静止
→ 触发 DCR55
→ 读取样本码
→ 绑定 Channel + Slot + Sample
```

需要继续补齐：

- 每个 Slot 是否有独立扫码坐标或 `ScannerRegion`；
- DCR55 扫码中心相对机械臂坐标基准 / Needle1 基准的工具偏移；
- `SampleScan` 是否移除为必需点，或仅作为 `SampleScannerCalibrationPoint` 校准 / 测试 / 参考点保留。

---

## 12. DAB、试剂与液路规则

### 12.1 DAB 配方与位置

配方：

```text
A : B : Water = 1 : 1 : 18
```

用量规则：

```text
200 µL / slide + 400 µL allowance
```

位置：

```text
M1–M8
```

### 12.2 DAB 生命周期规则

1. 配制完成时开始计算有效期。
2. 有效期为 3 小时。
3. 每次配液使用新试管。
4. 到期、异常中断或未知状态不得继续使用。
5. 未完成 DAB 操作到期时生成重配计划。
6. 无空位时提示清洗。
7. M1–M8 全部使用后提示清洗。
8. 清洗状态和重配状态必须进入审计与运行追溯。
9. 跨瓶预约、跨瓶消耗和 SystemWater 来源必须进入正式账本。
10. 不允许因状态不明自动释放 DAB 位置或库存。

### 12.3 试剂码与试剂门禁

试剂架：

```text
R1–R40
```

扫码职责：

- 试剂码由主控板固定多通道扫码模块 `0x08` 读取；
- DCR55 / `SampleScanner` 仅负责样本 / 玻片码，不参与试剂扫码；
- 当前倾向 `ch1–ch5` 对应 5 列试剂架，但扫码通道、列式位置标记和既有 `R1–R40` 业务编号的映射仍需现场确认。

试剂码包含：

- 唯一 ID；
- 数量；
- 3 位 0.1 mL 单位；
- `999 = 99.9 mL`。

状态包括：

```text
VALID
EMPTY
INVALID
```

门禁规则：

- 必需试剂缺失、无效、余量不足、无法解析或无法查表时阻止启动；
- VALID 但非本次必需的试剂可保留提示；
- 多瓶可按顺序使用；
- 余量到 0 必须产生明确处理提示；
- 真实扫码器的 EMPTY / INVALID 实际输出格式尚未现场确认，当前不能假设其行为。

### 12.4 双针与洗针规则

- 每根针一次仅能容纳一种试剂；
- 单针在一次吸液操作中不得混吸不同试剂；
- 换试剂前必须排空/洗针；
- 双针共享 XY 平台资源，任何并行计划都必须同时满足共享平台和机械几何约束；
- 两针可容纳不同试剂；
- 仅在几何、靶点、平台资源、资源锁与液体隔离全部满足时可并行；
- 当前策略保守串行化优先；
- 未引入 OR-Tools 作为当前正式运行依赖；
- OR-Tools 仅可作为未来优化方向，不应阻塞安全和真实设备接入。

---

## 13. 真实设备接入情况

### 13.1 设备接入总览

| 设备/模块 | Mock | 离线协议 | 离线只读边界 | 真实连接 | 真实动作 |
|---|---:|---:|---:|---:|---:|
| 温控 | 已完成 | 部分基础 | 未完成 | 未完成 | 未完成 |
| 制冷 | 已完成 | 已完成 | 已完成 | 未完成 | 未完成 |
| 液路 | 已完成 | 部分基础 | 未完成 | 未完成 | 未完成 |
| 主控串口 | 已完成模拟 | 已完成 | 已完成 | 未完成 | 未完成 |
| 主控二维码 | 已完成模拟 | 已完成 | 已完成 | 未完成 | 未完成 |
| DCR55 | 已完成模拟 | 已完成 | 已完成 | 未完成 | 未完成 |
| SOCON 机械臂 | 已完成模拟 | SDK 取证完成 | 受门禁只读软件边界已完成，待现场验证 | 未完成 | 未完成 |
| USB2CAN | 未完成 | SDK/Bridge 连接配置与门禁已实现 | 受控会话软件边界已完成，待现场验证 | 未完成 | 未完成 |
| 安全 IO | 部分 Mock/资料 | 部分资料 | 未完成 | 未完成 | 未完成 |
| LIS | Mock 已完成 | 实际接口未知 | 未完成 | 未完成 | 未完成 |

> Main Controller 的真实通讯软件基础设施已完成；表中的“真实连接”和“真实动作”仍为“未完成”，表示尚未打开真实 COM、连接真实主控、验证真实状态或开放真实控制命令。

> DCR55 的真实 Serial Transport 代码和后端控制服务抽象已完成；表中的“真实连接”和“真实动作”仍为“未完成”，表示尚未连接实际设备、确认现场 COM 端口、执行真实单次扫码验证或验证 Restart / Calibration Light / ROI 的真实效果。

> SOCON / USB2CAN 的“离线只读边界”仅表示 Bridge 已具备双门禁、白名单、会话生命周期和只读状态/位置接口；“真实连接”仍为“未完成”，表示尚未打开真实 USB2CAN、连接真实 NodeID 或读取现场状态。

### 13.2 主控串口

当前状态：

| 能力 | 状态 |
|---|---|
| 协议 | 已完成 |
| 真实通讯软件基础设施 | 已完成 |
| 真实硬件连接 | 未开始 |
| 真实状态验证 | 未开始 |
| 真实控制 | 未开放 |

当前已具备真实设备接入前的软件基础；仍需完成真实主控连接验证、真实状态读取验证，并在后续安全门禁满足后另行开放控制能力。

已确认参数：

| 项 | 值 |
|---|---|
| 连接 | USB 串口 / RS232 |
| 波特率 | 115200 |
| 数据位 | 8 |
| 校验 | None |
| 停止位 | 1 |
| 流控 | 无 |
| 协议版本 | V1.0.4 |
| 字节序 | Little Endian |

帧格式：

```text
A5 01 [length:uint16 LE] [body] [CRC16-MODBUS(body only):LE] 5A
```

已知功能范围：

- 系统复位；
- 工作状态；
- 节点状态；
- 运行时间；
- LED；
- 制冷；
- 加热；
- PWM；
- 混合；
- 光电；
- 二维码；
- 蜂鸣器。

当前禁止：

- 自动复位；
- 自动打开真实串口；
- 自动扫描 COM；
- 自动向真实设备发送控制命令。

### 13.3 主控温控、PWM、混合与光电

#### 温控

主控温控 Parent：`0x04`

已知能力：

- 批量当前温度 `.09`；
- 批量目标温度 `.0A`；
- 批量开关 `.0B`；
- 单点读写和开关；
- 板号 0–3。

待确认：

- 真实 Transport 的现场连接与状态读取行为；
- 实际温控板读写；
- 稳定判定；
- 加热超时；
- 过温；
- 真实报警；
- 断电恢复策略。

#### PWM

主控 PWM Parent：`0x07`

- 泵编号 0–3；
- 范围：`-100 ~ +100`；
- 正负方向由设备定义；
- 当前未接入真实读写。

#### 混合

主控混合 Parent：`0x0A`

- 板号 0–3；
- 已知：enable、home、remaining、target cycles、reset、config；
- 当前未接入真实读写、校准和循环参数验证。

#### 光电 / 安全相关状态

主控光电 Parent：`0x05`

已确认 PUT `.04` 的 ID：

| ID | 含义 |
|---:|---|
| 0 | 水空 |
| 1 | 废液满 |
| 2 | 有毒废液满 |
| 3 | PBS 空 |

状态：

```text
0 = 正常
1 = 异常
```

限制：

- 当前只确认 PUT 形式；
- 未确认主动 GET 查询；
- 急停、门、抽屉、真实限位、真实碰撞、真实安全链路尚未确认；
- 不得基于猜测接入安全逻辑。

### 13.4 主控二维码扫描

主控二维码 Parent：`0x08`

已知操作：

- start `.04`；
- reset `.05`；
- status `.06`；
- get text `.01`；
- put text `.03`。

已知状态：

```text
0 = idle
1 = scanning
```

已知格式：

- 支持 `ch1–ch16`；
- CRLF 分隔；
- 位置标签 `#1#` 到 `#9#`；
- 每个码最大约 24 字符；
- 缓冲约 1024 ASCII。

待现场确认：

- `ch1–ch5` 与物理扫描位置映射；
- `ch1–ch5`、列式位置标记与 `R1–R40` 业务编号的统一映射；
- EMPTY / INVALID 的真实输出；
- 超时与失败行为；
- 实际触发时序；
- 多码行为与重复扫描行为。

### 13.5 DCR55

设备：

```text
DCR55M2/R2
P/N 50136772
```

典型连接：

```text
USB 转 RS232，115200 8N1，无流控
```

已知命令：

```text
单次扫描：RDCMXEV1,P11,P20
停止扫描：RDCMXEV1,P10
连续扫描：RDCMXEV1,P11,P21
软重启：RDCMXRB1
校验光开：RDCMXEV2,P11
校验光关：RDCMXEV2,P10
ROI 宽度：CDOPSRW{width}
ROI 高度：CDOPSRH{height}
ROI 左边界：CDOPSRL{left}
ROI 上边界：CDOPSRT{top}
```

已知响应：

```text
条码文本 + CRLF
```

安装与职责：

- DCR55 安装在机械臂末端，负责样本 / 玻片二维码；
- 机械臂移动到对应 Slot / `ScannerRegion` 的扫码目标位，确认运动完成并保持静止后触发 DCR55；
- `ArmCamera` 若实际指该硬件，应作为随动扫码工具参考理解，后续建议统一命名为 `DCR55Scanner` 或 `SampleScanner`；
- DCR55 不参与试剂瓶扫码，试剂瓶扫码由主控板固定多通道扫码模块 `0x08` 负责。

待确认：

- 命令结束符；
- 无码返回；
- 超时；
- 多码格式；
- 成功确认；
- 物理触发时序；
- DCR55 扫码中心相对机械臂坐标基准 / Needle1 基准的工具偏移；
- 各 Slot / `ScannerRegion` 的样本扫码目标位和安全姿态；
- `SampleScan` 是否仅作为 `SampleScannerCalibrationPoint` 校准 / 测试 / 参考点保留。

当前实现边界：

- 支持协议解析；
- 支持单码、多码、无码；
- 已建立 `ISerialPort` 抽象、`System.IO.Ports.SerialPort` Infrastructure 封装和 DCR55 Serial Transport；
- `SerialPort` 仅存在于 Infrastructure Transport 层，未改变 Mock / Real 隔离；
- 支持 COM 参数配置（115200 / 8 / None / 1）、受控单次触发命令 `RDCMXEV1,P11,P20\r`、CRLF 读取、超时、断开和打开失败处理；
- Protocol、Real Adapter 与 Serial Transport 均已完成离线测试；
- 已完成 `ScannerControlService` 后端控制服务抽象，封装 Restart、Calibration Light 和 ROI 命令；
- 尚未连接实际 DCR55、确认现场 COM 端口或执行真实单次扫码验证；
- 尚未完成真实串口控制验证、真实 ROI 效果验证、真实校验光效果验证或真实硬件联调；
- 尚未将扫码结果接入 Sample / LIS 业务流程，不能视为 DCR55 真实设备接入完成或可用于生产。

### 13.6 制冷

#### 独立制冷设备

| 项 | 值 |
|---|---|
| 连接 | USB → RS485 |
| 波特率 | 9600 |
| 协议版本 | V1.0 |

读取温度：

```text
FF 00 8A 75
```

响应：

```text
FF 00 XX YY
```

设置温度：

```text
FF 00 XX YY
```

其中：

```text
XX = 1–10
YY = FF - XX
```

启动：

```text
FF 00 81 7E
```

停止：

```text
FF 00 82 7D
```

温度为 0–128°C 的整数表示。

待确认：

- ACK；
- 稳定判据；
- 报警；
- 超时；
- 真实接线；
- 最终由主控 `0x03` 还是独立 RS485 控制。

当前离线 Real 边界仅允许温度读取，不允许设置、启动或停止。

### 13.7 LIS

当前已完成：

- LIS Mock；
- 单结果和多结果业务验证。

实际 LIS 尚未接入。

实际接入前必须获得：

- 数据库/服务类型；
- 地址和网络权限；
- 认证方式；
- 表结构、字段定义和查询条件；
- 样本号规则；
- 多结果规则；
- 字符编码；
- 超时与错误处理；
- 测试数据和脱敏样本；
- 现场联调安排。

---

## 14. SOCON SDK 接入情况

### 14.1 已完成的兼容性取证

已完成：

- SDK 文件元数据检查；
- `.NET Framework 4.5.2 x86` 独立探针编译与隔离加载；
- `.NET 9 Windows x86` 独立探针编译与隔离加载；
- 关键类型和方法签名核对；
- 静态构造器与 P/Invoke 清单核对；
- 缺失运行期依赖检查；
- 不连接硬件、不调用 SDK 方法；
- 临时探针目录清理。

正式取证报告的目标仓库路径为：

```text
real-hardware/socon-sdk-compatibility-report.md
```

维护要求：

- 使用最终修订版的表述；
- 不提交 SDK、Demo、驱动、授权文件或本机绝对路径；
- 该报告的“内容完成”与“已写回目标路径并已提交 Git”是两件事，后者必须以实际仓库状态验证。

### 14.2 取证结论

SDK 文件：

| 文件 | 类型 |
|---|---|
| `SOCON.API.dll` | MSIL / AnyCPU 托管程序集 |
| `SOCON.Utility.dll` | MSIL / AnyCPU 托管程序集 |
| `can_bootloader.dll` | 原生 x86 二进制 |

重要结论：

1. 两个托管程序集可由独立 x86 探针隔离加载。
2. `.NET 9 Windows x86` 探针的成功仅证明该探针可编译并通过 `Assembly.LoadFrom` 隔离加载程序集。
3. 该结果**不代表**现有 Stainer 主项目可以、应该或已经直接以编译期引用方式接入 SOCON SDK。
4. `can_bootloader.dll` 为 x86 原生。
5. 若执行依赖 `can_bootloader.dll` 的 USB2CAN/CAN 通讯路径，承载该路径的进程必须为 x86。
6. SDK 中存在 17 个 P/Invoke 声明。
7. SDK 中存在 8 个带 `.cctor` 的类型。
8. `Assembly.LoadFrom` 与 `GetTypes()` 未证明实例化、静态成员访问或真实动作安全。
9. `SOCON.ScEventBus.dll` 与 `C1.C1Zip.4.dll` 未随 SDK 包提供。
10. 缺失依赖不影响本轮元数据加载；未来若触发相关运行路径，可能产生 `FileNotFoundException`。
11. “SDK 可加载”不等于“真实机械臂可控制”。

### 14.3 已知 SDK 类型与错误风格

重要类型：

```text
SOCON.API.SCDevice
SOCON.API.SCDeviceMA
SOCON.API.TPDevice
SOCON.API.SocaXY
SOCON.API.CAN_Bootloader
```

SDK 不存在统一 `ErrorCode` 枚举。

返回值语义：

| 类型 | 常见返回 |
|---|---|
| 动作（Move/Init/Aspirate/Dispense/LiqDet/WaitActionDone） | `string`，空字符串或 null 通常表示成功 |
| 连接（Connect/OpenPort/ClosePort） | `bool` |
| 状态（CheckIsInited/CheckHome） | `bool` |
| 位置（GetXPos/GetYPos/GetZ） | `float` |
| ACK/部分配置 | `int` |

后续适配层必须按实际返回类型统一包装，不能假设有统一枚举错误码。

### 14.4 最终架构决策

**已确认采用：独立 `.NET Framework 4.5.2 x86 SOCON Bridge`。**

不采用：

```text
直接将 SOCON SDK 嵌入现有 .NET 9 主进程
```

原因：

- x86 原生 `can_bootloader.dll`；
- 17 个 P/Invoke；
- 静态初始化副作用未验证；
- 存在缺失运行期依赖；
- 厂商 Demo 为 .NET Framework 4.5.2；
- 不应为了 SOCON 将主项目改为 x86；
- 独立进程可隔离 SDK、原生 DLL、厂商依赖和异常风险。

### 14.5 当前未完成事项

- Bridge 正式 net452 x86 构建验证（P0-02 已完成 @ 2026-07-08）；
- SDK 真实部署验证（P1-01 已完成 / ready-to-close——已配置 SDK 目录后完成运行时验证：核心文件齐、`can_bootloader.dll` PE=x86(0x014C)、SOCON.API/Utility 托管元数据加载 PASS、5 类型 available、self-test 60 checks PASS；optional warning 缺失但不阻塞；未连接硬件/未实例化 SDK/未调用真实动作）；
- SDK 实例化；
- USB2CAN 驱动和连接；
- CAN 通讯；
- NodeID 注册；
- 设备类型注册；
- `SetPerMM` 现场参数；
- 回零；
- 位置读取；
- 运动；
- 液面检测；
- 吸液；
- 加液；
- 动作完成等待；
- 安全互锁；
- 真实错误行为；
- 现场校准。

---

## 15. SOCON 独立 x86 Bridge：23D 基线与当前只读扩展

### 15.1 阶段目的

23D 已完成并已提交。该历史阶段只建立离线 Bridge 骨架、IPC、部署前文件/PE 检查和自检。此后 P1-01 已增加托管元数据反射验证，`97d8539` 又增加受门禁真实只读会话软件实现；真实 USB2CAN、节点和位置读取仍未现场验证，动作控制仍未开放。

已建立：

1. 独立 `.NET Framework 4.5.2 x86` Bridge 进程；
2. 本地 Named Pipe IPC；
3. SDK 文件部署前检查；
4. 可离线运行的自检；
5. 对未来真实 SOCON 接入的进程隔离边界。

### 15.2 已提交项目结构

已新增并提交：

```text
bridges/Stainer.SoconBridge/
```

主要文件：

```text
Stainer.SoconBridge.csproj
Program.cs
BridgeHost.cs
BridgeRequestProcessor.cs
BridgeContracts.cs
BridgeStatus.cs
SdkDeploymentValidator.cs
PeArchitectureInspector.cs
ISoconReadOnlyAdapter.cs
ReflectionBasedSoconReadOnlyAdapter.cs
RealReadOnlySessionGate.cs
SoconReadOnlyConfig.cs
README.md
SoconBridgeProtocol.md
SoconBridge.config.example.json
```

本机未跟踪配置：

```text
SoconBridge.config.local.json
```

根目录 `.gitignore` 应忽略：

```text
/bridges/Stainer.SoconBridge/SoconBridge.config.local.json
/bridges/Stainer.SoconBridge/bin/
/bridges/Stainer.SoconBridge/obj/
```

### 15.3 技术边界

Bridge 当前边界：

- 为 Console Application；
- 使用 .NET Framework 4.5.2；
- 项目文件固定 `<PlatformTarget>x86</PlatformTarget>`；
- 不加入 `Stainer.sln`；
- 不使用 NuGet 或第三方 JSON 库；
- 使用 .NET Framework 自带 JSON 能力；
- 不引用、不复制、不提交厂商 SDK、Demo、驱动、授权文件；
- 允许在部署验证中使用 `ReflectionOnlyLoadFrom` 检查托管元数据；仅在双启用、本机配置、SDK依赖和白名单门禁全部通过时构造只读适配器并调用受限连接/状态读取路径；
- 已创建 Bridge 内部 `ISoconReadOnlyAdapter` / `ReflectionBasedSoconReadOnlyAdapter`，但未接入主项目动作执行链路；
- 不提供初始化、回零、运动、等待动作、探液、吸液、加液或泵控制命令；
- 不自动扫描 USB、COM、CAN、网络或 NodeID，不从 IPC 接收连接参数；
- 不修改现有 .NET 9 主项目、数据库、Migration、页面、API、运行流程、坐标模型或动作门禁；
- 不自动提交 Git。

### 15.4 本机配置

配置解析优先级：

1. `SoconBridge.config.local.json` 中的 `sdkDirectory`；
2. 环境变量 `STAINER_SOCON_SDK_DIR`；
3. 无配置则视为 SDK 路径未配置。

示例配置默认关闭真实只读，并仅提供空值/无效占位：

```json
{
  "sdkDirectory": "",
  "diagnosticsEnabled": true,
  "realReadOnlyEnabled": false,
  "usb2can": {
    "connectionType": "CONN_USB",
    "portNumber": 0,
    "baudRate": 0
  },
  "whitelistNodes": [],
  "axisMappings": {
    "x": null,
    "y": null,
    "z1": null,
    "z2": null
  }
}
```

真实只读会话还要求 Bridge 以 `--enable-real-read-only` 显式启动；本机配置中的 `realReadOnlyEnabled` 也必须为 true。任一门禁不满足时不得构造适配器或打开端口。本机真实端口、NodeID和轴映射不得提交 Git、输出到IPC响应或写入脱敏报告。

不得在 Git、README、日志、标准输出或 IPC 响应中写入：

- SDK 绝对路径；
- 真实 COM；
- 真实 IP；
- NodeID；
- 设备序列号；
- 授权信息；
- 泵映射；
- 坐标。

### 15.5 IPC 协议

固定 Pipe 名称：

```text
Stainer.SoconBridge
```

要求：

- Windows Named Pipe；
- 仅本机，不创建 TCP / HTTP / WebSocket；
- 单实例服务端；
- 每次连接处理一个请求、返回一个响应后关闭；
- `PipeDirection.InOut`；
- 4 字节小端长度前缀 + UTF-8 JSON；
- 最大请求体 64 KB；
- 长度为 0、负数、超长、UTF-8 非法或 JSON 非法时拒绝；
- `PipeSecurity` 使用受保护 DACL；
- 仅授予：
  - 当前 Windows 用户 SID；
  - LocalSystem SID；
- 不允许 Everyone、Users、Authenticated Users 等宽泛主体；
- 未来若 Bridge 作为 Windows 服务运行，必须通过部署配置显式增加服务账户 SID，不得默认放宽 ACL。

当前允许命令：

```text
Ping
GetBridgeStatus
ValidateSdkDeployment
OpenConfiguredReadOnlySession
GetConfiguredNodeBasicStatus
GetConfiguredAxisPositions
CloseConfiguredReadOnlySession
```

未知命令以及任何动作类命令一律返回：

```text
success = false
message = NotSupported
```

只读会话命令不接受IPC传入的端口、NodeID或连接参数，只使用受控本机配置。仍不得为 `Move*`、`Init*`、`Home*`、`LiqDet`、`Aspirate`、`Dispense`、`WaitActionDone` 等动作建立可调用IPC命令。

### 15.6 Bridge 状态模型

最少状态：

```text
Starting
Offline
SdkPathMissing
SdkFilesMissing
ArchitectureInvalid
DeploymentValidated
SdkVersionInconsistent
RealReadOnlyNotEnabled
SessionClosed
SessionOpen
SessionBlocked
```

运行期依赖缺失使用 warnings 表示：

```text
SdkRuntimeDependenciesWarning
```

`DeploymentValidated` 仅表示：

1. Bridge 当前为 x86；
2. SDK 路径存在；
3. 核心文件存在；
4. `can_bootloader.dll` 的 PE Machine 为 x86 原生。

它不表示：

- 设备已连接；
- SDK 已加载；
- NodeID 已配置；
- 安全 IO 已确认；
- 坐标已校准；
- 允许真实动作。

`SessionOpen` 只表示受门禁只读会话已打开；不代表设备已初始化、已回零、坐标已校准或允许动作。`CloseConfiguredReadOnlySession` 只有在 SDK `ClosePort()` 明确成功后才进入 `SessionClosed` 并清除缓存；关闭失败进入 `SessionBlocked`，不得伪报已关闭。

### 15.7 ValidateSdkDeployment 的边界

仅允许：

- 文件系统检查；
- 原始 PE 头检查；
- 当前进程架构检查；
- 本机配置读取。

严禁：

- Assembly.Load；
- Assembly.LoadFrom；
- 反射；
- 静态成员访问；
- SDK 实例化；
- SDK 方法调用；
- USB/COM/CAN/网络扫描；
- 真实设备连接。

检查项：

1. Bridge 是否 x86；
2. SDK 路径是否存在；
3. `SOCON.API.dll` 是否存在；
4. `SOCON.Utility.dll` 是否存在；
5. `can_bootloader.dll` 是否存在；
6. `can_bootloader.dll` 是否为有效 x86 原生 PE；
7. `SOCON.ScEventBus.dll` 是否存在；
8. `C1.C1Zip.4.dll` 是否存在。

结果规则：

| 情形 | 状态 |
|---|---|
| 路径未配置或不存在 | `SdkPathMissing` |
| 核心 DLL 缺失或 `can_bootloader.dll` 非 x86/无效 PE | `SdkFilesMissing` |
| 当前 Bridge 非 x86 | `ArchitectureInvalid` |
| 核心条件通过 | `DeploymentValidated` |
| 两个运行期依赖任一缺失 | 维持 `DeploymentValidated`，增加 warning |

details 只能返回：

- 布尔检查结果；
- 缺失文件名；
- PE Machine 值；
- 警告代码。

不得返回 SDK 根目录或绝对路径。

### 15.8 PE 检查

`PeArchitectureInspector` 必须：

- 不加载程序集；
- 只读取必要字节；
- 校验 `MZ`；
- 通过 `e_lfanew` 定位 PE；
- 校验 `PE\0\0`；
- 读取 COFF Machine；
- `0x014C` 视为 x86；
- 损坏、格式错误或 Machine 不符均视为无效核心文件。

### 15.9 自检

Bridge 应提供：

```text
Stainer.SoconBridge.exe --self-test
```

自检与运行时部署验证按两层区分，均不得连接硬件、打开端口、实例化 SDK 或调用动作：

- **P0-02 离线自检**（`--self-test` 默认路径）：只能使用临时目录、假文件、假配置和可注入架构检测器，不加载真实 SDK、Demo、USB2CAN、串口、真实设备或正式数据库。
- **P1-01 运行时部署验证**：允许读取本机受控 SDK 文件，仅执行 `Assembly.ReflectionOnlyLoadFrom()` 元数据验证（仅反射托管 DLL 元数据与类型 available 检查），不执行 SDK 类型实例化、静态成员访问或方法调用。

自检覆盖：

1. Ping 成功，默认状态 Offline；
2. GetBridgeStatus 不自动触发 SDK 检查；
3. 未知命令返回 NotSupported；
4. 未配置 SDK 路径 → SdkPathMissing；
5. 核心 DLL 缺失 → SdkFilesMissing；
6. 伪造错误/非 x86 `can_bootloader.dll` → SdkFilesMissing；
7. 注入模拟非 x86 → ArchitectureInvalid；
8. 完整模拟核心文件 + x86 PE → DeploymentValidated；
9. 缺少运行期依赖仅产生 warning；
10. 所有动作类命令被拒绝且无副作用；
11. Named Pipe 超长、空长度、非法 UTF-8、非法 JSON 被拒绝；
12. 自检结束清理临时目录。

历史 P0-02 验证结论：2026-07-08 正式 net452 x86 构建（VS2022 MSBuild Release|x86，输出 `bin\x86\Release\Stainer.SoconBridge.exe`）`--self-test` **59 checks passed**。当前 HEAD `97d8539` 已于 2026-07-20 重新正式构建（0 warning / 0 error），离线 `--self-test` **93 checks passed**；本轮未配置 SDK、未连接硬件。

> 说明：P1-01 SDK 运行时部署验证**已完成 / ready-to-close**——已配置 SDK 目录后完成运行时验证：核心文件齐（SOCON.API/SOCON.Utility/can_bootloader）、`can_bootloader.dll` PE=x86(0x014C)、SOCON.API/Utility 托管元数据 `ReflectionOnlyLoadFrom` 加载 PASS、5 类型 available（SCDevice/SCDeviceMA/Utility+e_ConnectType/Utility+DeviceTypeEnum/Utility+ProtocolTypeEnum）、`--self-test` 60 checks PASS；optional warning（SOCON.ScEventBus.dll、C1.C1Zip.4.dll）缺失但不阻塞；**未连接真实硬件、未打开 COM/CAN/TCP、未实例化 SDK 类型、未调用真实动作**。`ReflectionOnlyLoadFrom()` 仅做元数据反射验证，P1-01 通过**不代表**真实机械臂已连接或可运动。

### 15.10 构建验证（P0-02 已完成）

Bridge 正式 `.csproj + MSBuild + net452 Targeting Pack` 构建验证**已完成**（2026-07-08）：

- VS2022 MSBuild `Release|x86` 构建成功；
- 输出 `bin\x86\Release\Stainer.SoconBridge.exe`；
- 运行 `--self-test` 通过 **59 checks**；
- 本次验证未加载 SOCON SDK、未连接真实硬件、未扫描 USB/COM/CAN/网络设备；
- 本次验证基于 HEAD（`e7c127df`）的 Bridge 代码，不含工作区后续 4 个 Bridge 源码改动（`ReflectionOnlyManagedAssemblyLoadProbe` 相关诊断能力属于 P1-01）。

构建命令参考：

```cmd
MSBuild bridges\Stainer.SoconBridge\Stainer.SoconBridge.csproj /p:Configuration=Release /p:Platform=x86
```

随后运行实际输出路径中的：

```text
Stainer.SoconBridge.exe --self-test
```

并确认主项目未受影响：

```cmd
dotnet build Stainer.sln --configuration Release --no-restore
dotnet test Stainer.sln --configuration Release --no-build
git diff --check
git status --short
```

---

## 16. 真实设备后续路线

### 16.1 总体顺序

必须按以下顺序推进：

```text
1. SOCON Bridge 离线骨架与部署检查
2. Bridge 进程/IPC 验证
3. 真实设备只读连接
4. 真实安全 IO 接入
5. 真实坐标与 Z 参数校准
6. 回零与单轴低速空跑
7. 双针无液空跑
8. 洗针、试剂架、玻片位、DAB 位置空跑
9. 液路替代液验证
10. HE 无试剂干跑
11. HE 替代液验证
12. HE 正式湿实验
13. IHC 替代液验证
14. IHC 正式湿实验
15. 性能与稳定性验证
```

### 16.2 真实设备只读连接的前置条件

开始真实只读前，至少需要：

- USB2CAN 硬件和驱动；
- SDK 本机部署完整性；
- 实际 COM / 通讯方式；
- NodeID 表；
- 设备类型映射；
- SOCA/SOPA/泵映射；
- 真实安全 IO 清单；
- 现场负责人确认；
- 设备处于安全、可检查状态；
- 无生物样本、无危险试剂，或具备明确安全空跑条件。

只读阶段允许：

- 连接；
- 读取通讯状态；
- 读取设备状态；
- 读取初始化状态；
- 读取回零状态；
- 读取位置；
- 读取错误；
- 读取安全 IO；
- 读取温度/制冷状态。

只读阶段禁止：

- 回零；
- 运动；
- 液面检测；
- 吸液；
- 加液；
- 泵动作；
- 温控写入；
- 制冷写入；
- 任何机械动作。

### 16.3 安全 IO 阶段必须完成的内容

真实动作前必须确认并接入：

- 急停；
- 门禁；
- 抽屉状态；
- 限位；
- 碰撞；
- 水空；
- PBS 空；
- 废液满；
- 有毒废液满；
- 通讯断连；
- 机械臂错误；
- CAN 断连；
- 温度异常；
- 制冷异常。

每项必须明确：

- 真实来源；
- 状态极性；
- 正常/异常含义；
- 查询/推送方式；
- 刷新周期；
- 断线默认策略；
- 报警等级；
- 与运行状态机的联动；
- 操作员/工程师权限差异。

### 16.4 坐标与校准阶段必须完成的内容

- XY 现场确认；
- Z；
- 安全高度；
- 接近/离开路径；
- 探液、吸液、加液高度；
- 速度、加速度；
- 软限位与硬限位；
- 回零方向；
- 双针距离；
- 洗针位置；
- DAB 位置；
- 试剂架位置；
- 玻片位位置；
- 无碰撞轨迹；
- 校准记录、审批与版本冻结。

### 16.5 性能目标

需求中存在以下性能目标：

```text
1 张玻片 ≤ 10 分钟
16 张玻片 ≤ 15 分钟
```

当前未进行真实设备性能验证。

Mock 时序不构成真实性能结论。

---

## 17. 已确认的重要设计决策

1. **正式业务权威**  
   业务权威固定为 ASP.NET Core Application Services + EF Core + SQLite。

2. **Mock / Real 严格分离**  
   Mock 用于模拟与测试；Real 未验证时必须 fail-closed，不能静默回退 Mock。

3. **通道级实验与工作流绑定**  
   操作员只选 HE/IHC；后端绑定默认发布工作流快照；通道内玻片共享同一工作流。

4. **DAB 严格生命周期**  
   M1–M8、三小时有效期、重配、清洗、未知状态保护、账本追溯均为正式规则。

5. **运行快照冻结**  
   工作流、坐标、Liquid Class 等运行关键配置必须冻结；历史运行不得静默使用最新配置。

6. **设备通信持久化**  
   先 Pending，再执行；适配器不直接写数据库；不得因数据库异常重复真实动作。

7. **坐标权威**  
   数字孪生 CSV 在坐标冲突时优先于旧文档；当前只有 XY 已导入，真实动作仍被门禁阻止。

8. **双针调度策略**  
   当前采用保守、可解释、可审计的资源锁；不引入 OR-Tools 作为当前核心运行依赖。

9. **权限与技术信息分层**  
   OP/AD 显示在标准登录；ENG 不在登录界面显示。操作员界面隐藏技术细节，工程端保留诊断。

10. **首页简化**  
    移除常显复杂流程卡和常显日志；最近事件按需打开。

11. **SOCON 进程隔离**  
    SOCON 未来由独立 .NET Framework 4.5.2 x86 Bridge 承载，不直接嵌入现有 .NET 9 主进程。

12. **真实硬件推进顺序**  
    先离线验证，再只读，再安全 IO，再校准，再空跑，再湿实验，不能跳阶段。

---

## 18. 开发规范

### 18.1 代码与架构规范

- 不得让 MockRuntimeStore、浏览器状态或旧原型成为业务权威；
- 不得让设备适配器直接写业务数据库；
- 不得在真实动作未知/失败后自动重试；
- 不得在未确认安全条件时建立“可用”的真实动作接口；
- 不得在 Real 下静默模拟成功；
- 不得以最新配置替代运行冻结快照；
- 不得在历史记录缺少快照时静默补用当前配置；
- 不得自动释放 Unknown 状态的资源；
- 不得通过 UI 绕过正式应用服务；
- 不得把 SDK 加载成功写成设备可用；
- 不得在无现场事实时猜测 NodeID、泵映射、坐标、限位或安全 IO。

### 18.2 Git 规范

- 每阶段完成后先执行：
  ```cmd
  git diff --check
  git status --short
  ```
- 未经用户确认不得自动提交；
- 提交前必须确认待提交文件；
- 不得提交：
  - 厂商 DLL；
  - SDK；
  - Demo；
  - 驱动；
  - 授权文件；
  - 本机配置；
  - `bin/`、`obj/`；
  - SQLite WAL/SHM；
  - 临时数据库；
  - 临时探针；
  - 测试产物；
  - 临时备份。
- Git 命令必须在：
  ```text
  D:\Stainer
  ```
  中执行。

### 18.3 数据库规范

- 不得单独删除运行中的 WAL/SHM；
- 不得盲目覆盖正式数据库；
- 备份先在临时目录生成，成功后提升到正式目录；
- 恢复前执行完整验证；
- 不因设备接入方便而绕过 EF Core 或随意新增迁移。

### 18.4 真实硬件规范

真实硬件未验证前，禁止：

- 扫描 USB、COM、CAN、网络；
- 打开端口；
- 连接设备；
- 加载/反射/实例化 SOCON SDK；
- 回零；
- 运动；
- 液面检测；
- 吸液/加液；
- 启动 DCR55；
- 写入温控/制冷/PWM/混合；
- 自动安装驱动或依赖；
- 自动重试动作；
- 在没有安全 IO 的条件下解除动作门禁。

### 18.5 文档规范

每个重要阶段必须记录：

- 范围；
- 修改文件；
- 已完成能力；
- 未完成能力；
- 禁止事项；
- 数据库影响；
- API 影响；
- 硬件影响；
- 测试结果；
- Git 状态；
- 下一阶段前置条件。

厂商资料只记录必要技术事实，不提交：

- DLL 内容；
- 绝对路径；
- 实际 COM/IP；
- NodeID；
- 真实设备序列号；
- 授权信息；
- 真实泵映射；
- 实际坐标。

---

## 19. GPT 与 Codex 协作方式

### 19.1 GPT 职责

GPT 负责：

- 梳理需求和阶段边界；
- 审核架构；
- 区分已验证事实与假设；
- 审核 Codex 修改报告；
- 判断是否存在越权、硬件风险、数据库风险或事实夸大；
- 形成简洁、可执行的 Codex 指令；
- 在 Git 提交前审阅实际状态；
- 管理长期项目文档和阶段路线；
- 明确何时可以进入下一阶段。

GPT 不应：

- 自动认同 Codex 结论；
- 把 Mock 结果当真实硬件结果；
- 在无现场资料时推断设备参数；
- 要求 Codex 提前接入动作；
- 让 Codex 未经确认自动提交；
- 以“可加载 SDK”宣称“可控制机械臂”。

### 19.2 Codex 职责

Codex 负责：

- 在明确范围内写代码和文档；
- 执行允许的构建、测试与静态检查；
- 输出修改文件、验证结果、风险和未完成项；
- 不擅自扩大范围；
- 不自动连接、扫描、控制真实硬件；
- 不提交未被允许提交的内容；
- 不引入厂商 SDK/Demo 到 Git。

### 19.3 推荐流程

```text
1. GPT 定义任务、边界、禁止项和验证方式。
2. 用户将指令交给 Codex。
3. Codex 实施并输出报告。
4. 用户提供 Codex 报告、构建/测试结果、git status。
5. GPT 审核范围、风险、数据库/硬件影响和收口条件。
6. 用户执行 Git 检查。
7. GPT 根据实际 Git 状态给出精确 add / commit 指令。
8. 提交完成后更新本文件并进入下一阶段。
```

---

## 20. 当前已知限制与风险

### 20.1 Mock 完成不等于真实设备完成

Mock 已证明：

- 业务规则；
- 数据库模型；
- 服务层；
- UI 正式数据接入；
- Mock 设备状态机；
- Mock 调度；
- Mock 追溯；
- 浏览器流程。

Mock 未证明：

- 真实机械精度；
- 真实液体行为；
- 实际扫码可靠性；
- 实际温控/制冷稳定性；
- 串口、CAN 通讯可靠性；
- 真实安全 IO；
- 真实动作时序；
- 真实湿实验质量；
- 性能目标。

### 20.2 SOCON 风险

- SDK 已完成隔离加载取证，并已实现受双门禁约束的只读会话软件边界；真实运行副作用和现场行为仍未知；
- 静态初始化实际副作用未知；
- 运行期依赖可能缺失；
- CAN 底层为 x86 原生；
- NodeID、轴映射、泵映射、安全 IO 均未知；
- `SetMaxTrip` 的默认值不得直接视为现场安全限位；
- 除经 P1-02 明确授权、双门禁和白名单约束的只读连接/状态/静态位置读取外，无现场验证前不得调用 SDK 设备方法；初始化、回零、运动和液路方法继续绝对禁止。

### 20.3 坐标风险

- 仅完成 XY 基线；
- Z 与工艺高度未完成；
- 真实坐标尚未校准；
- 真实动作必须保持门禁关闭。

### 20.4 安全风险

以下真实安全能力尚未接入：

- 急停；
- 门禁；
- 抽屉状态；
- 真实限位；
- 真实碰撞；
- 温度异常；
- 制冷异常；
- 水/PBS/废液/有毒废液状态；
- 机械臂异常；
- CAN/串口断连。

### 20.5 部署与恢复风险

尚需确认：

- Windows Kiosk / 全屏；
- 开机启动；
- 服务恢复；
- 断电恢复；
- 运行中断恢复；
- 设备重新初始化；
- 日志保留；
- 备份保留；
- 工程维护流程；
- 现场安全操作 SOP。

---

## 21. 当前正式命令

### 21.1 启动 Web Host

```cmd
cd /d D:\Stainer
dotnet run --project backend\Stainer.Web\Stainer.Web.csproj
```

预期地址通常为：

```text
http://127.0.0.1:5205
```

停止：

```text
Ctrl + C
```

### 21.2 主项目构建和测试

```cmd
cd /d D:\Stainer
dotnet build Stainer.sln --configuration Release --no-restore
dotnet test Stainer.sln --configuration Release --no-build
```

### 21.3 Git 检查

```cmd
cd /d D:\Stainer
git diff --check
git status --short
```

### 21.4 Bridge 正式构建验证（P0-02 已完成）

Bridge 已创建并提交；正式 `.csproj + MSBuild + net452 Targeting Pack` 构建验证**已完成**（VS2022 MSBuild Release|x86，59 checks passed）。构建命令参考：

```cmd
cd /d D:\Stainer
MSBuild bridges\Stainer.SoconBridge\Stainer.SoconBridge.csproj /p:Configuration=Release
```

随后执行实际输出目录中的：

```text
Stainer.SoconBridge.exe --self-test
```

---

## 21.5 文档与 Git 状态核验要求

本文件不替代当前工作树状态。23D Bridge 已完成并提交；`README.md`、`project_context/PROJECT_CONTEXT.md` 与 `project_context/TODO.md` 均为 Git 跟踪文件，本轮同步修改尚未提交。

后续继续推进 Bridge 正式构建验证或真实设备阶段前，仍必须确认：

```cmd
cd /d D:\Stainer
git diff --check
git status --short
```

并人工确认：

- `real-hardware\socon-sdk-compatibility-report.md` 的内容为最终修订版；
- 没有厂商 SDK、Demo、DLL、驱动、授权文件或临时探针进入待提交列表；
- 没有将最终修订版仅保留在下载目录而未写回仓库目标文件；
- Bridge 正式 net452 x86 构建验证已完成（P0-02，59 checks passed @ 2026-07-08）；P1-01 SDK 运行时部署验证已完成 / ready-to-close（60 checks passed）；
- P1-02 只读会话的 SDK 实例化/连接代码已实现，但尚未在真实环境执行；真实设备连接和动作控制均未开始。除后续明确授权的只读验证外，不得调用 SDK；动作控制继续禁止。

---

## 22. 当前项目结论

### 已完成

```text
正式 .NET 9 / SQLite 业务权威
HE/IHC 批次与工作流快照
DAB 生命周期
试剂、库存、跨瓶预约与消耗
温控、制冷、液路、运动 Mock
双针与资源锁
工程诊断、管理、审计和报警
操作员 UI 正式数据接入
工程/管理/历史 UI 正式数据接入
浏览器验收和 Mock 阶段收口（历史基线曾完成；当前完整回归失败，单页浏览器验收待复核）
数字孪生 XY 基线导入和真实动作门禁
主控/DCR55/制冷离线协议
离线 Real 读取边界
SOCON SDK 兼容性取证与兼容性报告 Git 收口（P0-01，已完成并已提交至 HEAD e7c127df）
SOCON 独立 x86 Bridge 最小骨架与部署前检查（23D，已完成并提交）
Bridge 正式 net452 x86 构建验证（P0-02，已完成 @ 2026-07-08，59 checks passed）
SDK 运行时部署验证（P1-01，已完成 / ready-to-close @ 2026-07-08，60 checks passed；未连接硬件/未实例化 SDK/未调用动作）
扫码器配置持久化与管理 API（ScannerProfile/ScannerRegion；不代表真实设备参数已生效）
扫码区域配置表达（区域编号、扫码顺序、坐标档案关联、坐标版本关联）
试剂坐标锚点与首尾坐标插值生成
DCR55 控制服务抽象和 Restart / Calibration Light / ROI 后端接口（不代表真实硬件验证）
样本 DCR55 与试剂主控 0x08 扫码职责分离、ArmCamera/SampleScan/ScannerRegion 坐标语义确认
坐标 Z 语义补齐（Z-Travel/Z-Start/Z-End/Z-Dispense；新增 AspirateEndZUm；未接入 Motion 执行）
LiquidClass 参数补齐（LiquidFollowingDepthUm/RetractSpeedUmPerSecond/ConditioningVolumeUl/BreakoffSpeedUlPerSecond/PostDispenseAirGapUl；未接入执行）
工程手动移液测试 API（LiquidDetect/Aspirate/Dispense/Wash/Flush；权限+会话+幂等+审计+Real fail-closed；未接真实设备）
Workflow 通道关联模型评估完成（无需另建关联模型；ChannelBatch→单一 WorkflowVersion→4 玻片共享已强制；PlanningRulesJson 字段与迁移已提交）
Mock Workflow 执行/调度基线（执行器、运行状态机、运行账本、资源租约、暂停/恢复/故障处理；当前完整回归仍为红）
主控真实串口白名单只读边界完善（未连接真实COM）
SOCON Bridge受门禁真实只读会话软件实现（未连接真实USB2CAN/节点）
```

### 未完成

```text
真实主控串口接入
真实 DCR55 接入
DCR55 真实串口控制验证、ROI 命令现场验证、校验光现场验证和真实硬件联调
数据库配置接管 DCR55 运行参数
扫码器基础配置/控制页面已接线；ScannerRegion 专门的区域/目标位可视化配置与真实效果验证未完成
DCR55 工具偏移、Slot/ScannerRegion 样本扫码目标位、SampleScan 去留/改名
主控 0x08 试剂扫码 ch1-ch5 与列/R 位映射、EMPTY/INVALID 原始格式
真实制冷接入
真实 SOCON / USB2CAN 接入
真实温控、液路和混合接入
真实安全 IO
NodeID/轴/泵映射确认
Z 与真实坐标校准
真实设备只读验证
真实设备动作验证
HE 空跑与湿实验
IHC 空跑与湿实验
实际 LIS 接入
真实性能验证
Kiosk、自动恢复和现场部署验证
修复当前完整后端回归的 61 项失败，并重跑单页浏览器验收
```

### 当前最重要原则

> Mock 功能阶段曾完成收口，但当前 HEAD 完整后端回归为 252/313 通过、61 失败，不能视为发布可用；真实设备只读软件准备已经开始，但真实硬件现场验收尚未开始。
> 后续必须严格按“Bridge → 部署验证 → 只读 → 安全 IO → 校准 → 空跑 → 湿实验”的顺序推进。
> 任何真实动作都不得建立在未验证的硬件事实、猜测坐标或未接入安全链路之上。
