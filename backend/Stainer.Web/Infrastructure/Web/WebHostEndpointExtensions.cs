namespace Stainer.Web.Infrastructure.Web;

using Microsoft.Extensions.Hosting;
using Stainer.Web.Application.Services;
using Stainer.Web.Application.Requests;
using Stainer.Web.Infrastructure.Health;

public static class WebHostEndpointExtensions
{
    private static readonly string[] PageRoutes =
    [
        "/",
        "/login",
        "/control-console",
        "/dashboard",
        "/samples",
        "/reagents",
        "/run",
        "/alerts",
        "/alarms",
        "/history",
        "/configure",
        "/engineer",
        "/admin",
        "/management"
    ];

    public static void MapStainerWebHostEndpoints(this WebApplication app)
    {
        app.MapHub<MachineHub>(MachineHub.Route);

        var pageRoutes = app.Environment.IsProduction()
            ? PageRoutes
            : PageRoutes.Concat(["/mock-timeline"]);

        foreach (var route in pageRoutes)
        {
            var capturedRoute = route;
            app.MapGet(capturedRoute, (LegacyUiPageRenderer renderer) => renderer.Render(capturedRoute));
        }

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
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.RequestModeChangeAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/device/state", async (HttpContext context, UserSessionService sessionService, DeviceControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.GetStateAsync(cancellationToken));
            }));
        app.MapPost("/api/device/mock-faults", async (HttpContext context, ConfigureMockDeviceFaultRequest request, UserSessionService sessionService, DeviceControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ConfigureFaultAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device/mock-faults/clear", async (HttpContext context, ClearMockDeviceFaultRequest request, UserSessionService sessionService, DeviceControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ClearFaultsAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/device-initialization", async (HttpContext context, UserSessionService sessionService, DeviceInitializationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.GetLatestAsync(cancellationToken));
            }));
        app.MapPost("/api/device-initialization", async (HttpContext context, StartDeviceInitializationRequest request, UserSessionService sessionService, DeviceInitializationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.InitializeAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/device-initialization/{initializationRunId}/retry", async (HttpContext context, string initializationRunId, RetryDeviceInitializationRequest request, UserSessionService sessionService, DeviceInitializationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.RetryAsync(initializationRunId, request, actor, cancellationToken));
            }));
        app.MapGet("/api/executor/lease", (MachineExecutorLeaseService service) => Results.Ok(service.GetStatus()));
        app.MapPost("/api/startup/recovery", async (HttpContext context, UserSessionService sessionService, StartupRecoveryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.RecoverAsync(cancellationToken));
            }));
        app.MapGet("/api/prehardware-readiness", async (HttpContext context, UserSessionService sessionService, PreHardwareReadinessService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.VerifyAsync(createBackup: false, cancellationToken));
            }));
        app.MapGet("/api/database/maintenance", async (HttpContext context, UserSessionService sessionService, DatabaseMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.CheckAsync(cancellationToken));
            }));
        app.MapPost("/api/database/backup", async (HttpContext context, DatabaseBackupRequest request, UserSessionService sessionService, DatabaseMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.BackupAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/database/restore-request", async (HttpContext context, DatabaseRestoreRequest request, UserSessionService sessionService, DatabaseMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.RequestRestoreAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/state", async (RuntimePageBridgeService bridge, CancellationToken cancellationToken) =>
            Results.Ok(await bridge.GetStateAsync(cancellationToken)));
        app.MapGet("/api/current-user", async (HttpContext context, UserSessionService sessionService, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var user = await sessionService.GetCurrentUserAsync(context, cancellationToken);
                return user is null
                    ? Results.Json(new { code = "authentication_required", detail = "Login is required." }, statusCode: StatusCodes.Status401Unauthorized)
                    : Results.Ok(UserSessionService.ToCurrentUser(user));
            }));
        app.MapGet("/api/users", async (UserQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListUsersAsync(cancellationToken)));
        app.MapGet("/api/roles", async (UserQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListRolesAsync(cancellationToken)));
        app.MapGet("/api/workflows", async (WorkflowQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));
        app.MapGet("/api/workflows/{id}", async (string id, WorkflowQueryService service, CancellationToken cancellationToken) =>
        {
            var workflow = await service.GetAsync(id, cancellationToken);
            return workflow is null ? Results.NotFound() : Results.Ok(workflow);
        });
        app.MapPost("/api/workflows", async (HttpContext context, CreateWorkflowRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.CreateWorkflowAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/workflows/{workflowId}/versions", async (HttpContext context, string workflowId, CreateWorkflowVersionRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.CreateWorkflowVersionAsync(workflowId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/workflows/drafts", async (HttpContext context, CreateWorkflowDraftRequest request, UserSessionService sessionService, WorkflowWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.CreateDraftAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/workflow-versions/{workflowVersionId}", async (string workflowVersionId, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var version = await service.GetVersionAsync(workflowVersionId, cancellationToken);
                return version is null ? Results.NotFound() : Results.Ok(version);
            }));
        app.MapPut("/api/workflow-versions/{workflowVersionId}", async (HttpContext context, string workflowVersionId, UpdateWorkflowVersionRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.UpdateVersionAsync(workflowVersionId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/workflow-versions/{workflowVersionId}/copy-draft", async (HttpContext context, string workflowVersionId, CopyWorkflowVersionDraftRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.CopyVersionAsDraftAsync(workflowVersionId, request, actor, cancellationToken));
            }));
        app.MapGet("/api/workflow-versions/{workflowVersionId}/steps", async (string workflowVersionId, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var version = await service.GetVersionAsync(workflowVersionId, cancellationToken);
                return version is null ? Results.NotFound() : Results.Ok(version.Steps);
            }));
        app.MapPost("/api/workflow-versions/{workflowVersionId}/steps", async (HttpContext context, string workflowVersionId, SaveWorkflowStepRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.AddStepAsync(workflowVersionId, request, actor, cancellationToken));
            }));
        app.MapPut("/api/workflow-versions/{workflowVersionId}/steps/{stepId}", async (HttpContext context, string workflowVersionId, string stepId, SaveWorkflowStepRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.UpdateStepAsync(workflowVersionId, stepId, request, actor, cancellationToken));
            }));
        app.MapDelete("/api/workflow-versions/{workflowVersionId}/steps/{stepId}", async (HttpContext context, string workflowVersionId, string stepId, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                var commandId = context.Request.Query["commandId"].ToString();
                return Results.Ok(await service.DeleteStepAsync(workflowVersionId, stepId, commandId, actor, cancellationToken));
            }));
        app.MapPost("/api/workflow-versions/{workflowVersionId}/steps/{stepId}/move-up", async (HttpContext context, string workflowVersionId, string stepId, RunCommandRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.MoveStepAsync(workflowVersionId, stepId, true, request.CommandId, actor, cancellationToken));
            }));
        app.MapPost("/api/workflow-versions/{workflowVersionId}/steps/{stepId}/move-down", async (HttpContext context, string workflowVersionId, string stepId, RunCommandRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.MoveStepAsync(workflowVersionId, stepId, false, request.CommandId, actor, cancellationToken));
            }));
        app.MapGet("/api/workflow-versions/{workflowVersionId}/reagent-requirements", async (string workflowVersionId, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var version = await service.GetVersionAsync(workflowVersionId, cancellationToken);
                return version is null ? Results.NotFound() : Results.Ok(version.ReagentRequirements);
            }));
        app.MapPost("/api/workflow-versions/{workflowVersionId}/reagent-requirements", async (HttpContext context, string workflowVersionId, SaveWorkflowReagentRequirementRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.AddRequirementAsync(workflowVersionId, request, actor, cancellationToken));
            }));
        app.MapPut("/api/workflow-versions/{workflowVersionId}/reagent-requirements/{id}", async (HttpContext context, string workflowVersionId, string id, SaveWorkflowReagentRequirementRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.UpdateRequirementAsync(workflowVersionId, id, request, actor, cancellationToken));
            }));
        app.MapDelete("/api/workflow-versions/{workflowVersionId}/reagent-requirements/{id}", async (HttpContext context, string workflowVersionId, string id, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                var commandId = context.Request.Query["commandId"].ToString();
                return Results.Ok(await service.DeleteRequirementAsync(workflowVersionId, id, commandId, actor, cancellationToken));
            }));
        app.MapPost("/api/workflow-versions/{workflowVersionId}/reagent-requirements/recalculate", async (HttpContext context, string workflowVersionId, RunCommandRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.RecalculateRequirementsAsync(workflowVersionId, request.CommandId, actor, cancellationToken));
            }));
        app.MapGet("/api/workflow-versions/{workflowVersionId}/publish-validation", async (string workflowVersionId, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () => Results.Ok(await service.ValidatePublishAsync(workflowVersionId, cancellationToken))));
        app.MapPost("/api/workflow-versions/{workflowVersionId}/publish", async (HttpContext context, string workflowVersionId, PublishWorkflowVersionRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.PublishAsync(workflowVersionId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/workflow-versions/{workflowVersionId}/set-default", async (HttpContext context, string workflowVersionId, SetDefaultWorkflowVersionRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.SetDefaultAsync(workflowVersionId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/workflow-versions/{workflowVersionId}/retire", async (HttpContext context, string workflowVersionId, RetireWorkflowVersionRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.RetireAsync(workflowVersionId, request, actor, cancellationToken));
            }));
        app.MapGet("/api/primary-antibody-mappings", async (WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListPrimaryAntibodyMappingsAsync(cancellationToken)));
        app.MapPost("/api/primary-antibody-mappings", async (HttpContext context, CreatePrimaryAntibodyMappingRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.CreateMappingAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/primary-antibody-mappings/{id}/enable", async (HttpContext context, string id, ChangePrimaryAntibodyMappingStateRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.SetMappingEnabledAsync(id, request, actor, true, cancellationToken));
            }));
        app.MapPost("/api/primary-antibody-mappings/{id}/disable", async (HttpContext context, string id, ChangePrimaryAntibodyMappingStateRequest request, UserSessionService sessionService, WorkflowMaintenanceService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.SetMappingEnabledAsync(id, request, actor, false, cancellationToken));
            }));
        app.MapGet("/api/protocols", async (WorkflowQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListProtocolCompatAsync(cancellationToken)));
        app.MapPost("/api/channel-batches/workflow-selection", async (HttpContext context, SelectChannelWorkflowRequest request, UserSessionService sessionService, ChannelBatchWorkflowService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.SelectWorkflowAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/channel-batches/experiment-type-selection", async (HttpContext context, SelectChannelExperimentTypeRequest request, UserSessionService sessionService, ChannelBatchWorkflowService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.SelectExperimentTypeAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/channel-batches/active", async (HttpContext context, EnsureChannelBatchRequest request, UserSessionService sessionService, ChannelBatchWorkflowService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.EnsureActiveBatchAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/reagents/catalog", async (ReagentQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListCatalogAsync(cancellationToken)));
        app.MapGet("/api/reagents/rack", async (ReagentQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListRackAsync(cancellationToken)));
        app.MapGet("/api/reagents/scan-sessions/overview", async (ReagentQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetScanSessionOverviewAsync(cancellationToken)));
        app.MapGet("/api/engineering/layout", async (EngineeringQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetLayoutAsync(cancellationToken)));
        app.MapGet("/api/engineering/coordinate-profiles", async (EngineeringQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListCoordinateProfilesAsync(cancellationToken)));
        app.MapGet("/api/engineering/liquid-classes", async (EngineeringQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListLiquidClassesAsync(cancellationToken)));
        app.MapGet("/api/dab", (MockRuntimeStore store, int? slideCount) => Results.Ok(store.GetDab(slideCount)));
        app.MapGet("/api/dab/positions", async (HttpContext context, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListPositionsAsync(cancellationToken));
            }));
        app.MapGet("/api/dab/batches/{batchId}", async (HttpContext context, string batchId, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.GetBatchAsync(batchId, cancellationToken));
            }));
        app.MapPost("/api/dab/batches", async (HttpContext context, CreateDabBatchRequest request, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.CreateBatchAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/dab/batches/{batchId}/preparation/start", async (HttpContext context, string batchId, DabBatchCommandRequest request, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.StartPreparationAsync(batchId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/dab/batches/{batchId}/preparation/complete", async (HttpContext context, string batchId, CompleteDabPreparationRequest request, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.CompletePreparationAsync(batchId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/dab/batches/{batchId}/consume", async (HttpContext context, string batchId, ConsumeDabBatchRequest request, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ConsumeAsync(batchId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/dab/batches/{batchId}/expire", async (HttpContext context, string batchId, DabBatchCommandRequest request, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.MarkExpiredAsync(batchId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/dab/batches/{batchId}/fail", async (HttpContext context, string batchId, FailDabBatchRequest request, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.FailAsync(batchId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/dab/batches/{batchId}/cleaning/start", async (HttpContext context, string batchId, DabBatchCommandRequest request, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.StartCleaningAsync(batchId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/dab/batches/{batchId}/cleaning/confirm", async (HttpContext context, string batchId, DabBatchCommandRequest request, UserSessionService sessionService, DabLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ConfirmCleaningAsync(batchId, request, actor, cancellationToken));
            }));
        app.MapGet("/api/logs", (MockRuntimeStore store) =>
        {
            var state = store.GetState();
            return Results.Ok(new { state.Logs, state.Alarms });
        });
        app.MapGet("/api/history/runs", async (HttpContext context, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListRunsAsync(context.Request.Query, cancellationToken));
            }));
        app.MapGet("/api/history/runs/{machineRunId}", async (HttpContext context, string machineRunId, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                var detail = await service.GetRunDetailAsync(machineRunId, cancellationToken);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            }));
        app.MapGet("/api/history/reagent-consumptions", async (HttpContext context, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListReagentConsumptionsAsync(context.Request.Query, cancellationToken));
            }));
        app.MapGet("/api/alarms", async (HttpContext context, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListAlarmsAsync(context.Request.Query, cancellationToken));
            }));
        app.MapPost("/api/alarms/{alarmId}/acknowledge", async (HttpContext context, string alarmId, AcknowledgeAlarmRequest request, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.AcknowledgeAlarmAsync(alarmId, request, actor, cancellationToken));
            }));
        app.MapGet("/api/audit/logs", async (HttpContext context, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListAuditLogsAsync(context.Request.Query, cancellationToken));
            }));
        app.MapGet("/api/history/export/runs", async (HttpContext context, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return ToCsvFile(await service.ExportRunsAsync(context.Request.Query, actor, cancellationToken));
            }));
        app.MapGet("/api/history/export/reagent-consumptions", async (HttpContext context, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return ToCsvFile(await service.ExportReagentConsumptionsAsync(context.Request.Query, actor, cancellationToken));
            }));
        app.MapGet("/api/alarms/export", async (HttpContext context, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return ToCsvFile(await service.ExportAlarmsAsync(context.Request.Query, actor, cancellationToken));
            }));
        app.MapGet("/api/audit/export", async (HttpContext context, UserSessionService sessionService, TraceabilityQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return ToCsvFile(await service.ExportAuditLogsAsync(context.Request.Query, actor, cancellationToken));
            }));

        app.MapPost("/api/login", async (HttpContext context, UserSessionService sessionService, MockRuntimeStore store, LoginRequest request, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var response = await sessionService.LoginAsync(request.Username, request.Password, request.Role, context, cancellationToken);
                store.SetActiveUser(new MockUser(response.User.Username, response.User.ActiveRole, response.User.DisplayName, true));
                return Results.Ok(response);
            }));

        app.MapPost("/api/logout", async (HttpContext context, UserSessionService sessionService, MockRuntimeStore store, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                await sessionService.LogoutAsync(context, cancellationToken);
                store.Logout();
                return Results.Ok(new { ok = true });
            }));

        app.MapPost("/api/users", async (HttpContext context, CreateUserRequest request, UserSessionService sessionService, UserManagementService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.CreateUserAsync(request, actor, cancellationToken));
            }));
        app.MapPut("/api/users/{id}/display-name", async (HttpContext context, string id, UpdateUserDisplayNameRequest request, UserSessionService sessionService, UserManagementService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.UpdateDisplayNameAsync(id, request, actor, cancellationToken));
            }));
        app.MapPut("/api/users/{id}/enabled", async (HttpContext context, string id, SetUserEnabledRequest request, UserSessionService sessionService, UserManagementService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.SetEnabledAsync(id, request, actor, cancellationToken));
            }));
        app.MapPut("/api/users/{id}/password", async (HttpContext context, string id, ResetUserPasswordRequest request, UserSessionService sessionService, UserManagementService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.ResetPasswordAsync(id, request, actor, cancellationToken));
            }));
        app.MapPut("/api/users/{id}/roles", async (HttpContext context, string id, SetUserRolesRequest request, UserSessionService sessionService, UserManagementService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.SetRolesAsync(id, request, actor, cancellationToken));
            }));
        app.MapDelete("/api/users/{id}", async (HttpContext context, string id, string commandId, UserSessionService sessionService, UserManagementService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireRoleAsync(context, "admin", cancellationToken);
                return Results.Ok(await service.DeleteUserAsync(id, commandId, actor, cancellationToken));
            }));

        app.MapPost("/api/tasks/he", async (HttpContext context, CreateHeTaskRequest request, UserSessionService sessionService, TaskCreationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.CreateHeTaskAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/tasks/ihc", async (HttpContext context, CreateIhcTaskRequest request, UserSessionService sessionService, TaskCreationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.CreateIhcTaskAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/lis/mock-query", async (HttpContext context, MockLisQueryRequest request, UserSessionService sessionService, MockLisQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.QueryAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/reagents/scan-confirm", async (HttpContext context, ConfirmReagentScanRequest request, UserSessionService sessionService, ReagentScanWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.ConfirmScanAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/reagents/scan-sessions/start", async (HttpContext context, StartReagentScanSessionRequest request, UserSessionService sessionService, ReagentScanWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.StartSessionAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/reagents/scan-sessions/{scanSessionId}/complete", async (HttpContext context, string scanSessionId, CompleteReagentScanSessionRequest request, UserSessionService sessionService, ReagentScanWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.CompleteSessionAsync(scanSessionId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/coordinate-points/calibrate", async (HttpContext context, CalibrateCoordinatePointRequest request, UserSessionService sessionService, EngineeringWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.CalibrateCoordinatePointAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/liquid-classes", async (HttpContext context, SaveLiquidClassRequest request, UserSessionService sessionService, EngineeringWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.SaveLiquidClassAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/device-profiles", async (HttpContext context, SaveDeviceProfileRequest request, UserSessionService sessionService, EngineeringWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.SaveDeviceProfileAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/run/preflight", async (HttpContext context, UserSessionService sessionService, PreflightValidationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ValidateAsync(cancellationToken));
            }));
        app.MapPost("/api/runs", async (HttpContext context, CreateMachineRunRequest request, UserSessionService sessionService, MachineRunService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.CreateRunAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/runs/current", async (HttpContext context, UserSessionService sessionService, MachineRunQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                var run = await service.GetCurrentAsync(cancellationToken);
                return run is null ? Results.NotFound() : Results.Ok(run);
            }));
        app.MapGet("/api/runs/{id}", async (HttpContext context, string id, UserSessionService sessionService, MachineRunQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                var run = await service.GetAsync(id, cancellationToken);
                return run is null ? Results.NotFound() : Results.Ok(run);
            }));
        app.MapPost("/api/runs/{id}/start", async (HttpContext context, string id, RunCommandRequest request, UserSessionService sessionService, RunControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.StartAsync(id, request, actor, cancellationToken));
            }));
        app.MapPost("/api/runs/{id}/pause", async (HttpContext context, string id, RunCommandRequest request, UserSessionService sessionService, RunControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.PauseAsync(id, request, actor, cancellationToken));
            }));
        app.MapPost("/api/runs/{id}/resume", async (HttpContext context, string id, RunCommandRequest request, UserSessionService sessionService, RunControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.ResumeAsync(id, request, actor, cancellationToken));
            }));
        app.MapPost("/api/runs/{id}/stop", async (HttpContext context, string id, RunCommandRequest request, UserSessionService sessionService, RunControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.StopAsync(id, request, actor, cancellationToken));
            }));
        app.MapPost("/api/runs/{id}/fault", async (HttpContext context, string id, InjectFaultRequest request, UserSessionService sessionService, RunControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.InjectFaultAsync(id, request, actor, cancellationToken));
            }));
        app.MapPost("/api/runs/{id}/redo-current-major-step", async (HttpContext context, string id, RedoMajorStepRequest request, UserSessionService sessionService, RunControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.RedoCurrentMajorStepAsync(id, request, actor, cancellationToken));
            }));

        app.MapPost("/api/system/initialize", async (HttpContext context, StartDeviceInitializationRequest request, UserSessionService sessionService, DeviceInitializationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
                return Results.Ok(await service.InitializeAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/system/reset", () => Results.Json(
            new { code = "legacy_runtime_reset_disabled", message = "Legacy MockRuntimeStore reset is disabled." },
            statusCode: StatusCodes.Status410Gone));
        app.MapPost("/api/samples/scan", async (HttpContext context, int? count, UserSessionService sessionService, SampleScanWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                var body = await ReadOptionalJsonAsync<MockSampleScanRequest>(context, cancellationToken);
                var request = body ?? new MockSampleScanRequest($"sample-scan-{Guid.NewGuid():N}", count ?? 8, "Mixed", null, null);
                return Results.Ok(await service.ScanAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/reagents/scan", async (HttpContext context, UserSessionService sessionService, ReagentScannerMockService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                var request = await ReadOptionalJsonAsync<MockReagentScanRequest>(context, cancellationToken)
                    ?? new MockReagentScanRequest($"reagent-scan-{Guid.NewGuid():N}", "all", null, null, "Mixed", null, null);
                return Results.Ok(await service.ScanAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/mock-demo-data/seed", async (HttpContext context, RunCommandRequest request, UserSessionService sessionService, MockDemoDataSeeder seeder, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await seeder.SeedAsync(request.CommandId, actor, cancellationToken));
            }));
        app.MapPost("/api/mock-demo-data/reset", async (HttpContext context, ResetMockDemoDataRequest request, UserSessionService sessionService, MockDemoDataSeeder seeder, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await seeder.ResetAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/run/start", async (HttpContext context, RuntimePageBridgeService bridge, UserSessionService sessionService, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await bridge.RunActionAsync("start", actor, cancellationToken));
            }));
        app.MapPost("/api/run/pause", async (HttpContext context, RuntimePageBridgeService bridge, UserSessionService sessionService, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await bridge.RunActionAsync("pause", actor, cancellationToken));
            }));
        app.MapPost("/api/run/resume", async (HttpContext context, RuntimePageBridgeService bridge, UserSessionService sessionService, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await bridge.RunActionAsync("resume", actor, cancellationToken));
            }));
        app.MapPost("/api/run/stop", async (HttpContext context, RuntimePageBridgeService bridge, UserSessionService sessionService, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await bridge.RunActionAsync("stop", actor, cancellationToken));
            }));
        app.MapPost("/api/slides/configure", (MockRuntimeStore store, SlideConfigureRequest request) =>
        {
            try
            {
                return Results.Ok(store.ConfigureSlide(request));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { detail = ex.Message }, statusCode: StatusCodes.Status404NotFound);
            }
        });
        app.MapPost("/api/run/add-slide", (MockRuntimeStore store) => Results.Ok(store.GetState()));
        app.MapPost("/api/engineer/command", (MockRuntimeStore store, EngineerCommandRequest request) => Results.Ok(store.EngineerCommand(request)));

        app.MapFallback((HttpContext context, LegacyUiPageRenderer renderer) =>
        {
            return context.Request.Path.StartsWithSegments("/api")
                ? Results.NotFound()
                : renderer.Render("/dashboard");
        });
    }

    private static async Task<IResult> ExecuteBusinessAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BusinessSelectionRequiredException ex)
        {
            return Results.Json(ex.Response, statusCode: StatusCodes.Status409Conflict);
        }
        catch (BusinessRuleException ex)
        {
            return Results.Json(new { code = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static async Task<T?> ReadOptionalJsonAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is null or 0)
        {
            return default;
        }

        if (context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return default;
        }

        return await context.Request.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private static IResult ToCsvFile(CsvExportResult export)
    {
        return Results.File(export.Content, export.ContentType, export.FileName);
    }
}
