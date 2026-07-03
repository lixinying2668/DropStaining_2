# Mock 运行端到端验证使用说明

本文用于验证 Stainer ASP.NET Core 本地上位机的管理员、操作员、任务创建、试剂扫码、启动前校验、Mock 运行控制、告警审计、实时同步和服务重启恢复。

适用范围：

- 本地服务：`backend/Stainer.Web`
- 默认地址：`http://127.0.0.1:5205`
- 默认管理员：`admin / 123456`
- 默认操作员：`operator / 123456`

当前版本说明：

- 页面已经能浏览配置、运行、告警、历史、管理等工作台。
- 创建 HE/IHC 业务任务、业务试剂扫码确认、启动前业务校验、故障注入和大步骤重做，当前以 API 验证最稳定。
- `/samples` 页的任务确认按钮目前仍是 Mock toast，不会创建正式 `staining_tasks`。
- `/reagents` 页的“扫描全部试剂架”会更新页面 Mock 状态；业务启动前校验需要调用 `/api/reagents/scan-confirm`。

## 0. 启动服务

在仓库根目录执行：

```powershell
cd D:\Stainer
dotnet run --project backend\Stainer.Web\Stainer.Web.csproj
```

看到监听地址后，浏览器打开：

```text
http://127.0.0.1:5205/
```

健康检查：

```powershell
$base = "http://127.0.0.1:5205"
Invoke-RestMethod "$base/health"
```

预期返回 `ok = true`。

## 1. 准备 Published 流程和试剂目录

进入 `/configure` 前，先确认数据库里至少有：

- 1 个 `WorkflowType = HE` 且版本状态 `Published` 的流程。
- 2 个 `WorkflowType = IHC` 且版本状态 `Published` 的流程。
- 至少 1 个 IHC Published 版本映射到一抗代码 `PA1`，用于创建 IHC 任务。

如果你的 `/configure` 页面已经满足这个条件，可跳过本节。

如果没有，使用下面脚本一次性准备验证数据。它会创建：

- `HE-DEMO`，Published v1.0
- `IHC-DEMO-PA1`，Published v1.0，映射一抗 `PA1`
- `IHC-DEMO-PA2`，Published v1.0，映射一抗 `PA2`
- 试剂目录：`HEM`、`PAB`、`SEC`、`DAB`

先停止正在运行的服务，然后执行：

```powershell
@'
import datetime
import json
import pathlib
import sqlite3
import uuid

db_path = pathlib.Path(r"data\stainer.db")
now = datetime.datetime.utcnow().replace(microsecond=0).isoformat() + "+00:00"

def gid():
    return str(uuid.uuid4())

conn = sqlite3.connect(db_path)
conn.execute("PRAGMA foreign_keys = ON")

def one(sql, args=()):
    return conn.execute(sql, args).fetchone()

def ensure_reagent(code, name, reagent_type):
    row = one("select id from reagent_definitions where reagent_code = ?", (code,))
    if row:
        return row[0]
    rid = gid()
    conn.execute(
        """
        insert into reagent_definitions
        (id, reagent_code, name, reagent_type, minimum_alarm_volume_ul, legacy_metadata_json, is_enabled, created_at_utc)
        values (?, ?, ?, ?, ?, ?, 1, ?)
        """,
        (rid, code, name, reagent_type, 1000, "{}", now),
    )
    return rid

for item in [
    ("HEM", "Hematoxylin", "common"),
    ("PAB", "Primary antibody PA1", "primary"),
    ("SEC", "Secondary antibody", "secondary"),
    ("DAB", "DAB working reagent", "dab"),
]:
    ensure_reagent(*item)

def ensure_workflow(code, name, workflow_type, steps, requirements):
    row = one("select id from workflow_definitions where code = ?", (code,))
    if row:
        definition_id = row[0]
    else:
        definition_id = gid()
        conn.execute(
            """
            insert into workflow_definitions
            (id, code, name, workflow_type, description, is_enabled, created_at_utc)
            values (?, ?, ?, ?, ?, 1, ?)
            """,
            (definition_id, code, name, workflow_type, f"{name} for validation.", now),
        )

    row = one(
        "select id from workflow_versions where workflow_definition_id = ? and version_label = '1.0'",
        (definition_id,),
    )
    if row:
        return row[0]

    version_id = gid()
    conn.execute(
        """
        insert into workflow_versions
        (id, workflow_definition_id, version_no, version_label, status, change_note, published_at_utc, created_at_utc)
        values (?, ?, 1, '1.0', 'Published', 'Seeded for validation.', ?, ?)
        """,
        (version_id, definition_id, now, now),
    )

    for index, step in enumerate(steps, start=1):
        conn.execute(
            """
            insert into workflow_steps
            (id, workflow_version_id, step_no, major_step_code, step_name, action_type, reagent_code,
             volume_ul, duration_seconds, target_temperature_deci_c, mix_parameters_json,
             wash_parameters_json, legacy_parameters_json, failure_strategy, created_at_utc)
            values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'Stop', ?)
            """,
            (
                gid(),
                version_id,
                index,
                step["major"],
                step["name"],
                step["action"],
                step.get("reagent"),
                step.get("volume"),
                step.get("duration", 1),
                420,
                json.dumps({"mixAfter": False}),
                json.dumps({"channelLevel": False}),
                json.dumps(step),
                now,
            ),
        )

    for reagent_code, volume_ul in requirements:
        conn.execute(
            """
            insert into workflow_reagent_requirements
            (id, workflow_version_id, reagent_code, required_volume_ul, is_required, created_at_utc)
            values (?, ?, ?, ?, 1, ?)
            """,
            (gid(), version_id, reagent_code, volume_ul, now),
        )

    return version_id

he_version = ensure_workflow(
    "HE-DEMO",
    "HE validation workflow",
    "HE",
    [
        {"major": "HEMATOXYLIN", "name": "HE hematoxylin", "action": "Dispense", "reagent": "HEM", "volume": 200},
        {"major": "TERMINAL_WASH", "name": "HE terminal wash", "action": "Wash"},
    ],
    [("HEM", 200)],
)

ihc_pa1_version = ensure_workflow(
    "IHC-DEMO-PA1",
    "IHC validation workflow PA1",
    "IHC",
    [
        {"major": "PRIMARY_ANTIBODY", "name": "Primary antibody", "action": "Dispense", "reagent": "PAB", "volume": 100},
        {"major": "SECONDARY_ANTIBODY", "name": "Secondary antibody", "action": "Dispense", "reagent": "SEC", "volume": 100},
        {"major": "DAB", "name": "DAB", "action": "Dab", "reagent": "DAB", "volume": 100},
        {"major": "HEMATOXYLIN", "name": "Hematoxylin counterstain", "action": "Dispense", "reagent": "HEM", "volume": 100},
    ],
    [("PAB", 100), ("SEC", 100), ("DAB", 100), ("HEM", 100)],
)

ihc_pa2_version = ensure_workflow(
    "IHC-DEMO-PA2",
    "IHC validation workflow PA2",
    "IHC",
    [
        {"major": "PRIMARY_ANTIBODY", "name": "Primary antibody PA2", "action": "Dispense", "reagent": "PAB", "volume": 100},
        {"major": "SECONDARY_ANTIBODY", "name": "Secondary antibody", "action": "Dispense", "reagent": "SEC", "volume": 100},
        {"major": "DAB", "name": "DAB", "action": "Dab", "reagent": "DAB", "volume": 100},
    ],
    [("PAB", 100), ("SEC", 100), ("DAB", 100)],
)

def ensure_mapping(primary_antibody_code, workflow_version_id):
    row = one(
        """
        select id from primary_antibody_workflow_mappings
        where primary_antibody_code = ? and workflow_version_id = ?
        """,
        (primary_antibody_code, workflow_version_id),
    )
    if row:
        return
    conn.execute(
        """
        insert into primary_antibody_workflow_mappings
        (id, primary_antibody_code, workflow_version_id, is_enabled, created_at_utc)
        values (?, ?, ?, 1, ?)
        """,
        (gid(), primary_antibody_code, workflow_version_id, now),
    )

ensure_mapping("PA1", ihc_pa1_version)
ensure_mapping("PA2", ihc_pa2_version)

conn.commit()
conn.close()
print("Validation workflows and reagent catalog are ready.")
'@ | .\venv\Scripts\python.exe -
```

重新启动服务后确认：

```powershell
$base = "http://127.0.0.1:5205"
$workflows = Invoke-RestMethod "$base/api/workflows"
$workflows |
  Select-Object code, workflowType, @{Name="versions";Expression={($_.versions | ForEach-Object { "$($_.versionLabel):$($_.status)" }) -join ","}}
```

在 `/configure` 页面也应看到 `HE-DEMO`、`IHC-DEMO-PA1`、`IHC-DEMO-PA2`，且最新版本显示为已发布。

## 2. API 会话初始化

后续 API 命令用两个会话：

```powershell
$base = "http://127.0.0.1:5205"

function New-CommandId([string]$prefix) {
  "$prefix-$([guid]::NewGuid().ToString('N'))"
}

$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$operatorSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$json = "application/json"
```

管理员登录 API 会话：

```powershell
Invoke-RestMethod "$base/api/login" `
  -Method Post `
  -ContentType $json `
  -WebSession $adminSession `
  -Body (@{ username = "admin"; password = "123456"; role = "admin" } | ConvertTo-Json)
```

## 3. 验证 1：管理员创建操作员账号

### 页面方式

1. 浏览器打开 `/`。
2. 选择管理员，用户名 `admin`，密码 `123456`。
3. 登录后进入 `/admin`。
4. 点击“新增用户”。
5. 输入用户名、显示名、初始密码和角色。角色填 `operator`。
6. 页面用户表出现新用户，启用列为“是”。

### API 方式

```powershell
$operatorUsername = "opqa$(Get-Date -Format 'HHmmss')"
$operatorPassword = "123456"

$createdUser = Invoke-RestMethod "$base/api/users" `
  -Method Post `
  -ContentType $json `
  -WebSession $adminSession `
  -Body (@{
    commandId = New-CommandId "user-create"
    username = $operatorUsername
    displayName = "QA Operator"
    password = $operatorPassword
    roles = @("operator")
  } | ConvertTo-Json)

$createdUser
```

预期：

- `ok = true`
- `username = $operatorUsername`
- `roles` 包含 `operator`

如果返回 `username_exists`，换一个用户名再执行。

## 4. 验证 2：操作员登录

页面方式：

1. 打开新浏览器窗口或退出当前账号。
2. 选择操作员。
3. 用户名填刚创建的 `$operatorUsername`，密码填 `$operatorPassword`。
4. 登录后左下角用户卡应显示该操作员。

API 方式：

```powershell
Invoke-RestMethod "$base/api/login" `
  -Method Post `
  -ContentType $json `
  -WebSession $operatorSession `
  -Body (@{
    username = $operatorUsername
    password = $operatorPassword
    role = "operator"
  } | ConvertTo-Json)

Invoke-RestMethod "$base/api/current-user" -WebSession $operatorSession
```

预期当前用户的 `activeRole` 为 `operator`。

## 5. 验证 3：创建 1 个 HE 任务

当前正式 HE 任务创建使用 API。

先取 Published HE 版本：

```powershell
$workflows = Invoke-RestMethod "$base/api/workflows"
$heWorkflow = $workflows | Where-Object { $_.workflowType -eq "HE" } | Select-Object -First 1
$heVersion = $heWorkflow.versions | Where-Object { $_.status -eq "Published" } | Select-Object -First 1
$heVersion.id
```

创建 HE 任务到 `A-01`：

```powershell
$heTask = Invoke-RestMethod "$base/api/tasks/he" `
  -Method Post `
  -ContentType $json `
  -WebSession $operatorSession `
  -Body (@{
    commandId = New-CommandId "task-he"
    workflowVersionId = $heVersion.id
    slotCode = "A-01"
  } | ConvertTo-Json)

$heTask
```

预期：

- `ok = true`
- `taskId` 有值
- `taskCode` 以 `HE-` 开头

如果返回 `slot_not_idle`，说明该槽位已有 Confirmed 任务，换成 `B-01`、`C-01` 等空槽位。

## 6. 验证 4：创建 1 个 IHC 任务

当前正式 IHC 任务创建使用 API。

取 `IHC-DEMO-PA1` 的 Published 版本：

```powershell
$workflows = Invoke-RestMethod "$base/api/workflows"
$ihcWorkflow = $workflows | Where-Object { $_.code -eq "IHC-DEMO-PA1" } | Select-Object -First 1
if (-not $ihcWorkflow) {
  $ihcWorkflow = $workflows | Where-Object { $_.workflowType -eq "IHC" } | Select-Object -First 1
}
$ihcVersion = $ihcWorkflow.versions | Where-Object { $_.status -eq "Published" } | Select-Object -First 1
$ihcVersion.id
```

创建 IHC 任务到 `A-02`，一抗代码用 `PA1`：

```powershell
$ihcTask = Invoke-RestMethod "$base/api/tasks/ihc" `
  -Method Post `
  -ContentType $json `
  -WebSession $operatorSession `
  -Body (@{
    commandId = New-CommandId "task-ihc"
    inputMode = "DirectPrimaryAntibody"
    rawCode = "PA1"
    slotCode = "A-02"
    selectedPrimaryAntibodyCode = $null
    selectedWorkflowVersionId = $ihcVersion.id
  } | ConvertTo-Json)

$ihcTask
```

预期：

- `ok = true`
- `taskId` 有值
- `taskCode` 以 `IHC-` 开头

如果返回 `ihc_workflow_not_found`，检查 `primary_antibody_workflow_mappings` 是否有 `PA1 -> IHC Published version` 映射。

## 7. 验证 5：完成 Mock 试剂扫码

业务启动前校验需要调用 `/api/reagents/scan-confirm`。下面扫码 4 个有效试剂，其余架位自动记为空位。

条码规则：前 3 位是试剂代码，接着 3 位是 0.1mL 数量单位，接着 8 位批号，最后 3 位序号。例如 `HEM50020260625001` 表示 `HEM`，50.0mL。

```powershell
$scan = Invoke-RestMethod "$base/api/reagents/scan-confirm" `
  -Method Post `
  -ContentType $json `
  -WebSession $operatorSession `
  -Body (@{
    commandId = New-CommandId "scan-confirm"
    items = @(
      @{ position = "R1"; scanResult = "VALID"; rawBarcode = "HEM50020260625001"; locatorCode = "R1"; expirationDate = "2027-12-31" }
      @{ position = "R2"; scanResult = "VALID"; rawBarcode = "PAB50020260625001"; locatorCode = "R2"; expirationDate = "2027-12-31" }
      @{ position = "R3"; scanResult = "VALID"; rawBarcode = "SEC50020260625001"; locatorCode = "R3"; expirationDate = "2027-12-31" }
      @{ position = "R4"; scanResult = "VALID"; rawBarcode = "DAB50020260625001"; locatorCode = "R4"; expirationDate = "2027-12-31" }
    )
  } | ConvertTo-Json -Depth 5)

$scan
```

预期：

- `validCount = 4`
- `invalidCount = 0`
- `emptyCount = 36`

页面辅助验证：

1. 打开 `/reagents`。
2. 点击“扫描全部试剂架”可验证页面 Mock 扫码展示。
3. 业务 API 扫码后的数据库架位可通过 `/api/reagents/rack` 查看。

```powershell
Invoke-RestMethod "$base/api/reagents/rack" | Where-Object { $_.bottle -ne $null } |
  Select-Object position, @{Name="reagent";Expression={$_.bottle.reagentCode}}, @{Name="volume";Expression={$_.bottle.remainingVolumeUl}}
```

## 8. 验证 6：执行启动前校验

调用业务预检：

```powershell
$preflight = Invoke-RestMethod "$base/api/run/preflight" -WebSession $operatorSession
$preflight
```

预期：

- `ok = true`
- `taskCount = 2`
- `issueCount = 0`

常见失败：

- `no_confirmed_tasks`：没有通过 API 创建 Confirmed HE/IHC 任务。
- `workflow_version_not_published`：任务引用了 Draft 或 Retired 版本。
- `scan_missing`：没有调用 `/api/reagents/scan-confirm`。
- `scan_has_invalid_items`：最新扫码里有 INVALID。
- `required_reagent_missing`：流程所需试剂没有在架。
- `required_reagent_volume_insufficient`：余量不足。

## 9. 验证 7：启动 Mock 运行

先创建 MachineRun：

```powershell
$run = Invoke-RestMethod "$base/api/runs" `
  -Method Post `
  -ContentType $json `
  -WebSession $operatorSession `
  -Body (@{
    commandId = New-CommandId "run-create"
    stainingTaskIds = @($heTask.taskId, $ihcTask.taskId)
  } | ConvertTo-Json)

$runId = $run.runId
$run
```

预期：

- `ok = true`
- `status = Created`
- `runId` 有值

启动运行：

```powershell
Invoke-RestMethod "$base/api/runs/$runId/start" `
  -Method Post `
  -ContentType $json `
  -WebSession $operatorSession `
  -Body (@{ commandId = New-CommandId "run-start" } | ConvertTo-Json)
```

查询运行：

```powershell
Start-Sleep -Milliseconds 100
Invoke-RestMethod "$base/api/runs/$runId" -WebSession $operatorSession
```

页面辅助验证：

- 打开 `/run`。
- 通道 A 应出现 `A-01`、`A-02` 两个 slide task。
- 状态会从待启动进入运行中，并快速推进进度。

## 10. 验证 8：测试暂停、恢复

Mock 执行器非常快。要稳定验证暂停，请在启动后立即发送 pause。

```powershell
Invoke-RestMethod "$base/api/runs/$runId/pause" `
  -Method Post `
  -ContentType $json `
  -WebSession $operatorSession `
  -Body (@{ commandId = New-CommandId "run-pause" } | ConvertTo-Json)

Start-Sleep -Milliseconds 200
$paused = Invoke-RestMethod "$base/api/runs/$runId" -WebSession $operatorSession
$paused.status
```

预期：

- 状态为 `Paused`，或页面显示“暂停”。

恢复：

```powershell
Invoke-RestMethod "$base/api/runs/$runId/resume" `
  -Method Post `
  -ContentType $json `
  -WebSession $operatorSession `
  -Body (@{ commandId = New-CommandId "run-resume" } | ConvertTo-Json)

Start-Sleep -Milliseconds 100
$resumed = Invoke-RestMethod "$base/api/runs/$runId" -WebSession $operatorSession
$resumed.status
```

预期：

- 状态回到 `Running`，或很快进入后续状态。

如果已经 `Completed`，说明 Mock 跑完了。重新在 `B-01`、`B-02` 创建一组任务，再创建新的 run 重测。

## 11. 验证 9：注入故障

故障注入需要管理员或工程师权限。这里用管理员会话。

建议在恢复后尽快执行：

```powershell
Invoke-RestMethod "$base/api/runs/$runId/fault" `
  -Method Post `
  -ContentType $json `
  -WebSession $adminSession `
  -Body (@{
    commandId = New-CommandId "run-fault"
    message = "QA injected fault"
  } | ConvertTo-Json)

Start-Sleep -Milliseconds 200
$faulted = Invoke-RestMethod "$base/api/runs/$runId" -WebSession $adminSession
$faulted.status
$faulted.alarms
```

预期：

- `status = Faulted`
- `alarms` 中出现故障相关记录。
- `/alerts` 页面出现告警。
- `/run` 页面状态显示故障或错误。

如果状态已经 `Completed`，重新创建新 run，并在 start 后立即 fault。

## 12. 验证 10：测试大步骤重做

大步骤重做也需要管理员或工程师权限，且 run 必须处于 `Faulted`。

```powershell
Invoke-RestMethod "$base/api/runs/$runId/redo-current-major-step" `
  -Method Post `
  -ContentType $json `
  -WebSession $adminSession `
  -Body (@{
    commandId = New-CommandId "run-redo"
    reason = "QA redo after injected fault"
  } | ConvertTo-Json)

Start-Sleep -Milliseconds 200
$afterRedo = Invoke-RestMethod "$base/api/runs/$runId" -WebSession $adminSession
$afterRedo.status
$afterRedo.workflowExecutions |
  ForEach-Object { $_.steps } |
  Where-Object { $_.redoCount -gt 0 } |
  Select-Object stepNo, majorStepCode, stepName, status, redoCount
```

预期：

- 故障大步骤里的 step `redoCount > 0`。
- run 从 `Faulted` 变为 `Running`，之后继续执行或完成。
- 审计表有 `run.redo_major_step`。

## 13. 验证 11：查看告警、历史、审计

页面方式：

- `/alerts`：查看当前 run 告警。
- `/history`：查看页面汇总的任务、试剂和日志。
- `/admin`：查看用户和最近日志入口。
- `/run`：查看当前 run 状态、通道、步骤日志。

API 查看 run 告警：

```powershell
Invoke-RestMethod "$base/api/runs/$runId" -WebSession $adminSession |
  Select-Object runCode, status, currentMajorStepCode, alarms
```

查看页面状态聚合：

```powershell
Invoke-RestMethod "$base/api/state" -WebSession $adminSession |
  Select-Object runId, status, alarms, logs
```

审计记录目前没有专门的读取 API，权威验证可查 SQLite：

```powershell
@'
import sqlite3
conn = sqlite3.connect(r"data\stainer.db")
for row in conn.execute("""
    select created_at_utc, action, entity_type, entity_id, message
    from audit_logs
    order by created_at_utc desc
    limit 30
"""):
    print(" | ".join("" if v is None else str(v) for v in row))
conn.close()
'@ | .\venv\Scripts\python.exe -
```

重点确认这些动作：

- `auth.login`
- `user.create`
- `task.create_he`
- `task.create_ihc`
- `reagent.scan_confirm`
- `run.create`
- `run.reagent_consumption`
- `run.redo_major_step`

命令幂等记录可查 `command_receipts`：

```powershell
@'
import sqlite3
conn = sqlite3.connect(r"data\stainer.db")
for row in conn.execute("""
    select created_at_utc, operation, entity_type, entity_id, status
    from command_receipts
    order by created_at_utc desc
    limit 30
"""):
    print(" | ".join("" if v is None else str(v) for v in row))
conn.close()
'@ | .\venv\Scripts\python.exe -
```

## 14. 验证 12：双浏览器实时同步

准备两个浏览器上下文：

- 浏览器 A：普通 Chrome/Edge，登录操作员或管理员，打开 `/run`。
- 浏览器 B：无痕窗口或另一个浏览器，登录另一个账号，打开 `/run`。

验证动作：

1. 两边都停留在 `/run`。
2. 在 PowerShell 或浏览器 A 发送 start、pause、resume、fault、redo 命令。
3. 观察浏览器 B：
   - 顶部状态 chip 应变化。
   - 通道进度应变化。
   - 发生故障时，告警横幅或 `/alerts` 内容应更新。
   - 页面日志应出现新事件。

实时同步依赖 WebSocket：

```text
/hubs/machine
```

如果浏览器 B 没有变化：

- 刷新浏览器 B，确认 `/api/state` 能返回最新 run。
- 打开浏览器开发者工具 Network，检查 WebSocket 是否连接。
- 确认服务不是刚刚重启导致 in-memory WebSocket 连接断开。

## 15. 验证 13：重启服务验证状态恢复

记录当前 run：

```powershell
$beforeRestart = Invoke-RestMethod "$base/api/runs/current" -WebSession $adminSession
$beforeRestart.id
$beforeRestart.status
```

停止服务：

- 如果在终端前台运行，按 `Ctrl+C`。
- 如果后台运行，先确认进程，再停止：

```powershell
Get-Process dotnet
Stop-Process -Id <pid>
```

重新启动：

```powershell
dotnet run --project backend\Stainer.Web\Stainer.Web.csproj
```

重新登录 API 会话，因为 cookie 是进程内 session：

```powershell
$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod "$base/api/login" `
  -Method Post `
  -ContentType $json `
  -WebSession $adminSession `
  -Body (@{ username = "admin"; password = "123456"; role = "admin" } | ConvertTo-Json)
```

查询恢复后的当前 run：

```powershell
$afterRestart = Invoke-RestMethod "$base/api/runs/current" -WebSession $adminSession
$afterRestart.id
$afterRestart.status
```

预期：

- `id` 与重启前一致，说明最新 run 从 SQLite 恢复。
- `/api/state` 仍能显示同一个 `runId`。
- 如果重启前是 `Faulted`，重启后仍为 `Faulted`，需要调用大步骤重做继续。
- 如果重启前是 `Paused`，重启后仍为 `Paused`，需要调用 resume 继续。
- 如果重启前是 `Completed`，重启后仍为 `Completed`。

注意：内存中的 pending command 和 pause/fault flag 不会跨进程恢复，恢复的是数据库中已经持久化的 run、step、alarm、consumption、audit 状态。

## 16. 常见问题速查

`401 authentication_required`

- 当前 PowerShell session 没登录，重新调用 `/api/login`。

`403 forbidden`

- 操作权限不对。故障注入和重做用 admin/engineer，普通启动暂停恢复用 operator/admin。

`workflow_version_not_published`

- 任务引用的 workflow version 不是 Published。
- 重新执行第 1 节准备数据，或在 `/configure` 确认版本状态。

`ihc_workflow_not_found`

- 一抗代码没有映射到 IHC Published workflow。
- 本文默认用 `PA1`，确认存在 `PA1 -> IHC-DEMO-PA1` 映射。

`slot_not_idle`

- 同一物理槽位已有 Confirmed 任务。
- 换槽位，如 `B-01`、`B-02`。

`active_run_exists`

- 已存在 Created、Running、Paused 或 Faulted run。
- 先完成、停止、重做恢复该 run，或使用新的测试数据库。

`scan_has_invalid_items`

- 最新一次扫码有 INVALID。
- 重新调用 `/api/reagents/scan-confirm`，确保所有非空条码都能被 parser 解析，并且前 3 位试剂代码在 `reagent_definitions` 存在。

Mock 运行太快，来不及点暂停或故障

- 使用本文 PowerShell 命令在 start 后立即发送 pause/fault。
- 手工页面验证时，可以创建更多任务增加步骤数，或临时调大 `MachineExecutor` 的 `mockDelay`。
# 2026-07-03 Mock 阶段收口验收说明

本节为当前有效说明。下方旧内容保留为历史手工记录，若与本节冲突，以本节为准。

## 当前自动化浏览器验收

运行前先构建 Release：

```powershell
dotnet build Stainer.sln --configuration Release
npm install
npm run test:browser
```

浏览器验收脚本：

```text
tools/browser-acceptance/mock-stage-acceptance.mjs
```

脚本使用 `playwright-core` 和系统 Chrome/Edge，固定 1920×1080 视口，并覆盖：

- 操作员建立 HE 通道并创建 HE 样本；
- 操作员建立 IHC 通道，完成 LIS 单结果 IHC 创建；
- 操作员完成 LIS 多结果候选选择后创建 IHC 样本；
- 查看试剂架、DAB、启动前预检和运行页；
- 操作员不可见工程诊断详情，工程师可看诊断，管理员可进管理页；
- 两个浏览器页面状态同步；
- 模拟 SignalR WebSocket 断线重连，确认页面重新拉取 `/api/operator/snapshot` 正式快照。

脚本启动独立后端实例，显式隔离：

- `STAINER_DATABASE_URL=Data Source=<temp>\db\stainer-browser.db`
- `MachineExecutor__LeasePath=<temp>\locks\machine-executor.lock`
- `Safety__LogDirectory=<temp>\logs`
- `Database__BackupDirectory=<temp>\backups`

默认结束后清理临时目录，不写 `data/stainer.db`、正式备份或正式运行日志。需要排查时可设置：

```powershell
$env:STAINER_KEEP_BROWSER_TEMP = "1"
npm run test:browser
```

## 当前边界

- 浏览器层不重复验证故障、Unknown、重做、复杂 DAB 生命周期等后端已覆盖场景。
- 正式页面不得调用旧 `/api/state`、旧 `/api/run/start|pause|resume|stop` 或旧 `/api/dab` 作为业务权威来源。
- 旧 `/api/state`、旧 `/api/run/start|pause|resume|stop` 仅 Development/Testing 映射；Staging/Production 不映射。
- `/control-console` 仅 Development/Testing 可用，不进入正式导航。

完整阶段报告见 `docs/mock-stage-completion-report.md`；工程诊断说明见 `docs/engineer-diagnostics-guide.md`；HE 空载干跑前检查见 `docs/he-dry-run-preflight-checklist.md`。
