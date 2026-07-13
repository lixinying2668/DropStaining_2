namespace Stainer.Web.Infrastructure.Web;

using Microsoft.Extensions.Hosting;

public static partial class WebHostEndpointExtensions
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
        "/alarms",
        "/history",
        "/configure",
        "/engineer",
        "/admin",
        "/management"
    ];

    private static void MapPageEndpoints(WebApplication app, bool legacyRuntimeCompatibilityEnabled)
    {
        // /control-console 已由 MapTwinEndpoints 接管（直接返回数字孪生 HTML），不再作为旧版页面渲染。
        var pageRoutes = legacyRuntimeCompatibilityEnabled
            ? PageRoutes.Concat(["/mock-timeline"])
            : PageRoutes;

        foreach (var route in pageRoutes)
        {
            var capturedRoute = route;
            app.MapGet(capturedRoute, (LegacyUiPageRenderer renderer) => renderer.Render(capturedRoute));
        }
    }
}
