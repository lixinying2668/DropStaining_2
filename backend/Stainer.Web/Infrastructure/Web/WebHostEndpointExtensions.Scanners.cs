namespace Stainer.Web.Infrastructure.Web;

using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;

public static partial class WebHostEndpointExtensions
{
    private static void MapScannerConfigurationEndpoints(WebApplication app)
    {
        app.MapGet("/api/scanners", async (HttpContext context, UserSessionService sessionService, ScannerConfigurationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ListProfilesAsync(cancellationToken));
            }));

        app.MapGet("/api/scanners/{id}", async (HttpContext context, string id, UserSessionService sessionService, ScannerConfigurationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                var profile = await service.GetProfileAsync(id, cancellationToken);
                return profile is null ? Results.NotFound() : Results.Ok(profile);
            }));

        app.MapPost("/api/scanners", async (HttpContext context, SaveScannerProfileRequest request, UserSessionService sessionService, ScannerConfigurationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.CreateProfileAsync(request, actor, cancellationToken));
            }));

        app.MapPut("/api/scanners/{id}", async (HttpContext context, string id, SaveScannerProfileRequest request, UserSessionService sessionService, ScannerConfigurationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.UpdateProfileAsync(id, request, actor, cancellationToken));
            }));

        app.MapGet("/api/scanner-regions", async (HttpContext context, string? scannerProfileId, string? regionType, UserSessionService sessionService, ScannerConfigurationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ListRegionsAsync(scannerProfileId, regionType, cancellationToken));
            }));

        app.MapPost("/api/scanner-regions", async (HttpContext context, SaveScannerRegionRequest request, UserSessionService sessionService, ScannerConfigurationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.CreateRegionAsync(request, actor, cancellationToken));
            }));

        app.MapPut("/api/scanner-regions/{id}", async (HttpContext context, string id, SaveScannerRegionRequest request, UserSessionService sessionService, ScannerConfigurationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.UpdateRegionAsync(id, request, actor, cancellationToken));
            }));

        app.MapGet("/api/reagent-coordinate-anchors", async (HttpContext context, string? coordinateProfileId, string? coordinateProfileVersionId, UserSessionService sessionService, ReagentCoordinateAnchorService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ListAnchorsAsync(coordinateProfileId, coordinateProfileVersionId, cancellationToken));
            }));

        app.MapGet("/api/reagent-coordinate-anchors/{id}", async (HttpContext context, string id, UserSessionService sessionService, ReagentCoordinateAnchorService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                var anchor = await service.GetAnchorAsync(id, cancellationToken);
                return anchor is null ? Results.NotFound() : Results.Ok(anchor);
            }));

        app.MapPost("/api/reagent-coordinate-anchors", async (HttpContext context, SaveReagentCoordinateAnchorRequest request, UserSessionService sessionService, ReagentCoordinateAnchorService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.CreateAnchorAsync(request, actor, cancellationToken));
            }));

        app.MapPut("/api/reagent-coordinate-anchors/{id}", async (HttpContext context, string id, SaveReagentCoordinateAnchorRequest request, UserSessionService sessionService, ReagentCoordinateAnchorService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.UpdateAnchorAsync(id, request, actor, cancellationToken));
            }));

        app.MapPost("/api/reagent-coordinate-anchors/preview", async (HttpContext context, GenerateReagentCoordinatesRequest request, UserSessionService sessionService, ReagentCoordinateGenerationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.PreviewAsync(request, cancellationToken));
            }));

        app.MapPost("/api/reagent-coordinate-anchors/generate", async (HttpContext context, GenerateReagentCoordinatesRequest request, UserSessionService sessionService, ReagentCoordinateGenerationService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.GenerateAndSaveAsync(request, actor, cancellationToken));
            }));

        app.MapPost("/api/scanners/{id}/restart", async (HttpContext context, string id, ScannerRestartRequest request, UserSessionService sessionService, ScannerControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.RestartScannerAsync(id, request, actor, cancellationToken));
            }));

        app.MapPost("/api/scanners/{id}/calibration-light/enable", async (HttpContext context, string id, ScannerCalibrationLightRequest request, UserSessionService sessionService, ScannerControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.EnableCalibrationLightAsync(id, request, actor, cancellationToken));
            }));

        app.MapPost("/api/scanners/{id}/calibration-light/disable", async (HttpContext context, string id, ScannerCalibrationLightRequest request, UserSessionService sessionService, ScannerControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.DisableCalibrationLightAsync(id, request, actor, cancellationToken));
            }));

        app.MapPost("/api/scanners/{id}/roi/apply", async (HttpContext context, string id, ScannerRoiRequest request, UserSessionService sessionService, ScannerControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ApplyRoiAsync(id, request, actor, cancellationToken));
            }));

        app.MapPost("/api/scanners/{id}/trigger", async (HttpContext context, string id, ScannerTriggerRequest request, UserSessionService sessionService, ScannerControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.TriggerScannerAsync(id, request, actor, cancellationToken));
            }));

        app.MapPost("/api/scanners/{id}/barcode/read", async (HttpContext context, string id, ScannerBarcodeRequest request, UserSessionService sessionService, ScannerControlService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.ReadBarcodeAsync(id, request, actor, cancellationToken));
            }));
    }
}
