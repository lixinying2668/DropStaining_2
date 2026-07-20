using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class EngineeringBackendConfigManagementTests
{
    [Fact]
    public async Task Engineering_session_is_required_expires_and_blocks_operator()
    {
        await using var factory = CreateFactory();
        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operator", "operator");

        var operatorSession = await operatorClient.PostAsJsonAsync("/api/engineering/session", new
        {
            commandId = "cmd-eng-session-operator",
            password = "123456",
            reason = "operator must not open engineering session",
            target = "engineering-session"
        });
        Assert.Equal(HttpStatusCode.Forbidden, operatorSession.StatusCode);

        using var adminClient = factory.CreateClient();
        await LoginAsync(adminClient, "admin", "admin");
        var forbiddenWrite = await adminClient.PostAsJsonAsync("/api/engineering/device-profiles", new
        {
            commandId = "cmd-eng-no-session",
            code = "DEVICE-NO-SESSION",
            name = "No Session",
            isActive = false,
            reason = "write without secondary auth"
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenWrite.StatusCode);

        var badPassword = await adminClient.PostAsJsonAsync("/api/engineering/session", new
        {
            commandId = "cmd-eng-session-bad-password",
            password = "bad",
            reason = "bad password test",
            target = "engineering-session"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, badPassword.StatusCode);

        var session = await OpenEngineeringSessionAsync(adminClient, "session-required");
        Assert.Equal(EngineeringSessionStatus.Active, session.Status);

        var saved = await PostAsync<EngineeringWriteResponse>(adminClient, "/api/engineering/device-profiles", new
        {
            commandId = "cmd-eng-session-save",
            code = "DEVICE-WITH-SESSION",
            name = "With Session",
            isActive = false,
            reason = "write after secondary auth"
        });
        Assert.True(saved.Ok);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var persisted = await dbContext.EngineeringSessions.SingleAsync(x => x.Id == session.SessionId);
            persisted.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dbContext.SaveChangesAsync();
        }

        var expiredWrite = await adminClient.PostAsJsonAsync("/api/engineering/device-profiles", new
        {
            commandId = "cmd-eng-expired-session-save",
            code = "DEVICE-EXPIRED-SESSION",
            name = "Expired Session",
            isActive = false,
            reason = "write after session expiry"
        });
        Assert.Equal(HttpStatusCode.Forbidden, expiredWrite.StatusCode);
    }

    [Fact]
    public async Task Running_engineering_write_requires_dangerous_confirmation()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "running-readonly");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            dbContext.MachineRuns.Add(new MachineRun
            {
                RunCode = $"RUN-ENG-RUNNING-{Guid.NewGuid():N}"[..30],
                Status = RuntimeLedgerStatus.Running,
                PauseRequested = false,
                StopRequested = false,
                CoordinateSnapshotJson = "{}",
                LiquidClassSnapshotJson = "{}",
                LiquidClassSelectionStatus = LiquidClassSelectionStatus.Unselected,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                StartedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var blocked = await client.PostAsJsonAsync("/api/engineering/device-profiles", new
        {
            commandId = "cmd-eng-running-blocked",
            code = "DEVICE-RUNNING-BLOCKED",
            name = "Running Blocked",
            isActive = false,
            reason = "running should default to read only"
        });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var confirmed = await PostAsync<EngineeringWriteResponse>(client, "/api/engineering/device-profiles", new
        {
            commandId = "cmd-eng-running-confirmed",
            code = "DEVICE-RUNNING-CONFIRMED",
            name = "Running Confirmed",
            isActive = false,
            reason = "dangerous confirmation for running write",
            dangerousOperationConfirmed = true
        });
        Assert.True(confirmed.Ok);
    }

    [Fact]
    public async Task Config_import_previews_diffs_applies_new_version_and_rolls_back_invalid_payload()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "config-import");

        var baseline = Assert.Single((await client.GetFromJsonAsync<List<LiquidClassResponse>>("/api/engineering/liquid-classes"))!, x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId is not null);
        var preview = await PostAsync<EngineeringConfigImportPreviewResponse>(client, "/api/engineering/config/import/preview", new
        {
            configType = "liquid-class",
            targetCode = baseline.Code,
            payload = new
            {
                versionLabel = "import-lc-1",
                sourceVersionId = baseline.EnabledVersionId,
                name = "Imported liquid class",
                aspirateSpeedUlPerSecond = 180,
                dispenseSpeedUlPerSecond = 190,
                leadingAirGapUl = 4,
                trailingAirGapUl = 5,
                excessVolumeUl = 1,
                preWetCycles = 1,
                mixCycles = 1
            }
        });
        Assert.True(preview.Ok);
        Assert.True(preview.HasChanges);
        Assert.Contains(preview.Differences, x => x.Path == "aspirateSpeedUlPerSecond");

        var applied = await PostAsync<LiquidClassVersionMutationResponse>(client, "/api/engineering/config/import", new
        {
            commandId = "cmd-eng-import-liquid",
            configType = "liquid-class",
            targetCode = baseline.Code,
            reason = "import creates new draft",
            payload = new
            {
                versionLabel = "import-lc-1",
                sourceVersionId = baseline.EnabledVersionId,
                name = "Imported liquid class",
                aspirateSpeedUlPerSecond = 180,
                dispenseSpeedUlPerSecond = 190,
                leadingAirGapUl = 4,
                trailingAirGapUl = 5,
                excessVolumeUl = 1,
                preWetCycles = 1,
                mixCycles = 1
            }
        });
        Assert.Equal(LiquidClassVersionStatus.Draft, applied.Status);

        var detail = await client.GetFromJsonAsync<LiquidClassVersionResponse>($"/api/engineering/liquid-class-versions/{applied.LiquidClassVersionId}");
        Assert.NotNull(detail);
        var diff = await client.GetFromJsonAsync<List<EngineeringConfigDiffItemResponse>>($"/api/engineering/liquid-class-versions/{applied.LiquidClassVersionId}/diff");
        Assert.NotNull(diff);
        Assert.Contains(diff!, x => x.Path == "aspirateSpeedUlPerSecond");

        var invalid = await client.PostAsJsonAsync("/api/engineering/config/import", new
        {
            commandId = "cmd-eng-import-invalid-liquid",
            configType = "liquid-class",
            targetCode = baseline.Code,
            reason = "invalid import rolls back",
            payload = new
            {
                versionLabel = "bad-import-lc",
                sourceVersionId = baseline.EnabledVersionId,
                name = "Bad import",
                aspirateSpeedUlPerSecond = 0,
                dispenseSpeedUlPerSecond = 190
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var export = await client.GetFromJsonAsync<EngineeringConfigExportResponse>("/api/engineering/config/export");
        Assert.NotNull(export);
        Assert.Contains(export!.LiquidClasses, x => x.Code == baseline.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.False(await dbContext.LiquidClassVersions.AnyAsync(x => x.VersionLabel == "bad-import-lc"));
        Assert.False(await dbContext.CommandReceipts.AnyAsync(x => x.CommandId == "cmd-eng-import-invalid-liquid"));
    }

    [Fact]
    public async Task Active_work_protects_coordinate_and_liquid_class_versions_from_deactivation()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "active-version-protection");

        var coordinateProfiles = await client.GetFromJsonAsync<List<CoordinateProfileResponse>>("/api/engineering/coordinate-profiles");
        var coordinateVersionId = Assert.Single(coordinateProfiles!, x => x.Code == ReferenceDataSeeder.DefaultCoordinateProfileCode).ActiveVersionId!;
        var liquidClass = Assert.Single((await client.GetFromJsonAsync<List<LiquidClassResponse>>("/api/engineering/liquid-classes"))!, x => x.Code == "FactoryGeneral-v1" && x.EnabledVersionId is not null);

        _ = await PostAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-eng-protect-batch",
            drawerCode = "A"
        });
        _ = await PostAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-eng-protect-select",
            drawerCode = "A",
            experimentType = StainingTaskType.He
        });

        var deactivateCoordinate = await client.PostAsJsonAsync($"/api/engineering/coordinate-profile-versions/{coordinateVersionId}/deactivate", new
        {
            commandId = "cmd-eng-protect-coordinate-deactivate",
            reason = "active batch should protect coordinate version"
        });
        Assert.Equal(HttpStatusCode.Conflict, deactivateCoordinate.StatusCode);

        var disableLiquid = await client.PostAsJsonAsync($"/api/engineering/liquid-class-versions/{liquidClass.EnabledVersionId}/disable", new
        {
            commandId = "cmd-eng-protect-liquid-disable",
            reason = "active batch should protect liquid class version"
        });
        Assert.Equal(HttpStatusCode.Conflict, disableLiquid.StatusCode);
    }

    [Fact]
    public async Task Diagnostics_list_and_export_command_logs_errors_and_mock_communications()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        _ = await PostAsync<DeviceInitializationResponse>(client, "/api/device-initialization", new
        {
            commandId = "cmd-eng-diagnostics-init"
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            var run = new MachineRun
            {
                RunCode = $"RUN-ENG-DIAG-{Guid.NewGuid():N}"[..30],
                Status = RuntimeLedgerStatus.Failed,
                PauseRequested = false,
                StopRequested = false,
                CoordinateSnapshotJson = "{}",
                LiquidClassSnapshotJson = "{}",
                LiquidClassSelectionStatus = LiquidClassSelectionStatus.Unselected,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.MachineRuns.Add(run);
            dbContext.DeviceCommandExecutions.Add(new DeviceCommandExecution
            {
                MachineRunId = run.Id,
                CommandType = "DiagnosticProbe",
                Status = DeviceCommandStatus.Failed,
                PayloadJson = "{}",
                ResultJson = "{\"errorCode\":\"diagnostic_probe_failed\",\"message\":\"Probe failed.\"}",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.DeviceCommunicationRecords.Add(new DeviceCommunicationRecord
            {
                DeviceMode = DeviceModes.Mock,
                AdapterName = "MockDeviceAdapter",
                ModuleCode = DeviceModules.Controller,
                Action = "locked-command",
                CommandId = "cmd-eng-diagnostics-pending",
                Source = "CommunicationPersistenceTest",
                Status = DeviceCommunicationPersistenceStatus.Pending,
                Message = "Structured adapter result is pending persistence.",
                RequestJson = "{}",
                ResponseJson = "{}",
                PersistenceStatus = DeviceCommunicationPersistenceStatus.Pending,
                PersistenceFailureReason = "SQLite lock prevented completion persistence.",
                PersistenceLastAttemptAtUtc = DateTimeOffset.UtcNow,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var communications = await client.GetFromJsonAsync<TraceabilityListResponse<EngineeringMockCommunicationResponse>>("/api/engineering/diagnostics/mock-communications?moduleCode=controller&pageSize=50");
        Assert.NotNull(communications);
        Assert.True(communications!.TotalCount > 0);
        Assert.Contains(communications.Items, x => x.CommandId == "cmd-eng-diagnostics-init:step-01:controller");

        var pendingCommunications = await client.GetFromJsonAsync<TraceabilityListResponse<EngineeringMockCommunicationResponse>>("/api/engineering/diagnostics/mock-communications?persistenceStatus=Pending");
        var pendingCommunication = Assert.Single(pendingCommunications!.Items, x => x.CommandId == "cmd-eng-diagnostics-pending");
        Assert.Equal(DeviceCommunicationPersistenceStatus.Pending, pendingCommunication.PersistenceStatus);
        Assert.Contains("SQLite lock", pendingCommunication.PersistenceFailureReason);

        var commandLog = await client.GetFromJsonAsync<TraceabilityListResponse<EngineeringCommandLogResponse>>("/api/engineering/diagnostics/command-log?commandType=DiagnosticProbe");
        Assert.NotNull(commandLog);
        Assert.Single(commandLog!.Items);

        var errors = await client.GetFromJsonAsync<TraceabilityListResponse<EngineeringErrorCodeResponse>>("/api/engineering/diagnostics/errors?code=diagnostic_probe_failed");
        Assert.NotNull(errors);
        Assert.Contains(errors!.Items, x => x.Code == "diagnostic_probe_failed");
        var communicationErrors = await client.GetFromJsonAsync<TraceabilityListResponse<EngineeringErrorCodeResponse>>("/api/engineering/diagnostics/errors?code=communication_record_pending");
        Assert.Contains(communicationErrors!.Items, x => x.SourceId == pendingCommunication.Id);

        var commandCsv = await client.GetAsync("/api/engineering/diagnostics/command-log.csv?commandType=DiagnosticProbe");
        Assert.Equal(HttpStatusCode.OK, commandCsv.StatusCode);
        Assert.Contains("text/csv", commandCsv.Content.Headers.ContentType!.MediaType);

        var communicationCsv = await client.GetAsync("/api/engineering/diagnostics/mock-communications.csv?moduleCode=controller");
        Assert.Equal(HttpStatusCode.OK, communicationCsv.StatusCode);
        Assert.Contains("text/csv", communicationCsv.Content.Headers.ContentType!.MediaType);
        var communicationCsvText = await communicationCsv.Content.ReadAsStringAsync();
        Assert.Contains("PersistenceStatus", communicationCsvText);
        Assert.Contains("cmd-eng-diagnostics-pending", communicationCsvText);

        await using var auditScope = factory.Services.CreateAsyncScope();
        var auditContext = auditScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var exportAudit = (await auditContext.AuditLogs
                .Where(x => x.Action == "engineering.diagnostics.export" && x.EntityId == "engineering_mock_communications")
                .ToListAsync())
            .OrderByDescending(x => x.CreatedAtUtc)
            .First();
        Assert.Contains("\"incompleteCommunicationCount\":1", exportAudit.Message);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-engineering-backend-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
            builder.UseSetting("Device:StartupInitialization:Enabled", "false");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
                ["Device:StartupInitialization:Enabled"] = "false"
            }));
        });
    }

    private static async Task LoginAsync(HttpClient client, string username, string role)
    {
        var response = await client.PostAsJsonAsync("/api/login", new { username, password = "123456", role });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<EngineeringSessionResponse> OpenEngineeringSessionAsync(HttpClient client, string suffix)
    {
        return await PostAsync<EngineeringSessionResponse>(client, "/api/engineering/session", new
        {
            commandId = $"cmd-eng-session-{suffix}",
            password = "123456",
            reason = $"engineering backend test {suffix}",
            target = "engineering-backend"
        });
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
    }
}
