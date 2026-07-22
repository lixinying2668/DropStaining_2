namespace Stainer.Web.Infrastructure.Web;

using Microsoft.Extensions.Hosting;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Infrastructure.Health;

public static partial class WebHostEndpointExtensions
{
    private static void MapDeviceAndOperationsEndpoints(WebApplication app)
    {
        app.MapGet("/api/system/info", async (DeviceModeService deviceModeService, CancellationToken cancellationToken) =>
            Results.Ok(new
            {
                app = "Stainer ASP.NET Core Web Host",
                uiHost = "ASP.NET Core",
                pythonRuntimeRequired = false,
                deviceMode = await deviceModeService.GetStatusAsync(cancellationToken),
                dataSource = "SQLite formal data source; runtime DeviceMode is configuration controlled.",
                timeUtc = DateTimeOffset.UtcNow
            }));
        app.MapGet("/api/device-mode", async (DeviceModeService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetStatusAsync(cancellationToken)));
        app.MapPost("/api/device-mode/change", async (HttpContext context, DeviceModeChangeRequest request, UserSessionService sessionService, DeviceModeService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.RequestModeChangeAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/device/state", async (HttpContext context, UserSessionService sessionService, DeviceControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.GetStateAsync(cancellationToken));
            }));
        app.MapGet("/api/thermal/state", async (HttpContext context, UserSessionService sessionService, ThermalControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.GetStateAsync(true, cancellationToken));
            }));
        app.MapGet("/api/thermal/telemetry", async (HttpContext context, int? take, UserSessionService sessionService, ThermalControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.ListTelemetryAsync(take ?? 200, cancellationToken));
            }));
        app.MapPost("/api/thermal/points/{drawerCode}/{slotNo:int}", async (HttpContext context, string drawerCode, int slotNo, SetThermalPointRequest request, UserSessionService sessionService, ThermalControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.SetPointAsync(drawerCode, slotNo, request, actor, cancellationToken));
            }));
        app.MapPost("/api/thermal/boards/{drawerCode}", async (HttpContext context, string drawerCode, SetThermalBoardRequest request, UserSessionService sessionService, ThermalControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.SetBoardAsync(drawerCode, request, actor, cancellationToken));
            }));
        app.MapPost("/api/thermal/cooling", async (HttpContext context, SetCoolingRequest request, UserSessionService sessionService, ThermalControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.SetCoolingAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/thermal/faults", async (HttpContext context, ConfigureThermalFaultRequest request, UserSessionService sessionService, ThermalControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ConfigureFaultAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/thermal/faults/clear", async (HttpContext context, ClearThermalFaultRequest request, UserSessionService sessionService, ThermalControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ClearFaultAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/fluidics/state", async (HttpContext context, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.GetStateAsync(cancellationToken));
            }));
        app.MapGet("/api/fluidics/telemetry", async (HttpContext context, int? take, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.ListTelemetryAsync(take ?? 200, cancellationToken));
            }));
        app.MapPost("/api/fluidics/pumps/run", async (HttpContext context, RunPumpRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.RunPumpAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/pumps/stop", async (HttpContext context, StopPumpRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.StopPumpAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/wash", async (HttpContext context, WashTargetRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.WashAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/wash-stop", async (HttpContext context, StopWashRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.StopWashAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/mixers/{drawerCode}/start", async (HttpContext context, string drawerCode, MixerCommandRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.StartMixerAsync(drawerCode, request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/mixers/{drawerCode}/complete", async (HttpContext context, string drawerCode, MixerCommandRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.CompleteMixerAsync(drawerCode, request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/mixers/{drawerCode}/stop", async (HttpContext context, string drawerCode, MixerCommandRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.StopMixerAsync(drawerCode, request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/liquid-levels", async (HttpContext context, SetLiquidLevelRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.SetLiquidLevelAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/level-thresholds", async (HttpContext context, SetLiquidThresholdRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.SetLiquidThresholdAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/faults", async (HttpContext context, ConfigureFluidicsFaultRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ConfigureFaultAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/fluidics/faults/clear", async (HttpContext context, ClearFluidicsFaultRequest request, UserSessionService sessionService, FluidicsControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ClearFaultAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/water-supply/state", async (HttpContext context, UserSessionService sessionService, WaterSupplyControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.GetStateAsync(cancellationToken));
            }));
        app.MapGet("/api/water-supply/channels/{channelNo:int}", async (HttpContext context, int channelNo, UserSessionService sessionService, WaterSupplyControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.GetChannelAsync(channelNo, cancellationToken));
            }));
        app.MapPost("/api/water-supply/channels/{channelNo:int}/target-temperature", async (HttpContext context, int channelNo, SetWaterSupplyTargetTemperatureRequest request, UserSessionService sessionService, WaterSupplyControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.SetTargetTemperatureAsync(channelNo, request, actor, cancellationToken));
            }));
        app.MapPost("/api/water-supply/channels/{channelNo:int}/flow", async (HttpContext context, int channelNo, SetWaterSupplyFlowRequest request, UserSessionService sessionService, WaterSupplyControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.SetFlowAsync(channelNo, request, actor, cancellationToken));
            }));
        app.MapPost("/api/water-supply/channels/{channelNo:int}/outlet", async (HttpContext context, int channelNo, SetWaterSupplyOutletRequest request, UserSessionService sessionService, WaterSupplyControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.SetOutletAsync(channelNo, request, actor, cancellationToken));
            }));
        app.MapPost("/api/water-supply/faults", async (HttpContext context, ConfigureWaterSupplyFaultRequest request, UserSessionService sessionService, WaterSupplyControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ConfigureFaultAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/water-supply/faults/clear", async (HttpContext context, ClearWaterSupplyFaultRequest request, UserSessionService sessionService, WaterSupplyControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ClearFaultAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device/mock-faults", async (HttpContext context, ConfigureMockDeviceFaultRequest request, UserSessionService sessionService, DeviceControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ConfigureFaultAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device/mock-faults/clear", async (HttpContext context, ClearMockDeviceFaultRequest request, UserSessionService sessionService, DeviceControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ClearFaultsAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device/reagent-scanner/qr/reset", async (HttpContext context, ReagentQrCommandRequest request, UserSessionService sessionService, ReagentQrScannerDeviceOperationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.ResetAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device/reagent-scanner/qr/start", async (HttpContext context, ReagentQrCommandRequest request, UserSessionService sessionService, ReagentQrScannerDeviceOperationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.StartAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device/reagent-scanner/qr/text", async (HttpContext context, ReagentQrCommandRequest request, UserSessionService sessionService, ReagentQrScannerDeviceOperationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.ReadTextAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device/reagent-scanner/qr/status", async (HttpContext context, ReagentQrCommandRequest request, UserSessionService sessionService, ReagentQrScannerDeviceOperationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.GetStatusAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device/reagent-scanner/qr/report", async (HttpContext context, ReportReagentQrTextRequest request, UserSessionService sessionService, ReagentQrScannerDeviceOperationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.ReportTextAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device/reagent-scanner/qr/clear", async (HttpContext context, ReagentQrCommandRequest request, UserSessionService sessionService, ReagentQrScannerDeviceOperationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.ClearTextAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/device-initialization", async (HttpContext context, UserSessionService sessionService, DeviceInitializationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.GetLatestAsync(cancellationToken));
            }));
        app.MapPost("/api/device-initialization", async (HttpContext context, StartDeviceInitializationRequest request, UserSessionService sessionService, DeviceInitializationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.InitializeAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device-initialization/{initializationRunId}/retry", async (HttpContext context, string initializationRunId, RetryDeviceInitializationRequest request, UserSessionService sessionService, DeviceInitializationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.RetryAsync(initializationRunId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/prechecks", async (HttpContext context, PrecheckRunRequest request, UserSessionService sessionService, DevicePrecheckService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.RunAllAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/prechecks/{checkId}", async (HttpContext context, string checkId, PrecheckRunRequest request, UserSessionService sessionService, DevicePrecheckService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.RunOneAsync(checkId, request, actor, cancellationToken));
            }));
        app.MapGet("/api/executor/lease", (MachineExecutorLeaseService service) => Results.Ok(service.GetStatus()));
        app.MapPost("/api/startup/recovery", async (HttpContext context, UserSessionService sessionService, StartupRecoveryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.RecoverAsync(cancellationToken));
            }));
        app.MapGet("/api/prehardware-readiness", async (HttpContext context, UserSessionService sessionService, PreHardwareReadinessService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.VerifyAsync(createBackup: false, cancellationToken));
            }));
        app.MapGet("/api/database/maintenance", async (HttpContext context, UserSessionService sessionService, DatabaseMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.CheckAsync(cancellationToken));
            }));
        app.MapPost("/api/database/backup", async (HttpContext context, DatabaseBackupRequest request, UserSessionService sessionService, DatabaseMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.BackupAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/database/restore-request", async (HttpContext context, DatabaseRestoreRequest request, UserSessionService sessionService, DatabaseMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.RequestRestoreAsync(request, actor, cancellationToken));
            }));
    }
}
