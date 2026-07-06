namespace Stainer.Web.Infrastructure.Web;

using Microsoft.Extensions.Hosting;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Infrastructure.Health;

public static partial class WebHostEndpointExtensions
{
    private static void MapEngineeringEndpoints(WebApplication app)
    {
        app.MapGet("/api/engineering/session", async (HttpContext context, UserSessionService sessionService, EngineeringSessionService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                var session = await service.GetCurrentAsync(actor, cancellationToken);
                return session is null ? Results.NotFound() : Results.Ok(session);
            }));
        app.MapPost("/api/engineering/session", async (HttpContext context, StartEngineeringSessionRequest request, UserSessionService sessionService, EngineeringSessionService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.StartAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/session/revoke", async (HttpContext context, EndEngineeringSessionRequest request, UserSessionService sessionService, EngineeringSessionService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.RevokeAsync(request, actor, cancellationToken));
            }));
        app.MapGet("/api/engineering/diagnostics/device-state", async (HttpContext context, UserSessionService sessionService, EngineeringDiagnosticService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.GetDeviceStateAsync(cancellationToken));
            }));
        app.MapGet("/api/engineering/diagnostics/command-log", async (HttpContext context, UserSessionService sessionService, EngineeringDiagnosticService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListCommandLogAsync(context.Request.Query, cancellationToken));
            }));
        app.MapGet("/api/engineering/diagnostics/command-log.csv", async (HttpContext context, UserSessionService sessionService, EngineeringDiagnosticService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return ToCsvFile(await service.ExportCommandLogAsync(context.Request.Query, actor, cancellationToken));
            }));
        app.MapGet("/api/engineering/diagnostics/errors", async (HttpContext context, UserSessionService sessionService, EngineeringDiagnosticService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListErrorsAsync(context.Request.Query, cancellationToken));
            }));
        app.MapGet("/api/engineering/diagnostics/mock-communications", async (HttpContext context, UserSessionService sessionService, EngineeringDiagnosticService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListMockCommunicationsAsync(context.Request.Query, cancellationToken));
            }));
        app.MapGet("/api/engineering/diagnostics/mock-communications.csv", async (HttpContext context, UserSessionService sessionService, EngineeringDiagnosticService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return ToCsvFile(await service.ExportMockCommunicationsAsync(context.Request.Query, actor, cancellationToken));
            }));
        app.MapGet("/api/engineering/layout", async (HttpContext context, UserSessionService sessionService, EngineeringQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.GetLayoutAsync(cancellationToken));
            }));
        app.MapGet("/api/engineering/coordinate-profiles", async (HttpContext context, UserSessionService sessionService, EngineeringQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListCoordinateProfilesAsync(cancellationToken));
            }));
        app.MapGet("/api/engineering/coordinate-profile-versions/{versionId}", async (HttpContext context, string versionId, UserSessionService sessionService, EngineeringConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                var version = await service.GetCoordinateVersionAsync(versionId, cancellationToken);
                return version is null ? Results.NotFound() : Results.Ok(version);
            }));
        app.MapGet("/api/engineering/coordinate-profile-versions/{versionId}/diff", async (HttpContext context, string versionId, string? sourceVersionId, UserSessionService sessionService, EngineeringConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.DiffCoordinateVersionAsync(versionId, sourceVersionId, cancellationToken));
            }));
        app.MapPost("/api/engineering/coordinate-profile-versions", async (HttpContext context, CreateCoordinateProfileVersionRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, CoordinateProfileLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"coordinate-profile:{request.ProfileCode}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.CreateVersionAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/coordinate-profile-versions/{versionId}/publish", async (HttpContext context, string versionId, PublishCoordinateProfileVersionRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, CoordinateProfileLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"coordinate-version:{versionId}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.PublishAsync(versionId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/coordinate-profile-versions/{versionId}/activate", async (HttpContext context, string versionId, ActivateCoordinateProfileVersionRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, CoordinateProfileLifecycleService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"coordinate-version:{versionId}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.ActivateAsync(versionId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/coordinate-profile-versions/{versionId}/deactivate", async (HttpContext context, string versionId, DeactivateCoordinateProfileVersionRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, EngineeringConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"coordinate-version:{versionId}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.DeactivateCoordinateVersionAsync(versionId, request, actor, cancellationToken));
            }));
        app.MapGet("/api/engineering/liquid-classes", async (HttpContext context, UserSessionService sessionService, EngineeringQueryService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ListLiquidClassesAsync(cancellationToken));
            }));
        app.MapGet("/api/engineering/liquid-class-versions/{versionId}", async (HttpContext context, string versionId, UserSessionService sessionService, EngineeringConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                var version = await service.GetLiquidClassVersionAsync(versionId, cancellationToken);
                return version is null ? Results.NotFound() : Results.Ok(version);
            }));
        app.MapGet("/api/engineering/liquid-class-versions/{versionId}/diff", async (HttpContext context, string versionId, string? sourceVersionId, UserSessionService sessionService, EngineeringConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.DiffLiquidClassVersionAsync(versionId, sourceVersionId, cancellationToken));
            }));
        app.MapPost("/api/engineering/liquid-class-versions/{versionId}/publish", async (HttpContext context, string versionId, PublishLiquidClassVersionRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, EngineeringWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"liquid-class-version:{versionId}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.PublishLiquidClassVersionAsync(versionId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/liquid-class-versions/{versionId}/enable", async (HttpContext context, string versionId, EnableLiquidClassVersionRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, EngineeringWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"liquid-class-version:{versionId}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.EnableLiquidClassVersionAsync(versionId, request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/liquid-class-versions/{versionId}/disable", async (HttpContext context, string versionId, DisableLiquidClassVersionRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, EngineeringConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"liquid-class-version:{versionId}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.DisableLiquidClassVersionAsync(versionId, request, actor, cancellationToken));
            }));
        app.MapGet("/api/engineering/config/export", async (HttpContext context, UserSessionService sessionService, EngineeringConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.ExportAsync(cancellationToken));
            }));
        app.MapPost("/api/engineering/config/import/preview", async (HttpContext context, PreviewEngineeringConfigImportRequest request, UserSessionService sessionService, EngineeringConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.PreviewImportAsync(request, cancellationToken));
            }));
        app.MapPost("/api/engineering/config/import", async (HttpContext context, ApplyEngineeringConfigImportRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, EngineeringConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"config-import:{request.ConfigType}:{request.TargetCode}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.ApplyImportAsync(request, actor, cancellationToken));
            }));

        app.MapPost("/api/engineering/coordinates/digital-twin/import/preview", async (HttpContext context, PreviewDigitalTwinCoordinateImportRequest request, UserSessionService sessionService, DigitalTwinCoordinateImportService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                return Results.Ok(await service.PreviewAsync(request, cancellationToken));
            }));
        app.MapPost("/api/engineering/coordinates/digital-twin/import", async (HttpContext context, ApplyDigitalTwinCoordinateImportRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, DigitalTwinCoordinateImportService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"coordinate-digital-twin:{request.VersionLabel ?? DigitalTwinCoordinateImportService.DefaultVersionLabel}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.ApplyAsync(request, actor, cancellationToken));
            }));

        app.MapPost("/api/engineering/coordinate-points/calibrate", async (HttpContext context, CalibrateCoordinatePointRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, EngineeringWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"coordinate-point:{request.ProfileCode}:{request.PointCode}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.CalibrateCoordinatePointAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/liquid-classes", async (HttpContext context, SaveLiquidClassRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, EngineeringWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"liquid-class:{request.Code}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.SaveLiquidClassAsync(request, actor, cancellationToken));
            }));
        app.MapPost("/api/engineering/device-profiles", async (HttpContext context, SaveDeviceProfileRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, EngineeringWriteService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["engineer", "admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(actor, request.CommandId, request.Reason, request.Target ?? $"device-profile:{request.Code}", request.DangerousOperationConfirmed, cancellationToken);
                return Results.Ok(await service.SaveDeviceProfileAsync(request, actor, cancellationToken));
            }));
    }
}
