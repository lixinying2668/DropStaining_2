namespace Stainer.Web.Infrastructure.Web;

using Stainer.Web.Application.Services;
using Stainer.Web.Application.Requests;

public static class WebHostEndpointExtensions
{
    private static readonly string[] PageRoutes =
    [
        "/",
        "/login",
        "/dashboard",
        "/samples",
        "/reagents",
        "/run",
        "/alerts",
        "/history",
        "/configure",
        "/engineer",
        "/admin"
    ];

    public static void MapStainerWebHostEndpoints(this WebApplication app)
    {
        foreach (var route in PageRoutes)
        {
            var capturedRoute = route;
            app.MapGet(capturedRoute, (LegacyUiPageRenderer renderer) => renderer.Render(capturedRoute));
        }

        app.MapGet("/api/system/info", (MockRuntimeStore store) => Results.Ok(store.SystemInfo()));
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
        app.MapGet("/api/protocols", async (WorkflowQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListProtocolCompatAsync(cancellationToken)));
        app.MapGet("/api/reagents/catalog", async (ReagentQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListCatalogAsync(cancellationToken)));
        app.MapGet("/api/reagents/rack", async (ReagentQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListRackAsync(cancellationToken)));
        app.MapGet("/api/engineering/layout", async (EngineeringQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetLayoutAsync(cancellationToken)));
        app.MapGet("/api/engineering/coordinate-profiles", async (EngineeringQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListCoordinateProfilesAsync(cancellationToken)));
        app.MapGet("/api/engineering/liquid-classes", async (EngineeringQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListLiquidClassesAsync(cancellationToken)));
        app.MapGet("/api/dab", (MockRuntimeStore store, int? slideCount) => Results.Ok(store.GetDab(slideCount)));
        app.MapGet("/api/logs", (MockRuntimeStore store) =>
        {
            var state = store.GetState();
            return Results.Ok(new { state.Logs, state.Alarms });
        });

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
        app.MapPost("/api/reagents/scan-confirm", async (HttpContext context, ConfirmReagentScanRequest request, UserSessionService sessionService, ReagentScanWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["operator", "admin"], cancellationToken);
                return Results.Ok(await service.ConfirmScanAsync(request, actor, cancellationToken));
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

        app.MapPost("/api/system/initialize", (MockRuntimeStore store) => Results.Ok(store.Initialize()));
        app.MapPost("/api/system/reset", (MockRuntimeStore store) => Results.Ok(store.Reset()));
        app.MapPost("/api/samples/scan", (MockRuntimeStore store, int? count) => Results.Ok(store.ScanSamples(count ?? 8)));
        app.MapPost("/api/reagents/scan", (MockRuntimeStore store) => Results.Ok(store.ScanReagents()));
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
}
