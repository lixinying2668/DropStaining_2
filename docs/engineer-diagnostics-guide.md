# 工程师诊断指南

本指南说明 Mock 阶段工程诊断页的使用边界。工程诊断页面用于查看技术细节和执行受控 Mock 诊断；真实硬件接入前，Real 模式继续 fail-closed。

## 1. 权限与入口

- 工程诊断入口：`/engineer`
- 允许角色：`engineer`、`admin`
- 操作员不可访问工程诊断 API，不显示工程导航。
- 工程写操作需要二次认证、原因、目标和危险操作确认。

工程师 API 登录示例：

```powershell
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod "http://127.0.0.1:5205/api/login" `
  -Method Post `
  -ContentType "application/json" `
  -WebSession $session `
  -Body (@{ username = "engineer"; password = "123456"; role = "engineer" } | ConvertTo-Json)
```

## 2. 可见诊断内容

工程师/管理员可查看：

- 设备模式和 Real fail-closed 状态。
- Mock 适配器初始化与设备状态。
- 命令记录、命令阶段、错误码。
- Mock 通讯记录、请求/响应报文、持久化状态。
- 坐标配置版本、Liquid Class 版本、差异、导入导出。
- 数据库维护、备份、恢复请求审计。

这些技术细节不得出现在操作员顶部告警、告警列表、告警详情、运行页日志或操作员 CSV 中。

## 3. Mock 与 Real 边界

Mock 模式：

- 工程诊断可执行 Mock 初始化和适配器测试。
- Mock 通讯记录明确标识 Mock。
- 测试入口只写正式诊断、命令、审计和 Mock 通讯数据，不写旧 `MockRuntimeStore` 作为业务权威状态。

Real 模式：

- 在真实适配器、健康检查和联锁条件未完成前，命令 fail-closed。
- 不允许用 Mock 成功结果代替真实硬件确认。
- 只读联调前只允许读取真实适配器状态，不允许驱动运动、加液、加热或扫码动作。

## 4. 诊断操作顺序

建议顺序：

1. 确认当前 `Device:Mode`。
2. 查看 `/api/device-mode` 和工程页设备状态。
3. 查看命令记录，确认没有未解释的 Failed/Unknown。
4. 查看错误码和 Mock 通讯记录。
5. 查看坐标版本和 Liquid Class 当前启用版本。
6. 需要写操作时，先建立工程二次认证会话，再执行配置导入、启停用或诊断命令。
7. 写操作完成后导出 CSV 或 JSON 归档，并检查审计记录。

## 5. 排查要点

- `401 authentication_required`：重新登录。
- `403 forbidden`：当前角色不是 engineer/admin。
- `engineering_write_session_required`：未完成工程二次认证。
- Real fail-closed 响应：真实适配器尚未满足安全条件，不能绕过。
- 命令记录存在 `Unknown`：按启动恢复策略处理，不得自动标记为完成。

## 6. 验收证据

本阶段自动化回归覆盖工程/管理员权限、工程诊断页可读、操作员不可见工程技术详情、CSV 和历史追溯、正式页面不依赖旧 Mock 写接口、Mock 标识保留和 Real fail-closed。
