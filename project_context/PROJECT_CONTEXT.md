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
  - Mock 业务闭环、正式数据模型、正式 Web HMI 接入和浏览器验收已完成；
  - 真实设备接入仍处在“离线协议、接口取证、真实动作前基础设施”阶段；
  - 尚未进入真实机械臂动作、真实湿实验或真实性能验收。

### 1.2 项目状态记录与事实优先级

本文件记录的项目状态截至**阶段 23D“SOCON 独立 x86 Bridge 最小骨架与部署前检查”已完成并已提交；P0-01 SOCON 兼容性报告收口与 P0-02 Bridge 正式 net452 x86 构建验证均已完成；P1-01 SDK 运行时部署验证已完成 / ready-to-close（已配置 SDK 目录后完成运行时验证：核心文件齐、`can_bootloader.dll` PE=x86(0x014C)、SOCON.API/Utility 托管元数据加载 PASS、5 类型 available、self-test 60 checks PASS；optional warning 为 SOCON.ScEventBus.dll、C1.C1Zip.4.dll 缺失，当前不阻塞；未连接真实硬件、未打开 COM/CAN/TCP、未实例化 SDK 类型、未调用真实动作）；SDK 执行加载（实例化/静态成员/方法调用）、真实设备连接和动作控制尚未开始**。P1-01 通过不代表真实机械臂已连接或可运动。

为避免后续维护时将旧资料、Mock 行为或现场猜测混入正式结论，事实优先级如下：

1. 已经确认并进入当前正式代码/数据库模型的实现与测试结果；
2. 后续确认的硬件接口资料、现场事实与正式变更记录；
3. 数字孪生坐标 CSV 对于 XY 坐标冲突的裁决；
4. 《全自动冰冻切片染色机上位机软件需求规格说明书（完整修订版 v2.3）》作为业务与验收基线；
5. 历史原型、早期文档和未验证推测，仅可作为参考。

当第 2、3 项与旧需求文档或旧原型冲突时，应以已确认的后续事实为准，并在对应配置版本、审计或变更文档中留下追溯记录。

SOCON 兼容性报告的技术结论已经完成（P0-01 已收口，已提交至 HEAD `e7c127df`），23D Bridge 骨架已完成并提交，P0-02 Bridge 正式 net452 x86 构建验证已完成（VS2022 MSBuild Release|x86，59 checks passed）；后续仍必须以当前仓库内文件内容和 `git status --short` 为准，不能仅凭本文件推定。

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

业务需求基线为《全自动冰冻切片染色机上位机软件需求规格说明书（完整修订版 v2.3）》。

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
6. IHC 通道中，玻片可保留各自确认的一抗代码；一抗代码不改变通道共享工作流。
7. HE 当前允许无二维码的人工确认流程。
8. 一个通道内即使只有 1–3 张玻片，也视为完整批次；不要求必须放满四张。
9. 批次开始后禁止继续向该通道追加玻片。

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
| Mock 端到端后端验收 | 已完成 | HE/IHC、DAB、跨瓶、暂停恢复等 |
| 操作员 UI 正式数据接入 | 已完成 | Dashboard、样本、试剂、DAB、运行页 |
| 工程/管理/历史/报警 UI 接入 | 已完成 | 正式追溯与敏感信息分层 |
| 浏览器验收与 Mock 阶段收口 | 已完成 | Playwright Core、重连、多窗口、权限 |
| 数字孪生 XY 基线导入 | 已完成 | DigitalTwinXY-v1、真实动作硬门禁 |
| 主控/DCR55/制冷离线协议 | 已完成 | 主控协议、真实通讯软件基础设施、只读通讯边界与离线验证已完成，真实硬件连接和状态验证未开始、真实控制未开放；制冷状态不变；DCR55 离线协议与离线 Real Adapter 读取边界已完成，真实 Serial Transport 代码已完成，真实单次扫码验证未执行 |
| Real 读取边界 | 已完成 | 只读边界、真实写入 fail-closed |
| SOCON SDK 兼容性取证（23D-0） | 已完成 | 已确认独立 x86 Bridge 架构；不代表主项目可直接引用 SDK 或设备可动作 |
| SOCON 独立 x86 Bridge 骨架（23D） | 已完成并已提交 | 独立 net452 x86 Console；未加入 Stainer.sln；仅离线 IPC 与部署前检查 |
| Bridge 正式 net452 x86 构建验证（P0-02） | 已完成 | VS2022 MSBuild Release|x86 成功；输出 bin\x86\Release\Stainer.SoconBridge.exe；--self-test 59 checks passed；未加载 SDK/未连接硬件/未扫描总线 |
| SDK 运行时部署验证（P1-01） | 已完成 / ready-to-close | 进程级 `STAINER_SOCON_SDK_DIR` 设置后 Release|x86 Rebuild + `--self-test` 60 checks PASS；核心文件（SOCON.API/SOCON.Utility/can_bootloader）齐、`can_bootloader.dll` PE=x86(0x014C)、SOCON.API/Utility 托管元数据加载 PASS、5 类型 available（SCDevice/SCDeviceMA/Utility+e_ConnectType/Utility+DeviceTypeEnum/Utility+ProtocolTypeEnum）；optional warning（ScEventBus/C1.C1Zip）缺失但不阻塞；未连接硬件/未打开 COM/CAN/TCP/未实例化 SDK/未调用真实动作 |
| 真实运行安全门禁与异常恢复规则（P0-04 草案） | 规则草案编制中 / 待评审 / 未完成 | 已产出 `real-hardware/真实运行安全门禁与异常恢复规则.md`（草案 v0.1）：覆盖真实动作前总门禁、安全 IO 异常规则矩阵、通讯异常规则、运行中断与恢复规则、温控/制冷安全规则、真实能力分级门禁、P1 阶段阻塞关系、待现场确认清单；所有未现场确认内容标注「待现场确认」，未含真实敏感现场参数；**不代表**真实安全 IO 已接入，**不代表**可进入 P1-02 |
| 真实运动坐标参数与现场校准方案（P0-05 草案） | 校准方案草案编制中 / 待评审 / 未完成 | 已产出 `real-hardware/真实运动坐标参数与现场校准方案.md`（草案 v0.1）：仅以 DigitalTwinXY-v1 作为 Draft / MockOnly / Unverified 的 XY 设计基线；真实 Z、高度、速度、加速度、软限位、轴映射和现场校准值均待现场确认；**不代表**真实坐标已校准或机械臂可运动，**不放行 P1-08** |
| 真实设备连接/只读 | 未完成 | 需硬件、映射、安全信息和现场条件 |
| 真实动作、校准、湿实验 | 未完成 | 禁止提前实施 |
| 实际 LIS 接入 | 未完成 | 仅 Mock LIS 已完成 |

### 3.2 当前最新已知验证状态

- Mock 后端验收：已完成。
- 操作员 UI 正式数据接入：已完成。
- 工程/管理/历史/报警 UI 正式数据接入：已完成。
- 浏览器验收：已完成。
- 数字孪生 XY 导入及门禁验证：已完成。
- 主控、DCR55、制冷离线协议测试：已完成。
- 离线 Real Adapter 读取边界测试：已完成。
- Main Controller：主控真实通讯软件基础设施完成，已具备 SerialPort 抽象、Transport 路由和只读通讯边界；真实主控连接、真实状态读取与真实控制均未开始。
- DCR55 Serial Transport：代码已完成；`SerialPort` 隔离在 Infrastructure Transport 层，Mock / Real 隔离未受影响，Protocol、Real Adapter 与 Serial Transport 离线测试均已通过；真实设备验证仍未开始。
- 最近已确认主项目构建/测试：`Stainer.Tests` 构建诊断命令成功；**171/171 tests passed，TEST_EXIT=0**。
- 最近已确认 Bridge 自检结果：**正式 net452 x86 构建**（VS2022 MSBuild Release|x86，输出 `bin\x86\Release\Stainer.SoconBridge.exe`）`--self-test` **59 checks passed**（P0-02 已完成 @ 2026-07-08）。
- Bridge 正式构建状态：**已完成**（P0-02 已关闭）。
- P1-01 SDK 运行时部署验证：**已完成 / ready-to-close**——已配置 SDK 目录后完成运行时验证：核心文件（SOCON.API/SOCON.Utility/can_bootloader）齐、`can_bootloader.dll` PE=x86(0x014C)、SOCON.API/Utility 托管元数据 `ReflectionOnlyLoadFrom` 加载 PASS、5 类型 available（SCDevice/SCDeviceMA/Utility+e_ConnectType/Utility+DeviceTypeEnum/Utility+ProtocolTypeEnum）、`--self-test` 60 checks PASS；optional warning（SOCON.ScEventBus.dll、C1.C1Zip.4.dll）缺失但不阻塞；未连接真实硬件、未打开 COM/CAN/TCP、未实例化 SDK 类型、未调用真实动作。
- **风险备注**：`ReflectionOnlyLoadFrom()` 仅做元数据反射验证，不执行设备动作；P1-01 通过**不代表**真实机械臂已连接或可运动。
- 之前 testhost 异常：未稳定复现，不能作为已定位根因记录。
- 正式数据库：已验证迁移一致、无 pending migration。
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
| 当前允许能力 | Ping、状态、SDK 文件部署检查、托管 DLL 元数据反射验证（`ReflectionOnlyLoadFrom`） |
| 当前禁止能力 | SDK 类型实例化、SDK 静态成员访问、Connect/OpenPort、真实设备连接、动作、扫描硬件 |

---

## 5. 系统整体架构

### 5.1 当前正式架构

```text
┌─────────────────────────────────────────────────────────┐
│                         本地 Web HMI                     │
│ Dashboard / Samples / Reagents / DAB / Run / History     │
│ Engineering / Management / Alarms                        │
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
- 历史 FastAPI/Jinja/Python 原型；
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
│        └─ static\
│           ├─ js\
│           │  ├─ api.js
│           │  ├─ stainer-host.js
│           │  ├─ run.js
│           │  └─ dashboard.js
│           └─ css\
│              └─ app.css
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
├─ 全自动冰冻切片染色机_上位机软件需求规格说明书_完整修订版_v2.3.docx
│
├─ bridges\
│  └─ Stainer.SoconBridge\            # 23D 已创建并提交
│
├─ package.json
│
└─ src\                               # 历史 FastAPI/Jinja/Python 原型；非正式系统
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

`src/` 下的旧 FastAPI/Jinja/Python 实现仅作历史参考：

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

- 管理 R1–R40 试剂架；物理试剂架为 5×8，R1 位于左上角并按列向下编号；
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
- DCR55 已具备真实 Serial Transport 实现，但尚未进入真实设备验证，也尚未开放样本/LIS 业务扫码流程；
- 在真实硬件未验证前，所有写操作返回 `NotSupported` 或 `NotConfigured`。

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

- 为操作员提供 Dashboard、Samples、Reagents、DAB、Run 页面；
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

### 8.11 旧接口收紧（20）

以下旧接口只允许 Development：

```text
/api/dab
/api/logs
/api/slides/configure
/api/run/add-slide
/api/engineer/command
```

以下旧接口/控制台只允许 Development 或 Testing，不得在 Staging / Production 映射：

```text
/api/state
/api/run/*
/control-console
```

标准登录后入口为：

```text
/dashboard
```

### 8.12 浏览器验收与 Mock 阶段收口（21）

已完成：

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

```text
S11–S18 → R1–R8
S21–S28 → R9–R16
S31–S38 → R17–R24
S41–S48 → R25–R32
S51–S58 → R33–R40
```

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
| SOCON 机械臂 | 已完成模拟 | SDK 取证完成 | 未完成 | 未完成 | 未完成 |
| USB2CAN | 未完成 | 未完成 | 未完成 | 未完成 | 未完成 |
| 安全 IO | 部分 Mock/资料 | 部分资料 | 未完成 | 未完成 | 未完成 |
| LIS | Mock 已完成 | 实际接口未知 | 未完成 | 未完成 | 未完成 |

> Main Controller 的真实通讯软件基础设施已完成；表中的“真实连接”和“真实动作”仍为“未完成”，表示尚未打开真实 COM、连接真实主控、验证真实状态或开放真实控制命令。

> DCR55 的真实 Serial Transport 代码已完成；表中的“真实连接”仍为“未完成”，表示尚未连接实际设备、确认现场 COM 端口或执行真实单次扫码验证。

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
```

已知响应：

```text
条码文本 + CRLF
```

待确认：

- 命令结束符；
- 无码返回；
- 超时；
- 多码格式；
- 成功确认；
- 物理触发时序。

当前实现边界：

- 支持协议解析；
- 支持单码、多码、无码；
- 已建立 `ISerialPort` 抽象、`System.IO.Ports.SerialPort` Infrastructure 封装和 DCR55 Serial Transport；
- `SerialPort` 仅存在于 Infrastructure Transport 层，未改变 Mock / Real 隔离；
- 支持 COM 参数配置（115200 / 8 / None / 1）、受控单次触发命令 `RDCMXEV1,P11,P20\r`、CRLF 读取、超时、断开和打开失败处理；
- Protocol、Real Adapter 与 Serial Transport 均已完成离线测试；
- 尚未连接实际 DCR55、确认现场 COM 端口或执行真实单次扫码验证；
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

## 15. 已完成阶段：23D SOCON 独立 x86 Bridge

### 15.1 阶段目的

23D 已完成并已提交。该阶段只建立离线 Bridge 骨架、IPC、部署前文件/PE 检查和自检；未开始 SDK 加载、真实设备连接或动作控制。

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
- 不含 `Assembly.Load`、`Assembly.LoadFrom`、反射或 SOCON P/Invoke；
- 不创建 SOCON Real Adapter；
- 不修改现有 .NET 9 主项目、数据库、Migration、页面、API、运行流程、坐标模型或动作门禁；
- 不自动提交 Git。

### 15.4 本机配置

配置解析优先级：

1. `SoconBridge.config.local.json` 中的 `sdkDirectory`；
2. 环境变量 `STAINER_SOCON_SDK_DIR`；
3. 无配置则视为 SDK 路径未配置。

示例配置仅包含非敏感字段：

```json
{
  "sdkDirectory": "",
  "diagnosticsEnabled": true
}
```

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

当前仅允许命令：

```text
Ping
GetBridgeStatus
ValidateSdkDeployment
```

未知命令以及任何动作类命令一律返回：

```text
success = false
message = NotSupported
```

不得为 `Connect`、`OpenPort`、`Move*`、`Init*`、`LiqDet`、`Aspirate`、`Dispense`、`WaitActionDone` 等建立预留 DTO、接口、枚举或占位实现。

### 15.6 Bridge 状态模型

最少状态：

```text
Starting
Offline
SdkPathMissing
SdkFilesMissing
ArchitectureInvalid
DeploymentValidated
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

当前验证结论：**正式 net452 x86 构建**（VS2022 MSBuild Release|x86，输出 `bin\x86\Release\Stainer.SoconBridge.exe`）`--self-test` **59 checks passed**（P0-02 已完成 @ 2026-07-08）。

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

- SDK 仅完成隔离加载取证；
- 静态初始化实际副作用未知；
- 运行期依赖可能缺失；
- CAN 底层为 x86 原生；
- NodeID、轴映射、泵映射、安全 IO 均未知；
- `SetMaxTrip` 的默认值不得直接视为现场安全限位；
- 无现场验证前不得调用任何 SDK 设备方法。

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

本文件不替代当前工作树状态。23D Bridge 已完成并提交；`project_context/` 当前仍未被 Git 跟踪，是否入库需另行确认。

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
- 未开始 SDK 执行加载（`ReflectionOnlyLoadFrom` 仅为元数据检查）、真实设备连接或动作控制，除非后续阶段另有明确授权。

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
浏览器验收和 Mock 阶段收口
数字孪生 XY 基线导入和真实动作门禁
主控/DCR55/制冷离线协议
离线 Real 读取边界
SOCON SDK 兼容性取证与兼容性报告 Git 收口（P0-01，已完成并已提交至 HEAD e7c127df）
SOCON 独立 x86 Bridge 最小骨架与部署前检查（23D，已完成并提交）
Bridge 正式 net452 x86 构建验证（P0-02，已完成 @ 2026-07-08，59 checks passed）
SDK 运行时部署验证（P1-01，已完成 / ready-to-close @ 2026-07-08，60 checks passed；未连接硬件/未实例化 SDK/未调用动作）
```

### 未完成

```text
真实主控串口接入
真实 DCR55 接入
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
```

### 当前最重要原则

> Mock 阶段已经完成，但真实设备阶段尚未开始。  
> 后续必须严格按“Bridge → 部署验证 → 只读 → 安全 IO → 校准 → 空跑 → 湿实验”的顺序推进。  
> 任何真实动作都不得建立在未验证的硬件事实、猜测坐标或未接入安全链路之上。
