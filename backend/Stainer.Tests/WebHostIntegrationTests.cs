using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class WebHostIntegrationTests
{
    [Fact]
    public async Task Web_host_serves_pages_static_assets_health_api_and_fallback()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var info = await client.GetFromJsonAsync<SystemInfoResponse>("/api/system/info");
        Assert.NotNull(info);
        Assert.False(info!.PythonRuntimeRequired);
        Assert.Equal("ASP.NET Core", info.UiHost);

        foreach (var route in new[] { "/", "/control-console", "/dashboard", "/samples", "/reagents", "/run", "/alerts", "/alarms", "/history", "/configure", "/engineer", "/admin", "/management", "/mock-timeline" })
        {
            var html = await client.GetStringAsync(route);
            Assert.Contains("app.css", html);
            Assert.DoesNotContain("{%", html);
            Assert.DoesNotContain("{{", html);
        }

        var login = await client.GetStringAsync("/");
        Assert.Contains("role-picker two-roles", login);
        Assert.Contains("value=\"operator\"", login);
        Assert.Contains("value=\"admin\"", login);
        Assert.DoesNotContain("value=\"engineer\"", login);
        Assert.DoesNotContain("ENG", login);

        var dashboard = await client.GetStringAsync("/dashboard");
        Assert.Contains("app-shell", dashboard);
        Assert.Contains("drawerBoard", dashboard);
        Assert.Contains(">检查<", dashboard);
        Assert.Contains("data-href=\"/control-console\"", dashboard);
        Assert.Contains("current-page-card", dashboard);
        Assert.Contains("currentPageLabel", dashboard);
        Assert.Contains("userMenu", dashboard);
        Assert.Contains("id=\"logoutButton\"", dashboard);
        Assert.Contains("退出登录", dashboard);
        Assert.DoesNotContain("onclick=\"toggleUserMenu", dashboard);
        Assert.Contains("<i>06</i><span>告警</span>", dashboard);
        Assert.Contains("<i>07</i><span>历史</span>", dashboard);
        Assert.DoesNotContain("top-panel", dashboard);
        Assert.DoesNotContain("workflow-strip v18-flow", dashboard);
        Assert.DoesNotContain("kpiInit", dashboard);
        Assert.DoesNotContain("dashboardLogs", dashboard);

        var controlConsole = await client.GetStringAsync("/control-console");
        Assert.Contains("app-shell", controlConsole);
        Assert.Contains("controlConsoleFrame", controlConsole);
        Assert.Contains("/static/control-console/index.html?v=20260702-r19-patch1", controlConsole);
        Assert.DoesNotContain("top-panel", controlConsole);

        var mockTimeline = await client.GetStringAsync("/mock-timeline");
        Assert.Contains("mockGanttBoard", mockTimeline);
        Assert.Contains("/static/js/mock-timeline.js", mockTimeline);

        var css = await client.GetAsync("/static/css/app.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Contains("text/css", css.Content.Headers.ContentType?.MediaType);

        var js = await client.GetAsync("/static/js/api.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        var apiScript = await js.Content.ReadAsStringAsync();
        Assert.Contains("initializeUserMenu", apiScript);
        Assert.Contains("logoutButton.addEventListener('click'", apiScript);

        var consoleCss = await client.GetAsync("/static/control-console/enhancement.css");
        Assert.Equal(HttpStatusCode.OK, consoleCss.StatusCode);

        var consoleJs = await client.GetAsync("/static/control-console/enhancement.js");
        Assert.Equal(HttpStatusCode.OK, consoleJs.StatusCode);

        var consolePage = await client.GetStringAsync("/static/control-console/index.html");
        Assert.Contains("twinSvg", consolePage);
        Assert.Contains("embedded-host", consolePage);
        Assert.Contains("/static/control-console/enhancement.js", consolePage);

        var fallback = await client.GetStringAsync("/kiosk/unknown");
        Assert.Contains("drawerBoard", fallback);
    }

    [Fact]
    public async Task Samples_page_uses_channel_batch_api_without_local_storage_business_state()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/samples");
        var hostScript = await client.GetStringAsync("/static/js/stainer-host.js");

        Assert.Contains("选择实验类型", html);
        Assert.Contains("使用当前默认 HE 流程", html);
        Assert.Contains("使用当前默认 IHC 流程", html);
        Assert.Contains("确认创建任务", html);
        Assert.DoesNotContain("统一选择脚本", html);
        Assert.DoesNotContain("channelScriptSelect", html);
        Assert.DoesNotContain("WorkflowVersionId", html);
        Assert.DoesNotContain("confirmMockTask", html);
        Assert.DoesNotContain("localStorage", html);

        Assert.DoesNotContain("localStorage", hostScript);
        Assert.DoesNotContain("channelWorkflowSelections", hostScript);
        Assert.DoesNotContain("/api/samples/scan", hostScript);
        Assert.Contains("/api/channel-batches/active", hostScript);
        Assert.Contains("/api/channel-batches/experiment-type-selection", hostScript);
        Assert.DoesNotContain("/api/channel-batches/workflow-selection", hostScript);
        Assert.Contains("/api/tasks/he", hostScript);
        Assert.Contains("/api/tasks/ihc", hostScript);
        Assert.Contains("/api/lis/mock-query", hostScript);
        Assert.Contains("channelBatch.changed", hostScript);
        Assert.Contains("slideTask.created", hostScript);
        Assert.Contains("继承通道脚本", hostScript);
        Assert.Contains("未选实验类型，禁止添加", hostScript);
    }

    [Fact]
    public async Task Reagents_page_reads_formal_api_without_mock_or_local_storage_business_state()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/reagents");
        var hostScript = await client.GetStringAsync("/static/js/stainer-host.js");

        Assert.Contains("扫码会话", html);
        Assert.Contains("开始扫码", html);
        Assert.Contains("完成扫码", html);
        Assert.Contains("reagentDeck", html);
        Assert.DoesNotContain("Mock:", html);
        Assert.DoesNotContain("localStorage", html);

        Assert.Contains("/api/reagents/rack", hostScript);
        Assert.Contains("/api/reagents/scan-sessions/overview", hostScript);
        Assert.Contains("/api/reagents/scan-sessions/start", hostScript);
        Assert.Contains("completeReagentScanSession", hostScript);
        Assert.Contains("/api/reagents/scan-confirm", hostScript);
        Assert.Contains("beginReagentScanGuide", hostScript);
        Assert.Contains("scanState", hostScript);
        Assert.Contains("reagent.bottleDepleted", hostScript);
        Assert.DoesNotContain("localStorage", hostScript);
        Assert.DoesNotContain("/api/reagents/scan'", hostScript);
        Assert.DoesNotContain("/api/reagents/scan\"", hostScript);
        Assert.DoesNotContain("CreateReagent(", hostScript);
    }

    [Fact]
    public async Task Operator_pages_use_formal_snapshot_dab_and_run_contracts()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var dashboard = await client.GetStringAsync("/dashboard");
        var samples = await client.GetStringAsync("/samples");
        var reagents = await client.GetStringAsync("/reagents");
        var run = await client.GetStringAsync("/run");
        var hostScript = await client.GetStringAsync("/static/js/stainer-host.js");
        var runScript = await client.GetStringAsync("/static/js/run.js");

        Assert.Contains("dashboardEvents", dashboard);
        Assert.Contains("dashboardEventModal", dashboard);
        Assert.Contains("openDashboardEventList()", dashboard);
        Assert.DoesNotContain("operator-event-row", dashboard);
        Assert.DoesNotContain("DAB 临时配液区", dashboard);
        Assert.Contains("channelScriptModal", samples);
        Assert.DoesNotContain("单玻片流程", samples);
        Assert.Contains("dabPositionGrid", reagents);
        Assert.Contains("dabCleaningConfirmButton", reagents);
        Assert.Contains("runResourceGrid", run);
        Assert.Contains("runNeedles", run);
        Assert.Contains("runCommandStages", run);
        Assert.Contains("启动前预检", run);

        Assert.Contains("/api/operator/snapshot", hostScript);
        Assert.Contains("/api/dab/batches/", hostScript);
        Assert.DoesNotContain("/api/state", hostScript);
        Assert.Contains("/api/operator/snapshot", runScript);
        Assert.Contains("/api/runs/", runScript);
        Assert.Contains("operatorConfirm", runScript);
        Assert.DoesNotContain("MockDeviceState", runScript);
        Assert.DoesNotContain("状态哈希", runScript);
        Assert.DoesNotContain("confirm(", runScript);
        Assert.DoesNotContain("prompt(", runScript);
        Assert.DoesNotContain("alert(", runScript);
        Assert.DoesNotContain("/api/state", runScript);
        Assert.DoesNotContain("/api/run/start", runScript);
    }

    [Fact]
    public async Task Operator_snapshot_projects_sqlite_needles_dab_and_audit_data()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/login", new
        {
            username = "operator",
            password = "123456",
            role = "operator"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var needle = await dbContext.NeedleStates.SingleOrDefaultAsync(x => x.NeedleCode == NeedleCodes.Needle1)
                ?? new NeedleState
                {
                    NeedleCode = NeedleCodes.Needle1,
                    NeedleNo = 1,
                    Status = MotionStatuses.Idle
                };
            if (dbContext.Entry(needle).State == EntityState.Detached)
            {
                dbContext.NeedleStates.Add(needle);
            }
            needle.LoadedSourceType = NeedleLoadSourceTypes.ReagentBottle;
            needle.LoadedReagentCode = "UI-FORMAL-REAGENT";
            needle.VolumeUl = 135;
            needle.NeedsWash = true;
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "operator.ui.formal-snapshot",
                EntityType = "OperatorUi",
                Message = "Formal operator snapshot integration test"
            });
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/operator/snapshot");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(4, root.GetProperty("channels").GetArrayLength());
        Assert.Equal(8, root.GetProperty("dabPositions").GetArrayLength());
        Assert.Equal(2, root.GetProperty("needles").GetArrayLength());
        Assert.Equal("operator", root.GetProperty("activeUser").GetProperty("role").GetString());
        var projectedNeedle = root.GetProperty("needles")
            .EnumerateArray()
            .Single(x => x.GetProperty("needleCode").GetString() == NeedleCodes.Needle1);
        Assert.Equal("UI-FORMAL-REAGENT", projectedNeedle.GetProperty("loadedReagentCode").GetString());
        Assert.Equal(135, projectedNeedle.GetProperty("volumeUl").GetInt32());
        Assert.True(projectedNeedle.GetProperty("needsWash").GetBoolean());
        Assert.Contains(root.GetProperty("recentEvents").EnumerateArray(),
            x => x.GetProperty("title").GetString() == "operator.ui.formal-snapshot");
    }

    [Fact]
    public async Task Traceability_pages_use_formal_history_alarm_audit_apis_and_csv_exports()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var history = await client.GetStringAsync("/history");
        var alarms = await client.GetStringAsync("/alarms");
        var management = await client.GetStringAsync("/management");
        var hostScript = await client.GetStringAsync("/static/js/stainer-host.js");

        Assert.Contains("historySlides", history);
        Assert.Contains("alarmList", alarms);
        Assert.Contains("auditCorrelationFilter", management);
        Assert.Contains("/api/history/runs", hostScript);
        Assert.Contains("/api/history/reagent-consumptions", hostScript);
        Assert.Contains("/api/alarms", hostScript);
        Assert.Contains("/api/audit/logs", hostScript);
        Assert.Contains("/api/history/export/runs", hostScript);
        Assert.Contains("/api/history/export/reagent-consumptions", hostScript);
        Assert.Contains("/api/alarms/export", hostScript);
        Assert.Contains("/api/audit/export", hostScript);
        Assert.Contains("acknowledgeTraceAlarm", hostScript);
        Assert.DoesNotContain("localStorage", hostScript);
    }

    [Fact]
    public async Task Mock_timeline_page_is_hidden_in_production()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/mock-timeline");

        Assert.Contains("drawerBoard", html);
        Assert.DoesNotContain("mockGanttBoard", html);
        Assert.DoesNotContain("mock-timeline.js", html);
    }

    [Fact]
    public async Task Web_host_mock_api_supports_login_initialize_and_state()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/login", new { username = "operator", password = "123456", role = "operator" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginResponse = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("/control-console", loginResponse?.Redirect);

        var initialize = await client.PostAsJsonAsync("/api/system/initialize", new
        {
            commandId = "cmd-web-host-initialize"
        });
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        var samples = await client.PostAsync("/api/samples/scan?count=4", null);
        Assert.Equal(HttpStatusCode.OK, samples.StatusCode);
        var scanSession = await samples.Content.ReadFromJsonAsync<SampleScanSessionResponse>();
        Assert.NotNull(scanSession);
        Assert.Equal(4, scanSession!.Items.Count);

        var state = await client.GetFromJsonAsync<RuntimeStateResponse>("/api/state");
        Assert.NotNull(state);
        Assert.True(state!.Initialized);
        Assert.Equal("initialized", state.Status);
        Assert.Empty(state.Channels.SelectMany(x => x.Slides));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(4, await dbContext.SampleScanItems.CountAsync(x => x.SampleScanSessionId == scanSession.SessionId));
        Assert.False(await dbContext.StainingTasks.AnyAsync());
    }

    [Fact]
    public async Task Logout_invalidates_the_session_and_writes_an_audit_record()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/login", new
        {
            username = "operator",
            password = "123456",
            role = "operator"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var logout = await client.PostAsync("/api/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var protectedRequest = await client.PostAsJsonAsync("/api/device-initialization", new
        {
            commandId = "cmd-after-logout"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, protectedRequest.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "auth.logout"));
    }

    [Fact]
    public async Task State_api_returns_current_run_snapshot_for_page_rendering()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        string slideTaskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var drawer = await dbContext.Drawers.SingleAsync(x => x.Code == "A");
            var slot = await dbContext.PhysicalSlots.SingleAsync(x => x.Code == "A-01");
            var workflow = new WorkflowDefinition
            {
                Code = "STATE-HE",
                Name = "State HE Workflow",
                WorkflowType = StainingTaskType.He,
                Description = "State API integration test"
            };
            var version = new WorkflowVersion
            {
                VersionNo = 810,
                VersionLabel = "8.1",
                Status = WorkflowVersionStatus.Published,
                ChangeNote = "state-api-test",
                PublishedAtUtc = DateTimeOffset.UtcNow
            };
            workflow.Versions.Add(version);

            var task = new StainingTask
            {
                TaskCode = "STATE-TASK-001",
                TaskType = StainingTaskType.He,
                Status = StainingTaskStatus.Confirmed,
                PhysicalSlot = slot,
                WorkflowDefinition = workflow,
                WorkflowVersion = version,
                WorkflowSnapshotJson = "{}",
                InputMode = "manual"
            };
            var run = new MachineRun
            {
                RunCode = "RUN-STATE-001",
                Status = RuntimeLedgerStatus.Running,
                CurrentMajorStepCode = "HEMATOXYLIN",
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            var batch = new ChannelBatch
            {
                MachineRun = run,
                DrawerId = drawer.Id,
                DrawerCode = "A",
                Status = RuntimeLedgerStatus.Running,
                ExperimentType = StainingTaskType.He,
                SelectedWorkflowVersion = version,
                WorkflowSnapshotJson = "{\"workflowVersionId\":\"state-test\"}",
                WorkflowSelectionStatus = WorkflowSelectionStatus.Locked,
                WorkflowLockedAtUtc = DateTimeOffset.UtcNow,
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            var slide = new SlideTask
            {
                ChannelBatch = batch,
                StainingTask = task,
                PhysicalSlot = slot,
                SlotCode = "A-01",
                TaskType = StainingTaskType.He,
                Status = RuntimeLedgerStatus.Running
            };
            var execution = new WorkflowExecution
            {
                MachineRun = run,
                SlideTask = slide,
                WorkflowVersion = version,
                Status = RuntimeLedgerStatus.Running
            };
            execution.StepExecutions.Add(new WorkflowStepExecution
            {
                StepNo = 1,
                MajorStepCode = "HEMATOXYLIN",
                StepName = "Hematoxylin",
                ActionType = "Dispense",
                ReagentCode = "HEM",
                VolumeUl = 100,
                Status = RuntimeLedgerStatus.Running
            });
            dbContext.MachineRuns.Add(run);
            dbContext.StainingTasks.Add(task);
            dbContext.ChannelBatches.Add(batch);
            dbContext.SlideTasks.Add(slide);
            dbContext.WorkflowExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            slideTaskId = slide.Id;
        }

        var state = await client.GetFromJsonAsync<RuntimeStateResponse>("/api/state");

        Assert.NotNull(state);
        Assert.Equal("running", state!.Status);
        Assert.Equal(4, state.Channels.Length);
        Assert.Contains(state.Channels.SelectMany(x => x.Slides), x => x.Id == slideTaskId);
    }

    [Fact]
    public async Task State_api_projects_active_channel_batches_for_samples_page_refresh()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        string batchId;
        string versionId;
        string taskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var drawer = await dbContext.Drawers.SingleAsync(x => x.Code == "A");
            var slot = await dbContext.PhysicalSlots.SingleAsync(x => x.Code == "A-01");
            var workflow = new WorkflowDefinition
            {
                Code = "SAMPLES-HE",
                Name = "Samples HE Workflow",
                WorkflowType = StainingTaskType.He,
                Description = "Samples page state projection test"
            };
            var version = new WorkflowVersion
            {
                VersionNo = 910,
                VersionLabel = "9.1",
                Status = WorkflowVersionStatus.Published,
                ChangeNote = "samples-page-state-test",
                PublishedAtUtc = DateTimeOffset.UtcNow
            };
            workflow.Versions.Add(version);

            var task = new StainingTask
            {
                TaskCode = "SAMPLES-TASK-001",
                TaskType = StainingTaskType.He,
                Status = StainingTaskStatus.Confirmed,
                PhysicalSlot = slot,
                WorkflowDefinition = workflow,
                WorkflowVersion = version,
                WorkflowSnapshotJson = "{\"workflow\":\"samples\"}",
                InputMode = "ManualHE",
                RawSampleCode = "SLIDE-001"
            };
            var batch = new ChannelBatch
            {
                DrawerId = drawer.Id,
                DrawerCode = "A",
                Status = RuntimeLedgerStatus.Pending,
                ExperimentType = StainingTaskType.He,
                SelectedWorkflowVersion = version,
                WorkflowSnapshotJson = "{\"workflow\":\"samples\"}",
                WorkflowSelectionStatus = WorkflowSelectionStatus.Selected,
                WorkflowSelectedAtUtc = DateTimeOffset.UtcNow
            };
            var slide = new SlideTask
            {
                ChannelBatch = batch,
                StainingTask = task,
                PhysicalSlot = slot,
                SlotCode = "A-01",
                TaskType = StainingTaskType.He,
                Status = RuntimeLedgerStatus.Pending
            };
            dbContext.WorkflowDefinitions.Add(workflow);
            dbContext.StainingTasks.Add(task);
            dbContext.ChannelBatches.Add(batch);
            dbContext.SlideTasks.Add(slide);
            await dbContext.SaveChangesAsync();

            batchId = batch.Id;
            versionId = version.Id;
            taskId = task.Id;
        }

        var state = await client.GetFromJsonAsync<RuntimeStateResponse>("/api/state");

        Assert.NotNull(state);
        Assert.Equal(4, state!.Channels.Length);
        var channelA = Assert.Single(state.Channels, x => x.DrawerCode == "A");
        Assert.Equal(batchId, channelA.ChannelBatchId);
        Assert.Equal(StainingTaskType.He, channelA.ExperimentType);
        Assert.Equal(versionId, channelA.WorkflowVersionId);
        Assert.Equal("Samples HE Workflow", channelA.WorkflowName);
        Assert.Equal("9.1", channelA.WorkflowVersionLabel);
        Assert.Equal(WorkflowSelectionStatus.Selected, channelA.WorkflowSelectionStatus);
        Assert.False(channelA.WorkflowLocked);
        Assert.False(channelA.CanSelectWorkflow);
        Assert.True(channelA.CanChangeWorkflow);

        var projectedSlide = Assert.Single(channelA.Slides);
        Assert.Equal(1, projectedSlide.Slot);
        Assert.Equal(taskId, projectedSlide.StainingTaskId);
        Assert.Equal("SLIDE-001", projectedSlide.SampleIdentifier);
        Assert.Equal("SAMPLES-HE", projectedSlide.ProtocolCode);
        Assert.Equal("Samples HE Workflow", projectedSlide.WorkflowName);
        Assert.Equal("9.1", projectedSlide.WorkflowVersionLabel);
        Assert.Equal(versionId, projectedSlide.WorkflowVersionId);

        var channelB = Assert.Single(state.Channels, x => x.DrawerCode == "B");
        Assert.Null(channelB.ChannelBatchId);
        Assert.True(channelB.CanSelectWorkflow);
        Assert.Empty(channelB.Slides);
    }

    [Fact]
    public async Task Read_apis_return_values_inserted_into_sqlite_not_legacy_json()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        string workflowId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var operatorRole = await dbContext.Roles.SingleAsync(x => x.Code == "operator");
            var r1 = await dbContext.ReagentRackPositions.SingleAsync(x => x.Code == "R1");

            var user = new User
            {
                Username = "db-reader",
                DisplayName = "SQLite Reader"
            };
            user.UserRoles.Add(new UserRole { RoleId = operatorRole.Id });
            dbContext.Users.Add(user);

            var workflow = new WorkflowDefinition
            {
                Code = "DB-IHC",
                Name = "SQLite IHC Workflow",
                WorkflowType = "IHC",
                Description = "Inserted by integration test"
            };
            var version = new WorkflowVersion
            {
                VersionNo = 900,
                VersionLabel = "9.0",
                Status = WorkflowVersionStatus.Published,
                ChangeNote = "integration-test",
                PublishedAtUtc = DateTimeOffset.UtcNow
            };
            version.Steps.Add(new WorkflowStep
            {
                StepNo = 1,
                MajorStepCode = "ANTIBODY",
                StepName = "SQLite Antibody Step",
                ActionType = "dispense",
                ReagentCode = "TDB",
                VolumeUl = 123,
                DurationSeconds = 45,
                TargetTemperatureDeciC = 420,
                FailureStrategy = "Stop"
            });
            workflow.Versions.Add(version);
            dbContext.WorkflowDefinitions.Add(workflow);
            workflowId = workflow.Id;

            var liquidClass = new LiquidClassProfile
            {
                Code = "LC-WEB",
                Name = "SQLite Liquid Class",
                AspirateSpeedUlPerSecond = 11,
                DispenseSpeedUlPerSecond = 22
            };
            var reagent = new ReagentDefinition
            {
                ReagentCode = "TDB",
                Name = "SQLite Test Reagent",
                ReagentType = "Primary",
                LiquidClassProfile = liquidClass,
                MinimumAlarmVolumeUl = 1000
            };
            var bottle = new ReagentBottle
            {
                ReagentDefinition = reagent,
                FullBarcode = "TDB05020260101001",
                ReagentCode = "TDB",
                ProductionBatchNo = "20260101",
                SerialNo = "001",
                InitialVolumeUl = 5000,
                RemainingVolumeUl = 4500,
                ExpirationDate = new DateOnly(2027, 1, 1),
                Status = "Available",
                FirstScannedAtUtc = DateTimeOffset.UtcNow,
                LastScannedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.ReagentRackPlacements.Add(new ReagentRackPlacement
            {
                ReagentBottle = bottle,
                ReagentRackPositionId = r1.Id,
                PlacedAtUtc = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync();
        }

        var workflows = await client.GetFromJsonAsync<List<WorkflowSummaryResponse>>("/api/workflows");
        Assert.Contains(workflows!, x => x.Code == "DB-IHC" && x.Name == "SQLite IHC Workflow");

        var workflowDetail = await client.GetFromJsonAsync<WorkflowDetailResponse>($"/api/workflows/{workflowId}");
        Assert.NotNull(workflowDetail);
        Assert.Equal("SQLite Antibody Step", workflowDetail!.Versions.Single().Steps.Single().StepName);

        var protocols = await client.GetFromJsonAsync<List<ProtocolCompatResponse>>("/api/protocols");
        Assert.Contains(protocols!, x => x.Code == "DB-IHC" && x.Version == "9.0");

        var catalog = await client.GetFromJsonAsync<List<ReagentCatalogItemResponse>>("/api/reagents/catalog");
        Assert.Contains(catalog!, x => x.ReagentCode == "TDB" && x.Name == "SQLite Test Reagent" && x.LiquidClassCode == "LC-WEB");

        var rack = await client.GetFromJsonAsync<List<ReagentRackPositionResponse>>("/api/reagents/rack");
        var rackR1 = Assert.Single(rack!, x => x.Position == "R1");
        Assert.Equal("TDB", rackR1.Bottle?.ReagentCode);
        Assert.Equal(4500, rackR1.Bottle?.RemainingVolumeUl);

        var layout = await client.GetFromJsonAsync<EngineeringLayoutResponse>("/api/engineering/layout");
        Assert.Equal(4, layout!.Drawers.Count);
        Assert.Equal(16, layout.Drawers.SelectMany(x => x.Slots).Count());
        Assert.Equal(40, layout.ReagentRackPositions.Count);
        Assert.Equal(8, layout.DabMixPositions.Count);
        Assert.Equal(4, layout.WashPositions.Count);

        var coordinateProfiles = await client.GetFromJsonAsync<List<CoordinateProfileResponse>>("/api/engineering/coordinate-profiles");
        var factoryDefault = Assert.Single(coordinateProfiles!, x => x.Code == "FactoryDefault-v1");
        Assert.Contains(factoryDefault.Points, x => x.PointCode == "Needle1" && x.PresetXUm == 0 && x.PresetYUm == 0);
        Assert.Contains(factoryDefault.Points, x => x.PointCode == "Needle2" && x.PresetXUm == 25000 && x.PresetYUm == 0);

        var liquidClasses = await client.GetFromJsonAsync<List<LiquidClassResponse>>("/api/engineering/liquid-classes");
        Assert.Contains(liquidClasses!, x => x.Code == "LC-WEB" && x.AspirateSpeedUlPerSecond == 11);

        var users = await client.GetFromJsonAsync<List<UserListItemResponse>>("/api/users");
        Assert.Contains(users!, x => x.Username == "db-reader" && x.Roles.Contains("operator"));

        var roles = await client.GetFromJsonAsync<List<RoleListItemResponse>>("/api/roles");
        Assert.Contains(roles!, x => x.Code == "operator");
        Assert.Contains(roles!, x => x.Code == "engineer");
        Assert.Contains(roles!, x => x.Code == "admin");
    }

    [Fact]
    public async Task Reagent_rack_read_api_returns_sqlite_scan_states_and_reflects_database_updates()
    {
        await using var factory = CreateFactory();
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        string bottleId;
        var scannedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var r1 = await dbContext.ReagentRackPositions.SingleAsync(x => x.Code == "R1");
            var r2 = await dbContext.ReagentRackPositions.SingleAsync(x => x.Code == "R2");
            var r3 = await dbContext.ReagentRackPositions.SingleAsync(x => x.Code == "R3");

            var definition = new ReagentDefinition
            {
                ReagentCode = "RACK-DB",
                Name = "Rack DB Reagent",
                ReagentType = "Primary",
                MinimumAlarmVolumeUl = 800
            };
            var bottle = new ReagentBottle
            {
                ReagentDefinition = definition,
                FullBarcode = "RDB05020270101009",
                ReagentCode = "RACK-DB",
                ProductionBatchNo = "20270101",
                SerialNo = "009",
                InitialVolumeUl = 5000,
                RemainingVolumeUl = 4200,
                ExpirationDate = new DateOnly(2028, 1, 1),
                Status = "Available",
                FirstScannedAtUtc = scannedAt,
                LastScannedAtUtc = scannedAt
            };
            var session = new ReagentScanSession
            {
                SessionCode = "SCAN-WEB-READ-001",
                Status = "Completed",
                StartedAtUtc = scannedAt,
                CompletedAtUtc = scannedAt
            };
            session.Items.Add(new ReagentScanItem
            {
                ReagentRackPositionId = r1.Id,
                ScannerChannelNo = r1.ScannerChannelNo,
                ScannerChannelCode = r1.ScannerChannelCode,
                LocatorCode = r1.Code,
                ScanResult = ReagentScanResult.Valid,
                RawBarcode = bottle.FullBarcode,
                ParsedReagentCode = "RACK-DB",
                ParsedQuantityUl = 5000,
                ParsedBatchNo = "20270101",
                ParsedSerialNo = "009",
                IsValidationPassed = true,
                ValidationMessage = "OK",
                CreatedAtUtc = scannedAt
            });
            session.Items.Add(new ReagentScanItem
            {
                ReagentRackPositionId = r2.Id,
                ScannerChannelNo = r2.ScannerChannelNo,
                ScannerChannelCode = r2.ScannerChannelCode,
                LocatorCode = r2.Code,
                ScanResult = ReagentScanResult.Invalid,
                RawBarcode = "BAD",
                ParsedReagentCode = "BAD",
                IsValidationPassed = false,
                ValidationMessage = "Barcode text must be 17 characters.",
                CreatedAtUtc = scannedAt
            });
            session.Items.Add(new ReagentScanItem
            {
                ReagentRackPositionId = r3.Id,
                ScannerChannelNo = r3.ScannerChannelNo,
                ScannerChannelCode = r3.ScannerChannelCode,
                LocatorCode = r3.Code,
                ScanResult = ReagentScanResult.Empty,
                IsValidationPassed = false,
                ValidationMessage = "Empty position.",
                CreatedAtUtc = scannedAt
            });

            dbContext.ReagentScanSessions.Add(session);
            dbContext.ReagentRackPlacements.Add(new ReagentRackPlacement
            {
                ReagentBottle = bottle,
                ReagentRackPositionId = r1.Id,
                ReagentScanSession = session,
                PlacedAtUtc = scannedAt,
                CreatedAtUtc = scannedAt
            });
            await dbContext.SaveChangesAsync();
            bottleId = bottle.Id;
        }

        var rack = await firstClient.GetFromJsonAsync<List<ReagentRackPositionResponse>>("/api/reagents/rack");

        Assert.NotNull(rack);
        Assert.Equal(40, rack!.Count);
        var valid = Assert.Single(rack, x => x.Position == "R1");
        Assert.Equal(ReagentScanResult.Valid, valid.ScanState);
        Assert.Equal("SCAN-WEB-READ-001", valid.LastScanSessionCode);
        Assert.Equal("OK", valid.ValidationMessage);
        Assert.True(valid.IsValidationPassed);
        Assert.Equal("RACK-DB", valid.Bottle?.ReagentCode);
        Assert.Equal(4200, valid.Bottle?.RemainingVolumeUl);
        Assert.Equal(new DateOnly(2028, 1, 1), valid.Bottle?.ExpirationDate);
        Assert.NotNull(valid.Bottle?.LastScannedAtUtc);

        var invalid = Assert.Single(rack, x => x.Position == "R2");
        Assert.Equal(ReagentScanResult.Invalid, invalid.ScanState);
        Assert.Null(invalid.Bottle);
        Assert.Equal("BAD", invalid.RawBarcode);
        Assert.Contains("17 characters", invalid.ValidationMessage);
        Assert.False(invalid.IsValidationPassed);

        var empty = Assert.Single(rack, x => x.Position == "R3");
        Assert.Equal(ReagentScanResult.Empty, empty.ScanState);
        Assert.Null(empty.Bottle);
        Assert.Equal("Empty position.", empty.ValidationMessage);

        var unscanned = Assert.Single(rack, x => x.Position == "R4");
        Assert.Equal("UNSCANNED", unscanned.ScanState);
        Assert.Null(unscanned.Bottle);
        Assert.Equal("No scan result recorded.", unscanned.ValidationMessage);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var bottle = await dbContext.ReagentBottles.SingleAsync(x => x.Id == bottleId);
            bottle.RemainingVolumeUl = 3100;
            bottle.LastScannedAtUtc = scannedAt.AddMinutes(3);
            await dbContext.SaveChangesAsync();
        }

        var updatedRack = await firstClient.GetFromJsonAsync<List<ReagentRackPositionResponse>>("/api/reagents/rack");
        var secondWindowRack = await secondClient.GetFromJsonAsync<List<ReagentRackPositionResponse>>("/api/reagents/rack");

        Assert.Equal(3100, Assert.Single(updatedRack!, x => x.Position == "R1").Bottle?.RemainingVolumeUl);
        Assert.Equal(3100, Assert.Single(secondWindowRack!, x => x.Position == "R1").Bottle?.RemainingVolumeUl);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment = "Testing")
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-web-host-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}"
                    });
                });
            });
    }

    private sealed record SystemInfoResponse(bool PythonRuntimeRequired, string UiHost);

    private sealed record RuntimeStateResponse(bool Initialized, string Status, RuntimeChannel[] Channels);

    private sealed record RuntimeChannel(
        int Id,
        string? DrawerCode,
        string? ChannelBatchId,
        string Status,
        string? ExperimentType,
        string? WorkflowVersionId,
        string? WorkflowName,
        string? WorkflowVersionLabel,
        string WorkflowSelectionStatus,
        bool WorkflowLocked,
        bool CanSelectWorkflow,
        bool CanChangeWorkflow,
        RuntimeSlide[] Slides);

    private sealed record RuntimeSlide(
        string Id,
        int Slot,
        string? Barcode,
        string? SampleIdentifier,
        string? ProtocolCode,
        string? WorkflowName,
        string? WorkflowVersionLabel,
        string? WorkflowVersionId,
        string? StainingTaskId,
        string Status,
        string? CurrentStep,
        int Progress);
}
