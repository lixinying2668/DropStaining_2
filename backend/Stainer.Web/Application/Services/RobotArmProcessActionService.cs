using Microsoft.Extensions.Logging;
using Stainer.Web.Application.Requests;

namespace Stainer.Web.Application.Services;

public interface IRobotArmProcessActionService
{
    Task<RobotArmProcessActionResult> MoveToSourceAndTakeLiquidAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        TakeLiquidRequest action,
        CancellationToken cancellationToken = default);

    Task<RobotArmProcessActionResult> MoveToTargetAndDispenseLiquidAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        DispenseLiquidRequest action,
        CancellationToken cancellationToken = default);

    Task<RobotArmProcessActionResult> MoveToMixAndPrepareMixAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        PrepareMixRequest action,
        CancellationToken cancellationToken = default);

    Task<RobotArmProcessActionResult> MoveToInnerWashAndWashInnerAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        WashInnerRequest action,
        CancellationToken cancellationToken = default);

    Task<RobotArmProcessActionResult> MoveToOuterWashAndWashOuterAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        WashOuterRequest action,
        CancellationToken cancellationToken = default);
}

public sealed class RobotArmProcessActionService(
    MotionControlService motionControlService,
    IRobotArmAtomicActionService atomicActionService,
    ILogger<RobotArmProcessActionService> logger) : IRobotArmProcessActionService
{
    public Task<RobotArmProcessActionResult> MoveToSourceAndTakeLiquidAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        TakeLiquidRequest action,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            nameof(MoveToSourceAndTakeLiquidAsync),
            move,
            ct => atomicActionService.TakeLiquidAsync(action, ct),
            cancellationToken);

    public Task<RobotArmProcessActionResult> MoveToTargetAndDispenseLiquidAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        DispenseLiquidRequest action,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            nameof(MoveToTargetAndDispenseLiquidAsync),
            move,
            ct => atomicActionService.DispenseLiquidAsync(action, ct),
            cancellationToken);

    public Task<RobotArmProcessActionResult> MoveToMixAndPrepareMixAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        PrepareMixRequest action,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            nameof(MoveToMixAndPrepareMixAsync),
            move,
            ct => atomicActionService.PrepareMixAsync(action, ct),
            cancellationToken);

    public Task<RobotArmProcessActionResult> MoveToInnerWashAndWashInnerAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        WashInnerRequest action,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            nameof(MoveToInnerWashAndWashInnerAsync),
            move,
            ct => atomicActionService.WashInnerAsync(action, ct),
            cancellationToken);

    public Task<RobotArmProcessActionResult> MoveToOuterWashAndWashOuterAsync(
        RobotMoveToXYAtSafeHeightRequest move,
        WashOuterRequest action,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            nameof(MoveToOuterWashAndWashOuterAsync),
            move,
            ct => atomicActionService.WashOuterAsync(action, ct),
            cancellationToken);

    private async Task<RobotArmProcessActionResult> RunAsync(
        string processAction,
        RobotMoveToXYAtSafeHeightRequest move,
        Func<CancellationToken, Task<RobotArmAtomicActionResult>> action,
        CancellationToken cancellationToken)
    {
        MotionDeviceResult moveResult;
        try
        {
            moveResult = await motionControlService.MoveToXYAtSafeHeightAsync(move, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Robot arm process action {ProcessAction} failed during movement.", processAction);
            return RobotArmProcessActionResult.Failed(RobotArmProcessFailureStage.Move, exception.Message);
        }

        if (!moveResult.Ok)
        {
            return RobotArmProcessActionResult.Failed(RobotArmProcessFailureStage.Move, moveResult.Message);
        }

        try
        {
            var actionResult = await action(cancellationToken);
            return actionResult.Ok
                ? RobotArmProcessActionResult.Succeeded()
                : RobotArmProcessActionResult.Failed(RobotArmProcessFailureStage.Action, actionResult.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Robot arm process action {ProcessAction} failed during atomic action.", processAction);
            return RobotArmProcessActionResult.Failed(RobotArmProcessFailureStage.Action, exception.Message);
        }
    }
}

public sealed record RobotArmProcessActionResult(
    bool Success,
    RobotArmProcessFailureStage FailureStage,
    string? ErrorMessage)
{
    public static RobotArmProcessActionResult Succeeded() =>
        new(true, RobotArmProcessFailureStage.None, null);

    public static RobotArmProcessActionResult Failed(RobotArmProcessFailureStage failureStage, string errorMessage) =>
        new(false, failureStage, errorMessage);
}

public enum RobotArmProcessFailureStage
{
    None = 0,
    Move = 1,
    Action = 2
}
