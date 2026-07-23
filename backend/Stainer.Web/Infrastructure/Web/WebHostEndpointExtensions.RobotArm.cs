namespace Stainer.Web.Infrastructure.Web;

using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;

public static partial class WebHostEndpointExtensions
{
    // 机械臂工程隔离测试：XY 安全平移 + 工艺原子动作。admin + 工程写会话 + 危险确认三重门禁，
    // 镜像试剂位 move-test（ReagentPositionConfig.cs:27-39）。供工程端点在不必跑完整染色工作流的前提下验证双针运动。
    private static void MapRobotArmEndpoints(WebApplication app)
    {
        app.MapPost("/api/engineering/robot-arm/move-test", async (HttpContext context, MoveRobotArmHardwareRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, RobotArmEngineeringService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(
                    actor,
                    request.CommandId,
                    request.Reason ?? string.Empty,
                    request.Target ?? $"robot-arm:move:{request.PointCode}:{request.TargetXUm}:{request.TargetYUm}",
                    request.DangerousOperationConfirmed,
                    cancellationToken);
                return Results.Ok(await service.MoveAsync(request, actor, cancellationToken));
            }));

        app.MapPost("/api/engineering/robot-arm/atomic-action", async (HttpContext context, RunRobotArmAtomicHardwareRequest request, UserSessionService sessionService, EngineeringSessionService engineeringSessionService, RobotArmEngineeringService service, CancellationToken cancellationToken) =>
            await ExecuteBusinessAsync(async () =>
            {
                var actor = await sessionService.RequireAnyRoleAsync(context, ["admin"], cancellationToken);
                await engineeringSessionService.RequireWriteSessionAsync(
                    actor,
                    request.CommandId,
                    request.Reason ?? string.Empty,
                    request.Target ?? $"robot-arm:atomic:{request.Action}",
                    request.DangerousOperationConfirmed,
                    cancellationToken);
                return Results.Ok(await service.RunAtomicActionAsync(request, actor, cancellationToken));
            }));
    }
}
