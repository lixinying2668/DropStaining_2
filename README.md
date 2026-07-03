# Stainer 上位机（ASP.NET Core Mock 阶段）

本仓库当前正式主线是 `backend/Stainer.Web`：ASP.NET Core + SQLite + 原生 HTML/CSS/JavaScript HMI。`src/` 目录仍保留早期 FastAPI/Jinja 原型作为参考，不作为正式运行入口。

## 当前阶段边界

- 已完成 Mock 阶段正式页面接入：操作员、试剂/DAB、运行、告警/历史、工程、管理页面均读取正式 API 和 SQLite 数据。
- Mock 设备适配器用于本地验证，工程测试入口保留 Mock 标识；`Device:Mode=Real` 仍保持 fail-closed。
- 未接真实硬件，不改变数据库模型、核心业务规则或最终视觉。
- 旧 `/api/state`、旧 `/api/run/start|pause|resume|stop` 仅在 Development/Testing 环境映射；Staging/Production 不可用。
- `/control-console` 仅 Development/Testing 可用，不进入正式导航。

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
Get-ChildItem backend\Stainer.Web\wwwroot\static\js\*.js,backend\Stainer.Web\wwwroot\static\control-console\*.js | ForEach-Object { node --check $_.FullName }
dotnet ef migrations has-pending-model-changes --project backend\Stainer.Web\Stainer.Web.csproj --startup-project backend\Stainer.Web\Stainer.Web.csproj --configuration Release --no-build
git diff --check
```

`npm run test:browser` 会启动一个临时后端实例，使用独立临时 SQLite、执行器锁、日志和备份目录；脚本结束后默认清理临时目录，不会写 `data/stainer.db`、正式备份或正式运行日志。

## Mock 阶段浏览器验收

浏览器验收入口：

```powershell
npm install
npm run test:browser
```

脚本使用 `playwright-core` 和系统 Chrome/Edge，默认 1920×1080、headless。可用 `PLAYWRIGHT_CHROME_EXECUTABLE` 指定浏览器路径。

覆盖范围：

- 操作员在页面上建立 HE 和 IHC 通道。
- 完成 HE 创建、LIS 单结果 IHC 创建、LIS 多结果二次选择 IHC 创建。
- 查看试剂架、DAB、启动前预检和运行页。
- 验证操作员看不到工程诊断详情，工程师可看诊断，管理员可进入管理页。
- 两个浏览器页面验证状态同步。
- 模拟 SignalR WebSocket 断线，确认重连后重新拉取 `/api/operator/snapshot` 正式快照。

浏览器层不会重复故障、Unknown、重做、复杂 DAB 生命周期等后端已有覆盖场景。

更多步骤见 [Mock 验收指南](docs/mock-runtime-validation-guide.md)。

## 文档索引

- [Mock 验收指南](docs/mock-runtime-validation-guide.md)
- [工程师诊断指南](docs/engineer-diagnostics-guide.md)
- [HE 空载干跑前检查表](docs/he-dry-run-preflight-checklist.md)
- [Mock 阶段完成报告](docs/mock-stage-completion-report.md)
- [ASP.NET Core Web Host 说明](docs/aspnet-core-web-host.md)
