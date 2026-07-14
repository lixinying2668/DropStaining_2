# `/control-console` 数字孪生 控件 → 后端 API 映射表

> 生成日期：2026-07-14。目标文件：[`wwwroot/twin/index.html`](../backend/Stainer.Web/wwwroot/twin/index.html)（单体 ~6056 行）。本页与 operator 主控页（`LegacyUiPageRenderer`）是**两套独立控制面**：孪生页以 2D 可视化 + 工程调试为主，主控页以表单化操作为主。后端端点为同一套 ASP.NET API（详见 [ui-api-mapping.md](ui-api-mapping.md)）。
>
> 鉴权已于本次先期改造为 cookie 范式（`/api/login`、`/api/logout`、`/api/current-user`），移除了原 localStorage 假登录。

## 0. 数据来源分类法（判定依据）

每一行「当前数据来源」按以下证据判定：

**`snapshot`** — 由 `applyDatabaseSnapshot(snapshot)`（[twin:5954](../backend/Stainer.Web/wwwroot/twin/index.html#L5954)）消费 `GET /api/twin/snapshot` 的下列顶层键写入内存：
| snapshot 键 | 写入的内存 map / 对象 | 驱动的可视化 |
|---|---|---|
| `items[]` | `itemLevels` / `itemState` | 40 试剂位、8 配液孔、A/B 液、废液/排毒/清洗孔的余量与状态 |
| `slideTemps[]` | `slideTemps` | 16 玻片温度 |
| `slideOps[]` | `slideOps` | 16 玻片步骤进度 |
| `channels[]` | `channels[i]` | 4 通道状态/进度/配置 |
| `configProfiles` | `configProfiles` | 配置文件（随后落 localStorage 缓存） |
| `liquids` | `liquids` | 纯水/PBS/废液/排毒百分比 |
| `metrics` | `headerMetrics` | 今日/活跃/总计样本数 |
| `cameras` | `cameraStates` | 试剂扫码相机、随动相机 |
| `arm` | `arm` | 机械臂 x/y/z1/z2/fluid1/fluid2 |
| `control_values{}` | 逐控件 `value`/`textContent` | 通用单值映射 |
| `precheckResults` | `window.digitalTwinPrecheckResults` | 11 项检测结果 |
| `scalars.reagent_current_temperature_c` | KPI 温度 | 试剂当前温度 |

**`local-mock`** — 由 `resetDemo()`（[twin:3691](../backend/Stainer.Web/wwwroot/twin/index.html#L3691)）或模拟循环 `runDemo()`（[twin:3661](../backend/Stainer.Web/wwwroot/twin/index.html#L3661)）写入、`applyDatabaseSnapshot` **不**消费的内存（如 `coords`、`uiMode`、按钮态、`log()` 队列）。

**`localStorage`** — 残留键（认证相关已删除）：
| 键 | 用途 | 写入函数 |
|---|---|---|
| `pathologyStainer.runtimeSettings.v1` | `appSettings`（通讯/设备参数/阈值/UI 偏好） | `saveAppSettings()` [twin:2831](../backend/Stainer.Web/wwwroot/twin/index.html#L2831) |
| `pathologyStainer.configProfiles.v1` | 配置文件列表 | `saveConfigProfilesOnly()` [twin:4323](../backend/Stainer.Web/wwwroot/twin/index.html#L4323) |
| `pathologyStainer.channelConfigAssignments.v1` | 通道↔配置绑定 | `persistAssignments()` [twin:4333](../backend/Stainer.Web/wwwroot/twin/index.html#L4333) |
| `digitalTwinRightPanel{NormalRatio,Ratio,Width}` | 右侧栏宽度布局 | [twin:5741-5848](../backend/Stainer.Web/wwwroot/twin/index.html#L5741) |

**`unwired`** — 控件存在但无任何读写绑定（装饰、占位）。**`already-wired`** — 已直接调用真实后端 API。

**接入优先级**：`P0` 当前为 mock 但关系安全/可运行性（运行启停、扫码入库、检测、移液）；`P1` 未接线但有现成 API、操作价值高；`P2` 工程师调试/配置（有 API）；`P3` 纯可视化/动画或布局，保留 mock。

---

## 1. 逐区映射表

> 列：控件 ID / 区域 · 类型 · 当前数据来源 · 目标后端 API · 是否已有 API · 权限角色 · 接入优先级

### 1.1 登录 / 用户（已接线，不展开）
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `loginUsername`/`loginPassword`/`adminLoginBtn`/`operatorLoginBtn`/`loginErrorText` | input/button/display | already-wired | `POST /api/login`、`POST /api/logout`、`GET /api/current-user` | yes | all | — |
| 用户管理面板（动态渲染） | container | already-wired | `/api/users`、`/api/roles` CRUD | yes | admin | — |
| `userBtn`/`userMenu` | button/menu | already-wired | 同上 | yes | admin | — |

### 1.2 顶部 Header / 模式
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `startBtn`（开始） | button | **local-mock**（→`runDemo()` [5884](../backend/Stainer.Web/wwwroot/twin/index.html#L5884)） | `GET /api/run/preflight` → `POST /api/runs` → `POST /api/runs/{id}/start` | yes | operator+admin | **P0** |
| `pauseBtn`（暂停/恢复） | button | **local-mock**（`paused` 切换） | `POST /api/runs/{id}/{pause,resume}` | yes | operator+admin | **P0** |
| `totalSampleCount`/`todaySampleCount`/`activeSampleCount` 等 | display | snapshot（`metrics`） | `GET /api/twin/snapshot` | yes | op/eng/admin | — |
| `modeTwinBtn`/`modeDebugBtn`/`modeProductionBtn` | tab | local-mock（`uiMode`） | `POST /api/device-mode/change` | yes | eng+admin | P1 |
| `connDot`/`connText`/`phaseText` | display | local-mock（`setConn()`/`setPhaseText()`） | `GET /api/device/state` | yes | op/eng/admin | P1 |

### 1.3 中央画布 — 试剂区 / 配液区 / 废液排毒
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `svg-reagent-s{col}{row}`（5×8=40） | canvas | snapshot（`items`→`itemLevels`/`itemState`） | `GET /api/twin/snapshot` | yes | op/eng/admin | — |
| `svg-mix-p{r}{c}`（2×4=8 配液孔）、A/B 液、`svg-port-{waste,toxic,clean}-*` | canvas | snapshot（同上） | `GET /api/twin/snapshot` | yes | op/eng/admin | — |
| `svg-reagent-legend-*-remaining-ml` | display | local-mock（`itemLevels`×`appSettings.reagentBottleCapacityMl`） | snapshot 已含；容量在 settings | yes | op/eng/admin | P1 |
| `svg-reagent-temperature-control`/`reagentTempText` | display | snapshot（`scalars.reagent_current_temperature_c`） | `GET /api/thermal/telemetry`（备选） | yes | op/eng/admin | — |
| `svg-reagent-lane-{1-5}-{position,entry}-sensor` | display | local-mock | `GET /api/twin/snapshot`（control_values） | yes | op/eng/admin | P2 |
| `svg-reagent-lane-separator-*`、`svg-zone-*` | display | unwired（装饰） | — | — | — | P3 |

### 1.4 中央画布 — 玻片通道 / 机械臂 / 相机
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `svg-slide-r{ch}{idx}`（16） | canvas | snapshot（`slideOps`+`slideTemps`+`items`） | `GET /api/twin/snapshot` | yes | op/eng/admin | — |
| `svg-channel-{c}-start-button`（4 个"开始"扫码入库） | button | **local-mock**（→`startChannelBarcode()` [3209](../backend/Stainer.Web/wwwroot/twin/index.html#L3209)） | `POST /api/device/reagent-scanner/qr/start` | yes | op/eng/admin | **P0** |
| `svg-channel-{c}-button`/`-tray` | button/canvas | local-mock（→`showChannelDetail()`） | `GET /api/twin/snapshot`（channels） | yes | op/eng/admin | — |
| `svg-arm-{x-rail,y-axis,head}`/`-needle-z{1,2}` | canvas | snapshot（`arm`） | `GET /api/twin/snapshot`/`GET /api/device/state` | yes | op/eng/admin | — |
| `svg-camera-reagent-scanner`/`svg-arm-camera-follow` | canvas | snapshot（`cameras`） | `GET /api/twin/snapshot` | yes | op/eng/admin | — |

### 1.5 右侧面板 — 状态/详情/工程指令
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `detailBox` | display | snapshot（`renderDetail`） | `GET /api/twin/snapshot` | yes | op/eng/admin | — |
| `channel{1-4}ConfigBox`/`statusChannel{1-4}ConfigSelect` | select | snapshot+localStorage | `POST /api/engineering/coordinate-profiles/.../activate` | yes | eng+admin | P1 |
| `{baseId}-barcode-scan-card`（ROI/坐标/扫描按钮组） | button 组 | **local-mock**（仅 `log()` [5028](../backend/Stainer.Web/wwwroot/twin/index.html#L5028)） | `POST /api/device/reagent-scanner/qr/{start,stop,text,status,report,clear}`；`/api/scanners`、`/api/scanner-regions` | yes | eng+admin | **P0** |
| `{baseId}-object-engineering`（吸液/打液/液面探测/孔位/混匀，~20 按钮） | button 组 | **local-mock**（仅 `log()` [3035](../backend/Stainer.Web/wwwroot/twin/index.html#L3035)） | `POST /api/engineering/pipetting-tests/{liquid-detect,aspirate,dispense,wash,flush}`；`/api/fluidics/*`；`/api/thermal/*` | yes | eng+admin | **P1** |
| `{baseId}-coord-editor`（坐标 x/y 保存/复位） | input/button | local-mock（内存 `coords`） | `PUT /api/scanners/{id}/roi/apply`；`PUT /api/reagent-coordinate-anchors` | yes | eng+admin | P1 |
| `logList`/`warnList`/`warnPanelCount` | display | local-mock（前端 log 队列） | `GET /api/alarms`（filter） | yes | op/eng/admin | P2 |
| `logFilterInput`/`warnFilterInput` 等 | input | unwired（前端筛选） | — | — | — | P3 |

### 1.6 右侧面板 — KPI / 液位 / 通道卡片（production status）
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `kpiProgress`/`overallBar` | display | local-mock（`channels[].progress` 均值） | snapshot 已含 channels.progress | yes | op/eng/admin | — |
| `kpiStep`/`stepBar` | display | local-mock（`currentStepIndex`/`DEMO_STEPS`） | `GET /api/runs/current` 当前步骤 | yes | op/eng/admin | P1 |
| `kpiTemp` | display | snapshot（`scalars`） | `GET /api/twin/value/reagent_current_temperature_c` | yes | op/eng/admin | — |
| `liquidList` | gauge | snapshot（`liquids`） | `GET /api/twin/snapshot`/`GET /api/fluidics/state` | yes | op/eng/admin | — |
| `channelCards`（4） | display | snapshot（`channels`） | `GET /api/twin/snapshot` | yes | op/eng/admin | — |

### 1.7 检测（Precheck）
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `precheckRunAllBtn` | button | snapshot（`precheckResults`） | `GET /api/device-initialization` / `GET /api/device/state` | yes | op/eng/admin | **P0** |
| `precheckStep{1-11}` | display/button | snapshot（每项读 `precheckResults[label]`） | 同上（逐项真实状态需后端在 snapshot 填充） | 部分 | op/eng/admin | **P0** |

### 1.8 配置页（files / rules / liquid / position / pipette / scanner / barcode / thermal）
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `configProfile*`（选择/新建/复制/删除/导入导出 + 步骤编辑器） | input/button | **localStorage**（`configProfiles`） | `GET/POST/PUT/DELETE /api/engineering/coordinate-profiles` + `publish`/`activate` | yes | eng+admin | **P1** |
| `liquidClass*`（选择/吸液/加液参数卡） | input/button | local-mock（仅 log） | `GET/POST /api/engineering/liquid-classes` | yes | eng+admin | P2 |
| 通道移位表单（`renderConfigPositionSection`） | input/button | local-mock | `GET /api/engineering/layout`；`/api/reagent-coordinate-anchors` | yes | eng+admin | P2 |
| 通道移液表单（`renderConfigPipetteSection`） | input/button | local-mock | `POST /api/engineering/pipetting-tests/*` | yes | eng+admin | P2 |
| 扫码器表单（`renderConfigScannerSection`） | input/button | local-mock | `/api/scanners`、`/api/scanner-regions`、`/scanners/{id}/{restart,calibration-light/*,roi/apply}` | yes | eng+admin | **P1** |
| 条码扫描卡（`barcodeSampleCardR*`/`barcodeReagentCellS*`） | input/display | local-mock | `/api/scanner-regions`；`/api/device/reagent-scanner/qr/*` | yes | eng+admin | P2 |
| 清洗混匀（`thermalMixParamCard`/`thermalSampleWashCard`） | input/button | local-mock | `/api/fluidics/mixers/{drawer}/{start,stop}`；`/api/fluidics/wash` | yes | eng+admin | P2 |
| 规则卡（`configRulesCard`，DAB 配比/约束） | input | local-mock | 存入 profile（coordinate-profiles） | yes | eng+admin | P1 |

### 1.9 设置页（runtime / communication / device / safety / ui）
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `settingsModeText` | display | local-mock（`uiMode`） | `GET /api/device-mode` | yes | op/eng/admin | P1 |
| `settingsPrecheckText` | display | local-mock（`precheckPassed`） | `GET /api/device-initialization` | yes | op/eng/admin | P1 |
| `settingsDataInterfaceInput`/`settingsHostAddressInput`/`settingsHeartbeatInput`/`settingsLogRetentionInput` | input | localStorage（`appSettings`） | proposed `POST /api/device/communication/config` | **no** | eng+admin | P2/P3 |
| `settingsNeedleGapInput`/`settingsReagentCapacityInput` | input | localStorage→snapshot 覆盖 | proposed `GET/POST /api/device/config` | **no** | eng+admin | P2 |
| `settingsReagentTargetInput`/`settingsWorkTargetInput` | input | localStorage→snapshot 覆盖 | `POST /api/thermal/{points/{drawer}/{slot},boards/{drawer}}` | yes | eng+admin | P2 |
| `settings{Pure,Pbs,Waste,Toxic}ThresholdInput` | input | localStorage→snapshot 覆盖 | `POST /api/fluidics/liquid-levels`（阈值） | yes | eng+admin | **P1** |
| `settingsSaveBtn`/`settingsResetBtn` | button | localStorage | proposed `POST /api/twin/settings` | **no** | eng+admin | P2/P3 |

### 1.10 调试页（admin-only）
| 控件 | 类型 | 来源 | 目标 API | 已有 | 角色 | 优先级 |
|---|---|---|---|---|---|---|
| `debugComCard`（COM/波特率/校验） | input | local-mock | proposed `POST /api/device/communication/config` | **no** | eng+admin | P2 |
| `debugPrecisionCard`（X/Y 校正/加样体积） | input | local-mock | proposed `POST /api/device/precision/calibrate` | **no** | eng+admin | P2 |
| `debugLiquidClassCard` | display | local-mock | `GET /api/engineering/liquid-classes` | yes | eng+admin | P2 |
| `debugModuleTestCard`（加样臂/条码/混匀/清洗/校验光/停止） | button 组 | local-mock | 条码→scanner API；混匀→`/api/fluidics/mixers/.../start`；清洗→`/api/fluidics/wash`；校验光→`/api/scanners/{id}/calibration-light/enable` | 部分 | eng+admin | P2 |
| `debugCommandConsole` | display | local-mock | — | — | — | P3 |

---

## 2. 已确认的「死端」（核实过的重点）

### 2.1 幽灵按钮 — JS 绑定但 DOM 不存在（6 个）
[twin:5886-5891](../backend/Stainer.Web/wwwroot/twin/index.html#L5886) 用 `bindClick('id', fn)` 绑了 6 个按钮，但全文无对应 `id="..."` 元素，`bindClick` 静默空转：

| 幽灵 ID | 绑定的本地动作 | 应接入 API | 优先级 |
|---|---|---|---|
| `scanSamplesBtn` | `scanSamples()`（样本扫码 mock） | `POST /api/device/reagent-scanner/qr/start` | **P0** |
| `scanReagentsBtn` | `scanReagents()`（试剂扫码 mock） | `POST /api/device/reagent-scanner/qr/start` | **P0** |
| `lowReagentBtn` | `simulateLowReagent()` | `POST /api/device/mock-faults` | P1 |
| `pullChannelBtn` | `togglePullChannel()` | `POST /api/device-mode/change` | P1 |
| `alarmBtn` | `simulateAlarm()` | `POST /api/device/mock-faults` | P1 |
| `resetBtn` | `resetDemo()`（本地复位） | proposed `POST /api/device/reset`（无现成端点） | P1 |

> 处理方式：要么在 HTML 加回这些按钮（底部工具栏）再接线，要么删掉这 6 行死绑定。

### 2.2 仅 `log()` 不发请求的批量按钮
- `[data-object-action]` → `bindGenericObjectActionButtons()` [twin:3035](../backend/Stainer.Web/wwwroot/twin/index.html#L3035)：吸液/打液/液面探测/孔位移位/混匀等 ~20 个工程指令按钮，仅 `log("…已进入模拟队列")`。
- `[data-action-log]` → `bindEngineeringControlHandlers()` [twin:5028](../backend/Stainer.Web/wwwroot/twin/index.html#L5028)：调试页加样臂/条码/清洗/校验光等按钮，同样仅 `log()`。

这两类是孪生页「工程调试」的核心交互面，目前完全未接后端。

---

## 3. 优先级汇总与接入建议

### P0 — 部署到真机前必须接入（当前为本地模拟，关系安全/可运行性）
1. **`startBtn`/`pauseBtn`** → 运行生命周期：`GET /api/run/preflight` → `POST /api/runs` → `POST /api/runs/{id}/start`；暂停/恢复 → `pause`/`resume`；进度通过 SignalR `/hubs/machine` 或轮询 `/api/runs/current` 回灌 `applyDatabaseSnapshot`。
2. **`svg-channel-{c}-start-button`（×4）** → `POST /api/device/reagent-scanner/qr/start`（扫码入库）。
3. **`{baseId}-barcode-scan-card` 扫码按钮组** → `POST /api/device/reagent-scanner/qr/{start,stop,text,status,report,clear}`。
4. **Precheck 11 项** → 确保后端 `GET /api/twin/snapshot` 的 `precheckResults` 返回每项真实状态（或前端逐项查 `/api/device-initialization`/`/api/device/state`）。
5. **幽灵按钮 `scanSamplesBtn`/`scanReagentsBtn`** → 决定加回 DOM 并接 `qr/start`，或删除死绑定。

### P1 — 有现成 API、操作价值高
- 工程指令面板（`data-object-action` ~20 按钮）→ 按动作分发到 `/api/engineering/pipetting-tests/*`、`/api/fluidics/*`、`/api/thermal/*`。
- 配置文件 CRUD（`configProfiles`）→ 从 localStorage 迁到 `/api/engineering/coordinate-profiles`（保留 localStorage 作离线缓存）。
- 扫码器配置表单 → `/api/scanners`、`/api/scanner-regions`、`/scanners/{id}/roi/apply`。
- 安全阈值输入 → `POST /api/fluidics/liquid-levels`；设备模式 → `GET/POST /api/device-mode[/change]`；KPI 步骤 → `/api/runs/current`。
- 其余 4 个幽灵按钮（`lowReagentBtn`/`pullChannelBtn`/`alarmBtn`/`resetBtn`）。

### P2 — 工程师调试/配置（有 API，价值中等）
- 液体类型卡、通道移位/移液表单、条码扫描卡、清洗混匀卡、调试页 COM/精度/模块测试。
- 通讯/设备参数设置（部分需新建 `/api/device/communication/config`、`/api/device/config`）。

### P3 — 保留 mock / 纯前端
- 区域背景、分隔线、图例、日志筛选、右侧栏布局比例、时钟、自动检测开关、命令控制台。

### 无后端 API、需新建（汇总）
`POST /api/device/communication/config`（COM/通讯）、`POST /api/device/config`（针距/试剂容量）、`POST /api/device/precision/calibrate`（精度校正）、`POST /api/device/reset`（复位）、`POST /api/twin/settings`（统一设置保存）。这些对应孪生页的设置/调试卡，目前以 localStorage 或 log 承载。

---

## 4. 建议接入顺序
1. **P0 运行生命周期 + 扫码入库 + precheck**：让孪生页能驱动一次真实运行。
2. **P1 工程指令面板分发器**：把 `bindGenericObjectActionButtons` 的 `log()` 改为按 action→endpoint 路由。
3. **P1 配置文件迁移**：`configProfiles` localStorage → coordinate-profiles CRUD。
4. **清理幽灵按钮**：加回 DOM 或删绑定。
5. **P2 设置/调试**：补新建端点 + 接线。
