using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class MachineRunQueryService(StainerDbContext dbContext)
{
    public async Task<MachineRunDetailResponse?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        var run = await dbContext.MachineRuns
            .AsNoTracking()
            .Include(x => x.ChannelBatches)
            .ThenInclude(x => x.SlideTasks)
            .Include(x => x.WorkflowExecutions)
            .ThenInclude(x => x.StepExecutions)
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var alarms = await dbContext.Alarms
            .AsNoTracking()
            .Where(x => x.MachineRunId == runId)
            .ToListAsync(cancellationToken);

        return new MachineRunDetailResponse(
            run.Id,
            run.RunCode,
            run.Status,
            run.CurrentMajorStepCode,
            run.ChannelBatches
                .OrderBy(x => x.DrawerCode)
                .Select(x => new ChannelBatchResponse(
                    x.Id,
                    x.DrawerCode,
                    x.Status,
                    x.SlideTasks
                        .OrderBy(s => s.SlotCode)
                        .Select(s => new SlideTaskResponse(s.Id, s.SlotCode, s.TaskType, s.Status))
                        .ToList()))
                .ToList(),
            run.WorkflowExecutions
                .OrderBy(x => x.SlideTaskId)
                .Select(x => new WorkflowExecutionResponse(
                    x.Id,
                    x.SlideTaskId,
                    x.Status,
                    x.StepExecutions
                        .OrderBy(s => s.StepNo)
                        .Select(s => new WorkflowStepExecutionResponse(
                            s.Id,
                            s.StepNo,
                            s.MajorStepCode,
                            s.StepName,
                            s.ActionType,
                            s.ReagentCode,
                            s.VolumeUl,
                            s.Status,
                            s.RedoCount))
                        .ToList()))
                .ToList(),
            alarms
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new AlarmResponse(x.Id, x.Code, x.Severity, x.Message, x.Status))
                .ToList());
    }

    public async Task<MachineRunDetailResponse?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var runs = await dbContext.MachineRuns.AsNoTracking().ToListAsync(cancellationToken);
        var run = runs
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        return run is null ? null : await GetAsync(run.Id, cancellationToken);
    }
}
