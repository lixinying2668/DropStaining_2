namespace Stainer.Web.Infrastructure.Web;

using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;

public static partial class WebHostEndpointExtensions
{
    // 通用坐标点现场移动（玻片/A-B液/配液/清洗位等）→ SOCON Bridge MoveX/Y/Z。
    // admin + 工程写会话 + 危险确认三重门禁，镜像试剂位 move-test（ReagentPositionConfig.cs:27-39）。
    private static void MapCoordinatePointEndpoints(WebApplication app)
    {
        app.MapPost("/api/engineering/coordinate-points/{pointCode}/move-test", async (HttpContext context, string pointCode, MoveCoordinatePointHardwareRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, CoordinatePointHardwareService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(
                    actor,
                    request.CommandId,
                    request.Reason,
                    request.Target ?? $"coordinate-point:{pointCode}:{request.TargetZ}",
                    request.DangerousOperationConfirmed,
                    cancellationToken);
                return Results.Ok(await service.MoveAsync(pointCode, request, actor, cancellationToken));
            }));
    }
}
