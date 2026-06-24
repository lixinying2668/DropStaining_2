namespace Stainer.Web.Infrastructure.Web;

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
        app.MapGet("/api/state", (MockRuntimeStore store) => Results.Ok(store.GetState()));
        app.MapGet("/api/users", (MockRuntimeStore store) => Results.Ok(store.GetUsers()));
        app.MapGet("/api/protocols", (MockRuntimeStore store) => Results.Ok(store.GetProtocols()));
        app.MapGet("/api/dab", (MockRuntimeStore store, int? slideCount) => Results.Ok(store.GetDab(slideCount)));
        app.MapGet("/api/logs", (MockRuntimeStore store) =>
        {
            var state = store.GetState();
            return Results.Ok(new { state.Logs, state.Alarms });
        });

        app.MapPost("/api/login", (MockRuntimeStore store, LoginRequest request) =>
        {
            var user = store.Authenticate(request.Username, request.Password, request.Role);
            return user is null
                ? Results.Json(new { detail = "Invalid username, password or role" }, statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(new { ok = true, user, redirect = "/dashboard" });
        });

        app.MapPost("/api/logout", (MockRuntimeStore store) =>
        {
            store.Logout();
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/system/initialize", (MockRuntimeStore store) => Results.Ok(store.Initialize()));
        app.MapPost("/api/system/reset", (MockRuntimeStore store) => Results.Ok(store.Reset()));
        app.MapPost("/api/samples/scan", (MockRuntimeStore store, int? count) => Results.Ok(store.ScanSamples(count ?? 8)));
        app.MapPost("/api/reagents/scan", (MockRuntimeStore store) => Results.Ok(store.ScanReagents()));
        app.MapPost("/api/run/start", (MockRuntimeStore store) => Results.Ok(store.RunAction("start")));
        app.MapPost("/api/run/pause", (MockRuntimeStore store) => Results.Ok(store.RunAction("pause")));
        app.MapPost("/api/run/resume", (MockRuntimeStore store) => Results.Ok(store.RunAction("resume")));
        app.MapPost("/api/run/stop", (MockRuntimeStore store) => Results.Ok(store.RunAction("stop")));
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
}
