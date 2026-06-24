using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class RunControlService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    MachineExecutor executor)
{
    public Task<RunCommandResponse> StartAsync(string runId, RunCommandRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.start", request, actor, async () => await executor.EnqueueStartAsync(runId, cancellationToken), "Start command queued.", cancellationToken);
    }

    public Task<RunCommandResponse> PauseAsync(string runId, RunCommandRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.pause", request, actor, () => { executor.RequestPause(runId); return Task.CompletedTask; }, "Pause requested.", cancellationToken);
    }

    public Task<RunCommandResponse> ResumeAsync(string runId, RunCommandRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.resume", request, actor, async () => await executor.EnqueueResumeAsync(runId, cancellationToken), "Resume command queued.", cancellationToken);
    }

    public Task<RunCommandResponse> StopAsync(string runId, RunCommandRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.stop", request, actor, () => { executor.RequestStop(runId); return Task.CompletedTask; }, "Stop requested.", cancellationToken);
    }

    public Task<RunCommandResponse> InjectFaultAsync(string runId, InjectFaultRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.inject_fault", request, actor, () => { executor.RequestFault(runId, request.Message); return Task.CompletedTask; }, "Fault injection requested.", cancellationToken);
    }

    public Task<RunCommandResponse> RedoCurrentMajorStepAsync(string runId, RedoMajorStepRequest request, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(runId, request.CommandId, "run.redo_current_major_step", request, actor, async () => await executor.EnqueueRedoAsync(runId, request.Reason, cancellationToken), "Redo command queued.", cancellationToken);
    }

    private Task<RunCommandResponse> EnqueueAsync(
        string runId,
        string commandId,
        string operation,
        object request,
        AuthenticatedUser actor,
        Func<Task> enqueue,
        string message,
        CancellationToken cancellationToken)
    {
        return idempotencyService.RunAsync(
            commandId,
            operation,
            new { runId, request },
            actor,
            async () =>
            {
                var run = await dbContext.MachineRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
                if (run is null)
                {
                    throw new BusinessRuleException("run_not_found", "Run was not found.", StatusCodes.Status404NotFound);
                }

                await enqueue();
                return new CommandExecutionResult<RunCommandResponse>(
                    new RunCommandResponse(true, commandId, false, runId, run.Status, message),
                    "MachineRun",
                    runId);
            },
            cancellationToken);
    }
}
