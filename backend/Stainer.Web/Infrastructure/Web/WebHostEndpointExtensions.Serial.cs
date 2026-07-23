namespace Stainer.Web.Infrastructure.Web;

using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Infrastructure.Devices;

public static partial class WebHostEndpointExtensions
{
    // 调试模块 COM 设置 → 后端数据库。admin-only，对齐 /api/scanners 的鉴权口径。
    // PUT 请求体手动 ReadFromJsonAsync 读取，不在 handler 上声明复杂 body 参数，
    // 以规避 minimal API 对该请求类型（全简单属性 record）的 body 推断报错。
    private static void MapSerialConnectionEndpoints(WebApplication app)
    {
        // 枚举本机真实可用串口名（只读名字，不开端口）。前端 COM 下拉据此显示真实端口，回退 COM1-4。
        app.MapGet("/api/engineering/serial-ports", async (HttpContext context, UserSessionService sessionService, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(SerialPortEnumerator.ListAvailablePortNames());
            }));

        app.MapGet("/api/engineering/serial-config/{deviceKey}", async (HttpContext context, string deviceKey, UserSessionService sessionService, SerialConnectionConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.GetAsync(deviceKey, cancellationToken));
            }));

        app.MapPut("/api/engineering/serial-config/{deviceKey}", async (HttpContext context, string deviceKey, UserSessionService sessionService, SerialConnectionConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                var request = await context.Request.ReadFromJsonAsync<SaveSerialConnectionRequest>(cancellationToken);
                if(request is null)
                {
                    return Results.BadRequest(new { code = "request_body_required", detail = "Request body is required." });
                }

                // deviceKey 来自路由（权威），request 仅含可写的串口参数。
                return Results.Ok(await service.SaveAsync(deviceKey, request, actor, cancellationToken));
            }));

        // 调试栏「通信测试」：真实打开指定 COM 口收发测试字节（admin-only，原始字节不经主控协议/白名单）。
        // body 手动 ReadFromJsonAsync，规避 minimal API 对全简单属性 record 的 body 推断报错（对齐 PUT serial-config）。
        app.MapPost("/api/engineering/serial-debug/exchange", async (HttpContext context, UserSessionService sessionService, SerialDebugService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                var request = await context.Request.ReadFromJsonAsync<SerialDebugExchangeRequest>(cancellationToken);
                if (request is null)
                {
                    return Results.BadRequest(new { code = "request_body_required", detail = "Request body is required." });
                }

                return Results.Ok(await service.ExchangeAsync(request, cancellationToken));
            }));
    }
}
