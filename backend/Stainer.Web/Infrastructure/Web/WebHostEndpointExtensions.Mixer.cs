namespace Stainer.Web.Infrastructure.Web;

using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;

public static partial class WebHostEndpointExtensions
{
    // 配置模块「清洗混匀 → 混匀参数」→ 后端数据库。admin-only，对齐 /api/engineering/precision-calibration 的鉴权口径。
    // PUT 请求体手动 ReadFromJsonAsync 读取（规避 minimal API 对全简单属性 record 的 body 推断报错）。
    private static void MapMixerParameterEndpoints(WebApplication app)
    {
        app.MapGet("/api/engineering/mixer-parameters/{drawerCode}", async (HttpContext context, string drawerCode, UserSessionService sessionService, MixerParameterConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.GetAsync(drawerCode, cancellationToken));
            }));

        app.MapPut("/api/engineering/mixer-parameters/{drawerCode}", async (HttpContext context, string drawerCode, UserSessionService sessionService, MixerParameterConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                var request = await context.Request.ReadFromJsonAsync<SaveMixerParameterRequest>(cancellationToken);
                if(request is null)
                {
                    return Results.BadRequest(new { code = "request_body_required", detail = "Request body is required." });
                }

                // drawerCode 来自路由（权威），request 仅含可写的混匀参数。
                return Results.Ok(await service.SaveAsync(drawerCode, request, actor, cancellationToken));
            }));
    }
}
