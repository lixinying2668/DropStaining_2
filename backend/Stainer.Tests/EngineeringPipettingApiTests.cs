using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class EngineeringPipettingApiTests
{
    [Fact]
    public async Task Migrated_aspirate_dispense_and_wash_outer_call_process_action_once_and_write_engineering_ledger_once()
    {
        var process = new RecordingProcessActionService();
        await using var factory = CreateFactory(processActionService: process);
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "pipetting-process-success");
        var setup = await LoadSetupAsync(factory);
        var washOuter = await LoadPointAsync(factory, "WashOuter");

        var aspirate = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/aspirate", new
        {
            commandId = "cmd-eng-process-aspirate",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            volumeUl = 50,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "process aspirate"
        });
        var dispense = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/dispense", new
        {
            commandId = "cmd-eng-process-dispense",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            volumeUl = 20,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "process dispense"
        });
        var washOuterResponse = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/wash", new
        {
            commandId = "cmd-eng-process-wash-outer",
            channel = "A",
            needleCode = "Needle1",
            position = washOuter.PointCode,
            reason = "process wash outer"
        });

        Assert.True(aspirate.Ok, aspirate.Message);
        Assert.True(dispense.Ok, dispense.Message);
        Assert.True(washOuterResponse.Ok, washOuterResponse.Message);
        var sourceCall = Assert.Single(process.SourceCalls);
        Assert.Equal(setup.PositionCode, sourceCall.Move.PointCode);
        Assert.Equal(setup.XUm, sourceCall.Move.TargetXUm);
        Assert.Equal(setup.YUm, sourceCall.Move.TargetYUm);
        Assert.Equal(setup.SafeZUm, sourceCall.Move.SafeZUm);
        var take = Assert.IsType<TakeLiquidRequest>(sourceCall.Action);
        Assert.Equal(50, take.VolumeUl);
        Assert.Equal(setup.ActionZUm, take.AspirateZUm);
        Assert.Equal(setup.SafeZUm, take.SafeZUm);

        var targetCall = Assert.Single(process.TargetCalls);
        Assert.Equal(setup.PositionCode, targetCall.Move.PointCode);
        var dispenseAction = Assert.IsType<DispenseLiquidRequest>(targetCall.Action);
        Assert.Equal(20, dispenseAction.VolumeUl);
        Assert.Equal(setup.DispenseZUm, dispenseAction.DispenseZUm);
        Assert.Equal(setup.SafeZUm, dispenseAction.SafeZUm);

        var outerCall = Assert.Single(process.OuterWashCalls);
        Assert.Equal(washOuter.PointCode, outerCall.Move.PointCode);
        Assert.Equal(washOuter.XUm, outerCall.Move.TargetXUm);
        Assert.Equal(washOuter.YUm, outerCall.Move.TargetYUm);
        Assert.Equal(washOuter.SafeZUm, outerCall.Move.SafeZUm);
        var outerAction = Assert.IsType<WashOuterRequest>(outerCall.Action);
        Assert.Equal(washOuter.CalibratedZUm ?? washOuter.SafeZUm, outerAction.WashOuterZUm);
        Assert.Equal(washOuter.SafeZUm, outerAction.SafeZUm);
        Assert.Empty(process.InnerWashCalls);

        await AssertEngineeringWriteOnceAsync(factory, "cmd-eng-process-aspirate", PipettingOperationTypes.Aspirate, "engineering.pipetting.aspirate");
        await AssertEngineeringWriteOnceAsync(factory, "cmd-eng-process-dispense", PipettingOperationTypes.Dispense, "engineering.pipetting.dispense");
        await AssertEngineeringWriteOnceAsync(factory, "cmd-eng-process-wash-outer", PipettingOperationTypes.WashNeedle, "engineering.pipetting.washneedle");
    }

    [Fact]
    public async Task Wash_inner_flush_and_liquid_detect_keep_current_behavior_without_process_action()
    {
        var process = new RecordingProcessActionService();
        await using var factory = CreateFactory(processActionService: process);
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "pipetting-process-unchanged");
        var setup = await LoadSetupAsync(factory);
        var washInner = await LoadPointAsync(factory, "WashInner");
        var washOuter = await LoadPointAsync(factory, "WashOuter");

        var liquidDetect = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/liquid-detect", new
        {
            commandId = "cmd-eng-process-liquid-detect-unchanged",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            reason = "unchanged liquid detect"
        });
        var washInnerResponse = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/wash", new
        {
            commandId = "cmd-eng-process-wash-inner-unchanged",
            channel = "A",
            needleCode = "Needle1",
            position = washInner.PointCode,
            reason = "unchanged inner wash"
        });
        var flush = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/flush", new
        {
            commandId = "cmd-eng-process-flush-unchanged",
            channel = "A",
            needleCode = "Needle1",
            position = washOuter.PointCode,
            reason = "unchanged flush"
        });

        Assert.True(liquidDetect.Ok, liquidDetect.Message);
        Assert.True(washInnerResponse.Ok, washInnerResponse.Message);
        Assert.True(flush.Ok, flush.Message);
        Assert.Equal(0, process.TotalCallCount);
    }

    [Theory]
    [InlineData("calibrated_x_um")]
    [InlineData("calibrated_y_um")]
    [InlineData("safe_z_um")]
    public async Task Missing_required_move_coordinate_returns_validation_failure_without_process_action(string missingColumn)
    {
        var process = new RecordingProcessActionService();
        await using var factory = CreateFactory(processActionService: process);
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "pipetting-process-coordinate-missing");
        var setup = await LoadSetupAsync(factory);
        await ClearCoordinateColumnAsync(factory, setup.PositionCode, missingColumn);

        var response = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/aspirate", new
        {
            commandId = $"cmd-eng-process-missing-{missingColumn.Replace("_um", string.Empty).Replace("_", "-")}",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            volumeUl = 50,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "missing x"
        });

        Assert.False(response.Ok);
        Assert.Equal("engineering_pipetting_coordinate_incomplete", response.ErrorCode);
        Assert.Equal(0, process.TotalCallCount);
        await AssertEngineeringWriteOnceAsync(factory, response.CommandId, PipettingOperationTypes.Aspirate, "engineering.pipetting.aspirate", DeviceCommandStatus.Failed);
    }

    [Theory]
    [InlineData(RobotArmProcessFailureStage.Move, "engineering_pipetting_move_failed")]
    [InlineData(RobotArmProcessFailureStage.Action, "engineering_pipetting_action_failed")]
    public async Task Process_action_failure_returns_failed_engineering_response_without_final_state_update(
        RobotArmProcessFailureStage failureStage,
        string expectedErrorCode)
    {
        var process = new RecordingProcessActionService
        {
            FailureStage = failureStage,
            FailureMessage = $"{failureStage} failed"
        };
        await using var factory = CreateFactory(processActionService: process);
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, $"pipetting-process-{failureStage}-failed");
        var setup = await LoadSetupAsync(factory);

        var response = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/aspirate", new
        {
            commandId = $"cmd-eng-process-{failureStage.ToString().ToLowerInvariant()}-failed",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            volumeUl = 50,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "process failure"
        });

        Assert.False(response.Ok);
        Assert.Equal(expectedErrorCode, response.ErrorCode);
        Assert.Single(process.SourceCalls);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var commandId = response.CommandId;
        Assert.False(await dbContext.RobotArmStates.AnyAsync(x => x.CurrentCommandId == commandId || x.DeviceCommandExecutionId == commandId));
        var needle = await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle1);
        Assert.Equal(0, needle.VolumeUl);
        Assert.False(await dbContext.PipettingOperations.AnyAsync(x => x.DeviceCommandExecutionId == commandId && x.Status == DeviceCommandStatus.Completed));
        Assert.Equal(1, await dbContext.PipettingOperations.CountAsync(x => x.DeviceCommandExecutionId == commandId && x.Status == DeviceCommandStatus.Failed));
    }

    [Fact]
    public async Task OperationCanceledException_from_process_action_is_not_converted_or_recorded_as_success()
    {
        var process = new RecordingProcessActionService { ThrowOperationCanceled = true };
        await using var factory = CreateFactory(processActionService: process);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<EngineeringPipettingService>();
        var setup = await LoadSetupAsync(factory);
        var actor = new AuthenticatedUser(string.Empty, "admin", "Admin", "admin", ["admin"]);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.AspirateAsync(
            new EngineeringPipettingTestRequest(
                "cmd-eng-process-cancel",
                "A",
                Position: setup.PositionCode,
                VolumeUl: 50,
                NeedleCode: "Needle1",
                LiquidClassVersionId: setup.LiquidClassVersionId,
                Reason: "cancel"),
            actor));

        Assert.Single(process.SourceCalls);
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.False(await dbContext.PipettingOperations.AnyAsync(x => x.DeviceCommandExecutionId == "cmd-eng-process-cancel"));
        Assert.False(await dbContext.RobotArmStates.AnyAsync(x => x.CurrentCommandId == "cmd-eng-process-cancel" || x.DeviceCommandExecutionId == "cmd-eng-process-cancel"));
    }

    [Fact]
    public async Task Command_replay_does_not_call_process_action_or_write_ledger_again()
    {
        var process = new RecordingProcessActionService();
        await using var factory = CreateFactory(processActionService: process);
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "pipetting-process-replay");
        var setup = await LoadSetupAsync(factory);
        var request = new
        {
            commandId = "cmd-eng-process-replay",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            volumeUl = 50,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "replay"
        };

        var first = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/aspirate", request);
        var second = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/aspirate", request);

        Assert.True(first.Ok, first.Message);
        Assert.True(second.Ok, second.Message);
        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Single(process.SourceCalls);
        await AssertEngineeringWriteOnceAsync(factory, "cmd-eng-process-replay", PipettingOperationTypes.Aspirate, "engineering.pipetting.aspirate");
    }

    [Fact]
    public async Task Failed_process_action_replay_does_not_call_process_action_or_write_ledger_again()
    {
        var process = new RecordingProcessActionService
        {
            FailureStage = RobotArmProcessFailureStage.Move,
            FailureMessage = "move failed"
        };
        await using var factory = CreateFactory(processActionService: process);
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "pipetting-process-failed-replay");
        var setup = await LoadSetupAsync(factory);
        var request = new
        {
            commandId = "cmd-eng-process-failed-replay",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            volumeUl = 50,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "failed replay"
        };

        var first = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/aspirate", request);
        var second = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/aspirate", request);

        Assert.False(first.Ok);
        Assert.False(second.Ok);
        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Single(process.SourceCalls);
        await AssertEngineeringWriteOnceAsync(factory, "cmd-eng-process-failed-replay", PipettingOperationTypes.Aspirate, "engineering.pipetting.aspirate", DeviceCommandStatus.Failed);
    }

    [Fact]
    public async Task Engineering_pipetting_tests_require_session_and_record_mock_operations()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        var setup = await LoadSetupAsync(factory);

        var catalog = await client.GetFromJsonAsync<EngineeringPipettingTestCatalogResponse>("/api/engineering/pipetting-tests/types");
        Assert.NotNull(catalog);
        Assert.Contains(catalog!.Operations, x => x.OperationType == PipettingOperationTypes.Aspirate && x.IsExecutable);

        var noSession = await client.PostAsJsonAsync("/api/engineering/pipetting-tests/aspirate", new
        {
            commandId = "cmd-eng-pipette-no-session",
            channel = "A",
            position = setup.PositionCode,
            volumeUl = 20,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "blocked without engineering session"
        });
        Assert.Equal(HttpStatusCode.Forbidden, noSession.StatusCode);

        await OpenEngineeringSessionAsync(client, "pipetting-api");

        var aspirate = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/aspirate", new
        {
            commandId = "cmd-eng-pipette-aspirate",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            volumeUl = 50,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "manual aspirate engineering test",
            operationParameters = new
            {
                sourceType = NeedleLoadSourceTypes.SystemLiquid,
                systemLiquidSourceType = "SystemWater"
            }
        });
        Assert.True(aspirate.Ok, aspirate.Message);
        Assert.Equal(PipettingOperationTypes.Aspirate, aspirate.OperationType);

        var dispense = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/dispense", new
        {
            commandId = "cmd-eng-pipette-dispense",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            volumeUl = 20,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "manual dispense engineering test"
        });
        Assert.True(dispense.Ok, dispense.Message);
        var needleVolume = Assert.IsType<JsonElement>(dispense.State["needleVolumeUl"]);
        Assert.Equal(30, needleVolume.GetInt32());

        var wash = await PostAsync<EngineeringPipettingTestResponse>(client, "/api/engineering/pipetting-tests/wash", new
        {
            commandId = "cmd-eng-pipette-wash",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            reason = "manual wash engineering test"
        });
        Assert.True(wash.Ok, wash.Message);
        Assert.Equal(PipettingOperationTypes.WashNeedle, wash.OperationType);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var operations = await dbContext.PipettingOperations
            .Where(x => x.DeviceCommandExecutionId != null && x.DeviceCommandExecutionId.StartsWith("cmd-eng-pipette-"))
            .ToListAsync();
        operations = operations.OrderBy(x => x.CreatedAtUtc).ToList();
        Assert.Equal(3, operations.Count);
        Assert.All(operations, x =>
        {
            Assert.Equal("A", x.ChannelCode);
            Assert.Equal("Needle1", x.NeedleCode);
            Assert.Equal(setup.PositionCode, x.TargetPointCode);
            Assert.False(string.IsNullOrWhiteSpace(x.ActorUserId));
            Assert.Contains("operationType", x.ParametersJson);
        });
        Assert.Contains(operations, x => x.OperationType == PipettingOperationTypes.Aspirate && x.VolumeUl == 50 && x.Status == DeviceCommandStatus.Completed);
        Assert.Contains(operations, x => x.OperationType == PipettingOperationTypes.Dispense && x.VolumeUl == 20 && x.Status == DeviceCommandStatus.Completed);
        Assert.Contains(operations, x => x.OperationType == PipettingOperationTypes.WashNeedle && x.Status == DeviceCommandStatus.Completed);
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "engineering.pipetting.aspirate"));
        Assert.Equal(0, (await dbContext.NeedleStates.SingleAsync(x => x.NeedleCode == NeedleCodes.Needle1)).VolumeUl);
    }

    [Fact]
    public async Task Engineering_pipetting_tests_fail_closed_in_real_mode()
    {
        var process = new RecordingProcessActionService();
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Device:Mode"] = DeviceModes.Real,
            ["Device:HardwareAvailable"] = "true",
            ["Device:UseMockWhenHardwareUnavailable"] = "false",
            ["Device:DebugMode"] = "false"
        }, process);
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "pipetting-real-blocked");
        var setup = await LoadSetupAsync(factory);

        var response = await client.PostAsJsonAsync("/api/engineering/pipetting-tests/aspirate", new
        {
            commandId = "cmd-eng-pipette-real-blocked",
            channel = "A",
            needleCode = "Needle1",
            position = setup.PositionCode,
            volumeUl = 20,
            liquidClassVersionId = setup.LiquidClassVersionId,
            reason = "real mode must fail closed"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, process.TotalCallCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.False(await dbContext.PipettingOperations.AnyAsync(x => x.DeviceCommandExecutionId == "cmd-eng-pipette-real-blocked"));
    }

    private static async Task<(string PositionCode, string LiquidClassVersionId, long XUm, long YUm, long SafeZUm, long? ActionZUm, long? DispenseZUm)> LoadSetupAsync(WebApplicationFactory<Program> factory)
    {
        var point = await LoadPointAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var liquidClassVersion = await dbContext.LiquidClassVersions
            .Include(x => x.LiquidClassProfile)
            .Where(x => x.LiquidClassProfile != null && x.LiquidClassProfile.EnabledVersionId == x.Id)
            .OrderBy(x => x.VersionNo)
            .FirstAsync();
        return (point.PointCode, liquidClassVersion.Id, point.XUm, point.YUm, point.SafeZUm, point.ActionZUm, point.DispenseZUm);
    }

    private static async Task<PointSetup> LoadPointAsync(WebApplicationFactory<Program> factory, string? pointType = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var query = dbContext.CoordinatePoints
            .Include(x => x.CoordinateProfileVersion)
            .Where(x => x.IsEnabled
                && x.CoordinateProfileVersion != null
                && x.CoordinateProfileVersion.IsActive);
        if (!string.IsNullOrWhiteSpace(pointType))
        {
            query = query.Where(x => x.PointType == pointType);
        }

        var point = await query.OrderBy(x => x.PointCode).FirstOrDefaultAsync();
        if (point is null && pointType is "WashOuter" or "WashInner")
        {
            var prefix = pointType == "WashOuter" ? "WashOuter" : "WashInner";
            point = await dbContext.CoordinatePoints
                .Include(x => x.CoordinateProfileVersion)
                .Where(x => x.IsEnabled
                    && x.CoordinateProfileVersion != null
                    && x.CoordinateProfileVersion.IsActive
                    && x.PointCode.StartsWith(prefix))
                .OrderBy(x => x.PointCode)
                .FirstAsync();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE coordinate_points SET point_type = {pointType} WHERE id = {point.Id}");
            point.PointType = pointType;
        }

        Assert.NotNull(point);
        var calibratedXUm = point.CalibratedXUm ?? point.PresetXUm ?? 123_000;
        var calibratedYUm = point.CalibratedYUm ?? point.PresetYUm ?? 456_000;
        var safeZUm = point.SafeZUm ?? 20_000;
        if (point.CalibratedXUm is null || point.CalibratedYUm is null || point.SafeZUm is null)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE coordinate_points SET calibrated_x_um = {calibratedXUm}, calibrated_y_um = {calibratedYUm}, safe_z_um = {safeZUm} WHERE id = {point.Id}");
            point.CalibratedXUm = calibratedXUm;
            point.CalibratedYUm = calibratedYUm;
            point.SafeZUm = safeZUm;
        }

        return new PointSetup(
            point.PointCode,
            point.CalibratedXUm!.Value,
            point.CalibratedYUm!.Value,
            point.SafeZUm!.Value,
            point.LiquidDetectZUm ?? point.CalibratedZUm ?? point.SafeZUm,
            point.DispenseZUm ?? point.CalibratedZUm ?? point.SafeZUm,
            point.CalibratedZUm);
    }

    private static async Task ClearCoordinateColumnAsync(WebApplicationFactory<Program> factory, string pointCode, string columnName)
    {
        Assert.Contains(columnName, new[] { "calibrated_x_um", "calibrated_y_um", "safe_z_um" });
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var point = await dbContext.CoordinatePoints
            .Include(x => x.CoordinateProfileVersion)
            .SingleAsync(x => x.PointCode == pointCode && x.CoordinateProfileVersion != null && x.CoordinateProfileVersion.IsActive);
        switch (columnName)
        {
            case "calibrated_x_um":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE coordinate_points SET calibrated_x_um = NULL WHERE id = {point.Id}");
                break;
            case "calibrated_y_um":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE coordinate_points SET calibrated_y_um = NULL WHERE id = {point.Id}");
                break;
            case "safe_z_um":
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE coordinate_points SET safe_z_um = NULL WHERE id = {point.Id}");
                break;
        }
    }

    private static async Task AssertEngineeringWriteOnceAsync(
        WebApplicationFactory<Program> factory,
        string commandId,
        string operationType,
        string auditAction,
        string expectedStatus = DeviceCommandStatus.Completed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var operation = await dbContext.PipettingOperations.SingleAsync(x => x.DeviceCommandExecutionId == commandId);
        Assert.Equal(operationType, operation.OperationType);
        Assert.Equal(expectedStatus, operation.Status);
        Assert.Equal(1, await dbContext.AuditLogs.CountAsync(x => x.Action == auditAction && x.EntityId == operation.Id));
        Assert.False(await dbContext.AuditLogs.AnyAsync(x => x.Action.StartsWith("atomic.action.") && x.Message.Contains(commandId)));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Dictionary<string, string?>? overrides = null,
        IRobotArmProcessActionService? processActionService = null)
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-engineering-pipetting-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}",
            ["MachineExecutor:LeasePath"] = Path.Combine(Path.GetDirectoryName(databasePath)!, $"machine-executor-{Guid.NewGuid():N}.lock"),
            ["Safety:LogDirectory"] = Path.Combine(Path.GetDirectoryName(databasePath)!, "logs"),
            ["Device:Mode"] = DeviceModes.Mock,
            ["Device:StartupInitialization:Enabled"] = "false"
        };
        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var pair in settings)
            {
                builder.UseSetting(pair.Key, pair.Value);
            }

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            if (processActionService is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IRobotArmProcessActionService>();
                    services.AddSingleton(processActionService);
                });
            }
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
            reason = $"engineering pipetting test {suffix}",
            target = "engineering-pipetting"
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

    private sealed record PointSetup(
        string PointCode,
        long XUm,
        long YUm,
        long SafeZUm,
        long? ActionZUm,
        long? DispenseZUm,
        long? CalibratedZUm);

    private sealed record ProcessCall(RobotMoveToXYAtSafeHeightRequest Move, object Action);

    private sealed class RecordingProcessActionService : IRobotArmProcessActionService
    {
        public List<ProcessCall> SourceCalls { get; } = [];
        public List<ProcessCall> TargetCalls { get; } = [];
        public List<ProcessCall> MixCalls { get; } = [];
        public List<ProcessCall> InnerWashCalls { get; } = [];
        public List<ProcessCall> OuterWashCalls { get; } = [];
        public RobotArmProcessFailureStage? FailureStage { get; set; }
        public string FailureMessage { get; set; } = "process action failed";
        public bool ThrowOperationCanceled { get; set; }
        public int TotalCallCount => SourceCalls.Count + TargetCalls.Count + MixCalls.Count + InnerWashCalls.Count + OuterWashCalls.Count;

        public Task<RobotArmProcessActionResult> MoveToSourceAndTakeLiquidAsync(
            RobotMoveToXYAtSafeHeightRequest move,
            TakeLiquidRequest action,
            CancellationToken cancellationToken = default) =>
            RecordAsync(SourceCalls, move, action);

        public Task<RobotArmProcessActionResult> MoveToTargetAndDispenseLiquidAsync(
            RobotMoveToXYAtSafeHeightRequest move,
            DispenseLiquidRequest action,
            CancellationToken cancellationToken = default) =>
            RecordAsync(TargetCalls, move, action);

        public Task<RobotArmProcessActionResult> MoveToMixAndPrepareMixAsync(
            RobotMoveToXYAtSafeHeightRequest move,
            PrepareMixRequest action,
            CancellationToken cancellationToken = default) =>
            RecordAsync(MixCalls, move, action);

        public Task<RobotArmProcessActionResult> MoveToInnerWashAndWashInnerAsync(
            RobotMoveToXYAtSafeHeightRequest move,
            WashInnerRequest action,
            CancellationToken cancellationToken = default) =>
            RecordAsync(InnerWashCalls, move, action);

        public Task<RobotArmProcessActionResult> MoveToOuterWashAndWashOuterAsync(
            RobotMoveToXYAtSafeHeightRequest move,
            WashOuterRequest action,
            CancellationToken cancellationToken = default) =>
            RecordAsync(OuterWashCalls, move, action);

        private Task<RobotArmProcessActionResult> RecordAsync<TAction>(
            List<ProcessCall> calls,
            RobotMoveToXYAtSafeHeightRequest move,
            TAction action)
        {
            calls.Add(new ProcessCall(move, action!));
            if (ThrowOperationCanceled)
            {
                throw new OperationCanceledException();
            }

            return Task.FromResult(FailureStage is null
                ? RobotArmProcessActionResult.Succeeded()
                : RobotArmProcessActionResult.Failed(FailureStage.Value, FailureMessage));
        }
    }
}
