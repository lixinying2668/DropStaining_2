using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class PreHardwareSafetyTests
{
    [Fact]
    public async Task Device_mode_defaults_to_mock_and_change_requests_are_admin_only_idempotent_and_audited()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator", "operator");

        var mode = await operatorClient.GetFromJsonAsync<DeviceModeStatusResponse>("/api/device-mode");
        Assert.NotNull(mode);
        Assert.Equal(DeviceModes.Mock, mode!.CurrentMode);
        Assert.True(mode.IsMock);
        Assert.True(mode.CanStartRuns);

        var dashboard = await operatorClient.GetStringAsync("/dashboard");
        Assert.Contains("metricDeviceMode", dashboard);

        var forbidden = await operatorClient.PostAsJsonAsync("/api/device-mode/change", new
        {
            commandId = "cmd-device-mode-forbidden",
            deviceMode = DeviceModes.Real,
            reason = "operator should not be able to switch modes"
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var adminClient = factory.CreateClient();
        await LoginAsync(adminClient, "admin", "admin");
        var missingReason = await adminClient.PostAsJsonAsync("/api/device-mode/change", new
        {
            commandId = "cmd-device-mode-no-reason",
            deviceMode = DeviceModes.Real,
            reason = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.Equal("reason_required", (await missingReason.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var changed = await PostJsonAsync<DeviceModeChangeResponse>(adminClient, "/api/device-mode/change", new
        {
            commandId = "cmd-device-mode-real",
            deviceMode = DeviceModes.Real,
            reason = "pre-hardware safety test"
        });
        Assert.True(changed.Ok);
        Assert.False(changed.Replayed);
        Assert.Equal(DeviceModes.Mock, changed.CurrentMode);
        Assert.Equal(DeviceModes.Real, changed.RequestedMode);
        Assert.True(changed.RestartRequired);

        var replayed = await PostJsonAsync<DeviceModeChangeResponse>(adminClient, "/api/device-mode/change", new
        {
            commandId = "cmd-device-mode-real",
            deviceMode = DeviceModes.Real,
            reason = "pre-hardware safety test"
        });
        Assert.True(replayed.Replayed);

        var pending = await adminClient.GetFromJsonAsync<DeviceModeStatusResponse>("/api/device-mode");
        Assert.Equal(DeviceModes.Real, pending!.PendingRequestedMode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(1, await dbContext.CommandReceipts.CountAsync(x => x.CommandId == "cmd-device-mode-real"));
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "device.mode.change_requested" && x.EntityId == DeviceModes.Real));
    }

    [Fact]
    public async Task Real_mode_without_health_check_blocks_run_start()
    {
        var context = CreateFactory(new Dictionary<string, string?>
        {
            ["Device:Mode"] = DeviceModes.Real,
            ["Device:HardwareAvailable"] = "true",
            ["Device:RealHealthCheckComplete"] = "false"
        });
        await using var factory = context.Factory;
        using var client = factory.CreateClient();

        var mode = await client.GetFromJsonAsync<DeviceModeStatusResponse>("/api/device-mode");
        Assert.NotNull(mode);
        Assert.Equal(DeviceModes.Real, mode!.CurrentMode);
        Assert.True(mode.IsReal);
        Assert.False(mode.RealDeviceHealthCheckComplete);
        Assert.False(mode.CanStartRuns);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeviceModeService>();
        var exception = Assert.Throws<BusinessRuleException>(() => service.EnsureRunStartAllowed());
        Assert.Equal("real_device_health_required", exception.Code);
    }

    [Fact]
    public void Machine_executor_lease_allows_only_one_owner_for_the_same_lock_file()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "stainer-lease-tests", Guid.NewGuid().ToString("N"), "machine-executor.lock");
        using var first = new MachineExecutorLeaseService(lockPath);
        using var second = new MachineExecutorLeaseService(lockPath);

        Assert.True(first.TryAcquire());
        Assert.True(first.IsOwner);
        Assert.False(second.TryAcquire());
        Assert.True(second.ReadOnlyMode);
        Assert.Equal("executor_lease_unavailable", Assert.Throws<BusinessRuleException>(second.EnsureOwner).Code);

        first.Release();
        Assert.True(second.TryAcquire());
        Assert.True(second.IsOwner);
    }

    [Fact]
    public async Task Startup_recovery_marks_sent_commands_unknown_faults_run_and_creates_alarm_and_audit()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        string runId;
        string commandId;
        string stepId;
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var drawer = await dbContext.Drawers.SingleAsync(x => x.Code == "A");
            var slot = await dbContext.PhysicalSlots.SingleAsync(x => x.Code == "A-01");
            var now = DateTimeOffset.UtcNow;

            var workflow = new WorkflowDefinition
            {
                Code = $"RECOVERY-IHC-{Guid.NewGuid():N}",
                Name = "Recovery IHC",
                WorkflowType = StainingTaskType.Ihc,
                Description = "startup recovery test"
            };
            var version = new WorkflowVersion
            {
                WorkflowDefinition = workflow,
                VersionNo = 1,
                VersionLabel = "1.0",
                Status = WorkflowVersionStatus.Published,
                ChangeNote = "startup recovery test",
                PublishedAtUtc = now
            };
            var run = new MachineRun
            {
                RunCode = $"RUN-RECOVERY-{Guid.NewGuid():N}",
                Status = RuntimeLedgerStatus.Running,
                StartedAtUtc = now
            };
            var batch = new ChannelBatch
            {
                MachineRun = run,
                Drawer = drawer,
                DrawerCode = drawer.Code,
                Status = RuntimeLedgerStatus.Running,
                ExperimentType = StainingTaskType.Ihc,
                SelectedWorkflowVersion = version,
                WorkflowSnapshotJson = "{}",
                WorkflowSelectionStatus = WorkflowSelectionStatus.Locked,
                WorkflowLockedAtUtc = now,
                StartedAtUtc = now
            };
            var task = new StainingTask
            {
                TaskCode = $"TASK-RECOVERY-{Guid.NewGuid():N}",
                TaskType = StainingTaskType.Ihc,
                Status = StainingTaskStatus.Confirmed,
                PhysicalSlot = slot,
                WorkflowDefinition = workflow,
                WorkflowVersion = version,
                WorkflowSnapshotJson = "{}",
                InputMode = "test"
            };
            var slide = new SlideTask
            {
                ChannelBatch = batch,
                StainingTask = task,
                PhysicalSlot = slot,
                SlotCode = slot.Code,
                TaskType = StainingTaskType.Ihc,
                Status = RuntimeLedgerStatus.Running
            };
            var execution = new WorkflowExecution
            {
                MachineRun = run,
                SlideTask = slide,
                WorkflowVersion = version,
                Status = RuntimeLedgerStatus.Running,
                StartedAtUtc = now
            };
            var step = new WorkflowStepExecution
            {
                WorkflowExecution = execution,
                StepNo = 1,
                MajorStepCode = "PRIMARY",
                StepName = "Primary antibody",
                ActionType = "Dispense",
                ReagentCode = "P01",
                VolumeUl = 100,
                Status = RuntimeLedgerStatus.Running,
                StartedAtUtc = now
            };
            var command = new DeviceCommandExecution
            {
                MachineRun = run,
                WorkflowStepExecution = step,
                CommandType = "Dispense",
                Status = DeviceCommandStatus.CommandSent,
                PayloadJson = "{}",
                CommandSentAtUtc = now
            };

            dbContext.WorkflowDefinitions.Add(workflow);
            dbContext.MachineRuns.Add(run);
            dbContext.StainingTasks.Add(task);
            dbContext.ChannelBatches.Add(batch);
            dbContext.SlideTasks.Add(slide);
            dbContext.WorkflowExecutions.Add(execution);
            dbContext.WorkflowStepExecutions.Add(step);
            dbContext.DeviceCommandExecutions.Add(command);
            await dbContext.SaveChangesAsync();
            runId = run.Id;
            commandId = command.Id;
            stepId = step.Id;
        }

        var report = await PostJsonAsync<StartupRecoveryReportResponse>(client, "/api/startup/recovery", new { });
        Assert.True(report.RunsScanned >= 1);
        Assert.Equal(1, report.CommandsMarkedUnknown);
        Assert.Equal(1, report.RunsMarkedFaulted);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(DeviceCommandStatus.Unknown, (await verifyContext.DeviceCommandExecutions.SingleAsync(x => x.Id == commandId)).Status);
        Assert.Equal(RuntimeLedgerStatus.Unknown, (await verifyContext.WorkflowStepExecutions.SingleAsync(x => x.Id == stepId)).Status);
        Assert.Equal(RuntimeLedgerStatus.Faulted, (await verifyContext.MachineRuns.SingleAsync(x => x.Id == runId)).Status);
        Assert.True(await verifyContext.ChannelBatches.AnyAsync(x => x.MachineRunId == runId && x.Status == RuntimeLedgerStatus.Faulted));
        Assert.True(await verifyContext.Alarms.AnyAsync(x => x.MachineRunId == runId && x.Code == "startup_recovery_unknown_command" && x.Status == "Active"));
        Assert.True(await verifyContext.AuditLogs.AnyAsync(x => x.Action == "startup.recovery.unknown_command" && x.EntityId == runId));
    }

    [Fact]
    public async Task Database_maintenance_backup_and_readiness_are_authorized_audited_and_report_safe_baseline()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        using var anonymous = factory.CreateClient();

        var anonymousHealth = await anonymous.GetAsync("/api/database/maintenance");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousHealth.StatusCode);

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator", "operator");
        var forbiddenHealth = await operatorClient.GetAsync("/api/database/maintenance");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenHealth.StatusCode);

        using var adminClient = factory.CreateClient();
        await LoginAsync(adminClient, "admin", "admin");
        var health = await adminClient.GetFromJsonAsync<DatabaseMaintenanceReportResponse>("/api/database/maintenance");
        Assert.NotNull(health);
        Assert.True(health!.Ok, health.Message);
        Assert.True(health.ForeignKeysEnabled);
        Assert.Equal("wal", health.JournalMode, ignoreCase: true);
        Assert.True(health.BusyTimeoutMilliseconds >= DatabaseInitializer.MinimumBusyTimeoutMilliseconds);
        Assert.True(health.CanReadWrite);
        Assert.True(health.IntegrityOk);
        Assert.Equal(0, health.PendingMigrationCount);

        string historicalBackupFailureAlarmId;
        await using (var arrangeScope = factory.Services.CreateAsyncScope())
        {
            var arrangeDbContext = arrangeScope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var historicalAlarm = new Alarm
            {
                Code = "database_backup_failed",
                Severity = "Critical",
                Status = "Active",
                Message = "Historical backup failure from a previous degraded run.",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            arrangeDbContext.Alarms.Add(historicalAlarm);
            await arrangeDbContext.SaveChangesAsync();
            historicalBackupFailureAlarmId = historicalAlarm.Id;
        }

        var backup = await PostJsonAsync<DatabaseBackupResponse>(adminClient, "/api/database/backup", new
        {
            commandId = "cmd-database-backup-safety",
            outputDirectory = context.BackupDirectory
        });
        Assert.True(backup.Ok);
        Assert.True(backup.IntegrityBeforeOk);
        Assert.True(backup.IntegrityAfterOk);
        Assert.True(File.Exists(backup.BackupPath));
        Assert.Contains("stainer-backup-", Path.GetFileName(backup.BackupPath));

        var replayedBackup = await PostJsonAsync<DatabaseBackupResponse>(adminClient, "/api/database/backup", new
        {
            commandId = "cmd-database-backup-safety",
            outputDirectory = context.BackupDirectory
        });
        Assert.True(replayedBackup.Replayed);
        Assert.Equal(backup.BackupPath, replayedBackup.BackupPath);

        var initialized = await PostJsonAsync<DeviceInitializationResponse>(adminClient, "/api/device-initialization", new
        {
            commandId = "cmd-prehardware-device-initialization"
        });
        Assert.True(initialized.Ok, initialized.Message);

        var readiness = await adminClient.GetFromJsonAsync<PreHardwareReadinessResponse>("/api/prehardware-readiness");
        Assert.NotNull(readiness);
        Assert.True(readiness!.Ok, string.Join("; ", readiness.BlockingReasons));
        Assert.Contains(readiness.Checks, x => x.Code == "database_health" && x.Ok);
        Assert.Contains(readiness.Checks, x => x.Code == "executor_single_owner" && x.Ok);
        Assert.Contains(readiness.Checks, x => x.Code == "channel_script_and_workflow_seed" && x.Ok);
        Assert.Contains(readiness.Checks, x => x.Code == "task_compatibility_mapping" && x.Ok);
        Assert.Contains(readiness.Checks, x => x.Code == "formal_preflight_available" && x.Ok);
        Assert.Contains(readiness.Checks, x => x.Code == "mock_executor_traceability" && x.Ok);
        Assert.Contains(readiness.Checks, x => x.Code == "signalr_event_buffer" && x.Ok);
        Assert.Contains(readiness.Checks, x => x.Code == "structured_safety_log" && x.Ok);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "database.backup" && x.EntityId == backup.BackupPath));
        var historicalAlarmAfterBackup = await dbContext.Alarms
            .Include(x => x.Actions)
            .SingleAsync(x => x.Id == historicalBackupFailureAlarmId);
        Assert.Equal("Resolved", historicalAlarmAfterBackup.Status);
        Assert.NotNull(historicalAlarmAfterBackup.ClearedAtUtc);
        Assert.Contains(historicalAlarmAfterBackup.Actions, x => x.Action == "Acknowledged" && x.Message.Contains(backup.BackupPath, StringComparison.Ordinal));
        Assert.Contains(historicalAlarmAfterBackup.Actions, x => x.Action == "Resolved" && x.Message.Contains(backup.BackupPath, StringComparison.Ordinal));
        var backupFileName = Path.GetFileName(backup.BackupPath);
        Assert.True(await dbContext.AuditLogs.AnyAsync(x =>
            x.Action == "alarm.resolve"
            && x.EntityType == "Alarm"
            && x.EntityId == historicalBackupFailureAlarmId
            && x.Message.Contains(backupFileName)));
        Assert.False(await dbContext.Alarms.AnyAsync(x => x.Code == "database_backup_failed" && x.Status != "Resolved"));

        var backupStem = ResolveBackupStem(backup.BackupPath);
        var formalBackupFiles = Directory.GetFiles(context.BackupDirectory)
            .Select(Path.GetFileName)
            .Where(name => name is not null && name.StartsWith(backupStem, StringComparison.Ordinal))
            .ToList();
        Assert.DoesNotContain(formalBackupFiles, name => name!.EndsWith(".db-journal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(formalBackupFiles, name =>
            name!.Contains("-vacuum-", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, Path.GetFileName(backup.BackupPath), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, $"{Path.GetFileName(backup.BackupPath)}-wal", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, $"{Path.GetFileName(backup.BackupPath)}-shm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Safety_log_writer_records_required_trace_fields_and_scrubs_sensitive_values()
    {
        var context = CreateFactory();
        await using var factory = context.Factory;
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<SafetyLogWriter>();

        await writer.WriteAsync(
            "device",
            "Error",
            "password=secret; token=secret-token; Data Source=C:\\secret\\stainer.db",
            new SafetyLogContext(
                CorrelationId: "corr-001",
                CommandId: "cmd-001",
                MachineRunId: "run-001",
                ChannelBatchId: "batch-001",
                SlideTaskId: "slide-001",
                WorkflowStepExecutionId: "step-001",
                DeviceCommandExecutionId: "device-command-001",
                DeviceMode: DeviceModes.Mock,
                Actor: "operator",
                Source: "PreHardwareSafetyTests"),
            new InvalidOperationException("connection string contains token"),
            CancellationToken.None);

        var logFile = Assert.Single(Directory.GetFiles(context.LogDirectory, "device-*.jsonl"));
        var logText = await File.ReadAllTextAsync(logFile);
        Assert.Contains("\"correlationId\":\"corr-001\"", logText);
        Assert.Contains("\"commandId\":\"cmd-001\"", logText);
        Assert.Contains("\"machineRunId\":\"run-001\"", logText);
        Assert.Contains("\"channelBatchId\":\"batch-001\"", logText);
        Assert.Contains("\"slideTaskId\":\"slide-001\"", logText);
        Assert.Contains("\"workflowStepExecutionId\":\"step-001\"", logText);
        Assert.Contains("\"deviceCommandExecutionId\":\"device-command-001\"", logText);
        Assert.Contains("\"deviceMode\":\"Mock\"", logText);
        Assert.Contains("\"actor\":\"operator\"", logText);
        Assert.Contains("\"source\":\"PreHardwareSafetyTests\"", logText);
        Assert.Contains("[redacted sensitive details]", logText);
        Assert.DoesNotContain("secret-token", logText);
        Assert.DoesNotContain("C:\\secret", logText);
    }

    private static FactoryContext CreateFactory(Dictionary<string, string?>? extraSettings = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "stainer-prehardware-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "stainer.db");
        var logDirectory = Path.Combine(root, "logs");
        var backupDirectory = Path.Combine(root, "backups");
        var leasePath = Path.Combine(root, "machine-executor.lock");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
            ["MachineExecutor:LeasePath"] = leasePath,
            ["Safety:LogDirectory"] = logDirectory,
            ["Database:BackupDirectory"] = backupDirectory
        };
        if (extraSettings is not null)
        {
            foreach (var (key, value) in extraSettings)
            {
                settings[key] = value;
            }
        }

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
                builder.UseSetting("MachineExecutor:LeasePath", leasePath);
                builder.UseSetting("Safety:LogDirectory", logDirectory);
                builder.UseSetting("Database:BackupDirectory", backupDirectory);
                foreach (var pair in settings)
                {
                    builder.UseSetting(pair.Key, pair.Value);
                }

                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            });
        return new FactoryContext(factory, root, databasePath, logDirectory, backupDirectory);
    }

    private static async Task LoginAsync(HttpClient client, string username, string role)
    {
        var response = await client.PostAsJsonAsync("/api/login", new
        {
            username,
            password = "123456",
            role
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
    }

    private sealed record FactoryContext(
        WebApplicationFactory<Program> Factory,
        string Root,
        string DatabasePath,
        string LogDirectory,
        string BackupDirectory);

    private static string ResolveBackupStem(string backupPath)
    {
        var stem = Path.GetFileNameWithoutExtension(backupPath);
        var copyIndex = stem.IndexOf("-copy-", StringComparison.OrdinalIgnoreCase);
        if (copyIndex > 0)
        {
            return stem[..copyIndex];
        }

        var vacuumIndex = stem.IndexOf("-vacuum-", StringComparison.OrdinalIgnoreCase);
        if (vacuumIndex > 0)
        {
            return stem[..vacuumIndex];
        }

        return stem;
    }
}
