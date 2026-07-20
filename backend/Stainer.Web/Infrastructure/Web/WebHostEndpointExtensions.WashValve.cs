namespace Stainer.Web.Infrastructure.Web;

using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;

public static partial class WebHostEndpointExtensions
{
    // 配置模块「清洗混匀 → 样本清洗/阀状态」单行配置 → 后端数据库。admin-only。
    // PUT 手动 ReadFromJsonAsync（规避全简单属性 record 的 body 推断报错）。
    private static void MapWashValveConfigEndpoints(WebApplication app)
    {
        app.MapGet("/api/engineering/wash-valve-config", async (HttpContext context, UserSessionService sessionService, WashValveConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                _ = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                return Results.Ok(await service.GetAsync(cancellationToken));
            }));

        app.MapPut("/api/engineering/wash-valve-config", async (HttpContext context, UserSessionService sessionService, WashValveConfigService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                var request = await context.Request.ReadFromJsonAsync<SaveWashValveConfigRequest>(cancellationToken);
                if(request is null)
                {
                    return Results.BadRequest(new { code = "request_body_required", detail = "Request body is required." });
                }

                return Results.Ok(await service.SaveAsync(request, actor, cancellationToken));
            }));
    }
}
