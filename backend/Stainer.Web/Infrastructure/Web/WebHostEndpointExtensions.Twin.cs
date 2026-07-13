using Microsoft.Extensions.Hosting;
using Stainer.Web.Application.Services;
using Stainer.Web.Infrastructure.Twin;

namespace Stainer.Web.Infrastructure.Web;

// 数字孪生（移植自 stainer_twin_fastapi）的页面与 API。
// /control-console 直接返回孪生 HTML（无旧版外壳、无 iframe），/api/twin/* 与其它只读 API 一致加角色网关。
public static partial class WebHostEndpointExtensions
{
    private static void MapTwinEndpoints(WebApplication app)
    {
        var twinHtmlPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "twin", "index.html");

        app.MapGet("/control-console", () => Results.File(twinHtmlPath, "text/html; charset=utf-8"));

        app.MapGet("/api/twin/snapshot", async (HttpContext context, UserSessionService sessionService, TwinSnapshotService service, CancellationToken cancellationToken) =>
        {
            _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
            return Results.Json(service.BuildSnapshot(), TwinSnapshotService.JsonOptions);
        });

        app.MapGet("/api/twin/value/{controlId}", async (HttpContext context, string controlId, UserSessionService sessionService, TwinSnapshotService service, CancellationToken cancellationToken) =>
        {
            _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
            var value = service.GetControlValue(controlId);
            return Results.Json(new Dictionary<string, object?>
            {
                ["control_id"] = controlId,
                ["value"] = value,
            }, TwinSnapshotService.JsonOptions);
        });

        app.MapGet("/api/twin/mapping", async (HttpContext context, HttpRequest request, UserSessionService sessionService, TwinSnapshotService service, CancellationToken cancellationToken) =>
        {
            _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
            var status = (string?)request.Query["status"];
            return Results.Json(service.GetMappingRows(string.IsNullOrEmpty(status) ? null : status), TwinSnapshotService.JsonOptions);
        });

        app.MapGet("/api/twin/mapping.csv", async (HttpContext context, UserSessionService sessionService, TwinSnapshotService service, CancellationToken cancellationToken) =>
        {
            _ = await sessionService.RequireAnyRoleAsync(context, ["operator", "engineer", "admin"], cancellationToken);
            return Results.File(service.GetMappingCsv(), "text/csv; charset=utf-8", "frontend_db_mapping.csv");
        });
    }
}
