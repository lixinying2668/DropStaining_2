# Stainer 上位机

本仓库当前正式主线是 `backend/Stainer.Web`：ASP.NET Core + SQLite。正式 Web UI 已收敛为唯一的 `/control-console`（`wwwroot/twin/index.html`，自包含）；早期 FastAPI/Jinja 原型和重复静态资源已删除。

当前提交基线为 `012a698`，本轮测试修订尚未提交。Mock 业务能力和真实设备只读软件边界已形成基础，但项目尚未完成发布验收：2026-07-21 对当前工作树执行完整后端回归，结果为 **313 项全部通过、0 失败、0 跳过**；最新单页浏览器回归仍待重跑。SOCON Bridge 最近一次 Release|x86 构建为 **0 warning / 0 error**，离线 `--self-test` 为 **93 checks passed**；该结果没有连接 SDK 或真实硬件。

## 当前阶段边界

- 正式 UI 为唯一的 `/control-console`；旧的 `/dashboard`、`/samples`、`/run`、`/configure`、`/engineer`、`/admin` 等多页面、`LegacyUiPageRenderer` 及 `wwwroot/static/*` 已删除，旧 URL 返回 404（共 12 个旧正式页面 + `/mock-timeline` = 13 个旧 URL）。
- Mock 设备适配器用于本地验证，工程测试入口保留 Mock 标识；`Device:Mode=Real` 仍保持 fail-closed。
- 主控串口和 SOCON/USB2CAN 已具备受限只读软件边界，但尚未打开真实 COM/CAN、连接真实硬件或完成现场状态读取。
- 未执行真实扫码、初始化、回零、运动、温控、制冷、混匀或液路动作；真实动作继续禁止。
- 旧 `/api/state`、旧 `/api/run/start|pause|resume|stop` 仅在 Development/Testing 环境映射；Staging/Production 不可用。
- `/control-console` 为唯一正式 UI，在所有环境直接返回数字孪生页；根路径 `/` 重定向到它。

## 快速启动

```powershell
cd D:\Stainer
dotnet build Stainer.sln --configuration Release
dotnet run --project backend\Stainer.Web\Stainer.Web.csproj
```

默认地址：

```text
http://127.0.0.1:5205/
```

默认开发账号：

| 角色 | 用户名 | 密码 |
| --- | --- | --- |
| 操作员 | `operator` | `123456` |
| 工程师 | `engineer` | `123456` |
| 管理员 | `admin` | `123456` |

## 常用验证命令

```powershell
dotnet build Stainer.sln --configuration Release
dotnet test backend\Stainer.Tests\Stainer.Tests.csproj --configuration Release --no-restore --logger "console;verbosity=minimal"
npm run test:browser
npm run test:workflow-browser
npm run test:timeline-browser
dotnet ef migrations has-pending-model-changes --project backend\Stainer.Web\Stainer.Web.csproj --startup-project backend\Stainer.Web\Stainer.Web.csproj --configuration Release --no-build
dotnet build bridges\Stainer.SoconBridge\Stainer.SoconBridge.csproj --configuration Release --no-restore -p:Platform=x86
bridges\Stainer.SoconBridge\bin\x86\Release\Stainer.SoconBridge.exe --self-test
git diff --check
```

2026-07-21 当前工作树的完整后端回归为 313/313 通过；后续代码或测试变化后仍须重新记录实际通过、失败和跳过数量。

浏览器验收脚本会启动临时后端实例，使用独立临时 SQLite、执行器锁、日志和备份目录；脚本结束后默认清理临时目录，不应写入 `data/stainer.db`、正式备份或正式运行日志。

Bridge `--self-test` 默认为离线自检。除非另有明确的现场授权与受控配置，不得将其用于扫描或连接真实 USB2CAN、NodeID，也不得调用初始化、回零、运动或液路动作。

## 浏览器验收

浏览器验收入口：

```powershell
npm install
npm run test:browser
npm run test:workflow-browser
npm run test:timeline-browser
```

脚本使用 `playwright-core` 和系统 Chrome/Edge，默认 1920×1080、headless。可用 `PLAYWRIGHT_CHROME_EXECUTABLE` 指定浏览器路径。

历史 `test:browser` 覆盖范围包括：

- 操作员在页面上建立 HE 和 IHC 通道。
- 完成 HE 创建、LIS 单结果 IHC 创建、LIS 多结果二次选择 IHC 创建。
- 查看试剂架、DAB、启动前预检和运行页。
- 验证操作员看不到工程诊断详情，工程师可看诊断，管理员可进入管理页。
- 两个浏览器页面验证状态同步。
- 模拟 SignalR WebSocket 断线，确认重连后重新拉取 `/api/operator/snapshot` 正式快照。

浏览器层不会重复故障、Unknown、重做、复杂 DAB 生命周期等后端测试场景。由于正式 UI 已收敛到单页且当前后端回归未通过，以上浏览器验收也需要在当前 HEAD 重新执行，不能直接沿用历史完成结论。

更多步骤见 [Mock 验收指南](docs/mock-runtime-validation-guide.md)。

## 文档索引

- [项目上下文](project_context/PROJECT_CONTEXT.md)
- [当前 TODO](project_context/TODO.md)
- [项目交接说明](docs/PROJECT_HANDOFF.md)
- [Mock 验收指南](docs/mock-runtime-validation-guide.md)
- [工程师诊断指南](docs/engineer-diagnostics-guide.md)
- [HE 空载干跑前检查表](docs/he-dry-run-preflight-checklist.md)
- [Mock 阶段完成报告](docs/mock-stage-completion-report.md)
- [ASP.NET Core Web Host 说明](docs/aspnet-core-web-host.md)

其中 Mock 完成报告和既有验收指南属于历史阶段证据；判断当前状态时，以当前代码、实际测试结果、`project_context/PROJECT_CONTEXT.md`、`project_context/TODO.md` 和 `git status --short` 为准。
