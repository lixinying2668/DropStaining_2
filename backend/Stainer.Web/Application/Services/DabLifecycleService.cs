using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class DabLifecycleService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DabMixPositionResponse>> ListPositionsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.DabMixPositions
            .AsNoTracking()
            .OrderBy(x => x.PositionNo)
            .Select(x => new DabMixPositionResponse(
                x.Id,
                x.Code,
                x.PositionNo,
                x.IsEnabled,
                x.Status,
                x.ActiveDabBatchId,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<DabBatchResponse> GetBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var batch = await BatchQuery(asTracking: false)
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null)
        {
            throw new BusinessRuleException("dab_batch_not_found", "DAB batch was not found.", StatusCodes.Status404NotFound);
        }

        return ToResponse(batch, null, false, "DAB batch loaded.");
    }

    public Task<DabBatchResponse> CreateBatchAsync(
        CreateDabBatchRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "dab.batch.create",
            request,
            actor,
            async () =>
            {
                var taskIds = request.TaskIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (taskIds.Count == 0 || taskIds.Count != request.TaskIds.Count)
                {
                    throw new BusinessRuleException("dab_tasks_invalid", "At least one distinct non-empty taskId is required.");
                }

                if (string.IsNullOrWhiteSpace(request.DabAReagentBottleId)
                    || string.IsNullOrWhiteSpace(request.DabBReagentBottleId)
                    || string.Equals(request.DabAReagentBottleId, request.DabBReagentBottleId, StringComparison.Ordinal))
                {
                    throw new BusinessRuleException("dab_source_bottles_invalid", "Distinct DAB A and DAB B source bottles are required.");
                }

                var tasks = await dbContext.StainingTasks
                    .Include(x => x.WorkflowVersion)
                    .ThenInclude(x => x!.Steps)
                    .Where(x => taskIds.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                if (tasks.Count != taskIds.Count)
                {
                    throw new BusinessRuleException("dab_task_not_found", "One or more DAB tasks were not found.", StatusCodes.Status404NotFound);
                }

                var nonDabTasks = tasks
                    .Where(x => x.WorkflowVersion?.Steps.Any(step =>
                        step.ActionType.Equals("Dab", StringComparison.OrdinalIgnoreCase)) != true)
                    .Select(x => x.Id)
                    .ToList();
                if (nonDabTasks.Count > 0)
                {
                    throw new BusinessRuleException("dab_task_incompatible", "Every selected task must use a workflow containing a DAB step.", StatusCodes.Status409Conflict);
                }

                var alreadyAssigned = await dbContext.DabBatchTasks
                    .Where(x => taskIds.Contains(x.StainingTaskId) && x.DabBatch!.Status != DabBatchStatus.Cleaned)
                    .Select(x => x.StainingTaskId)
                    .ToListAsync(cancellationToken);
                if (alreadyAssigned.Count > 0)
                {
                    throw new BusinessRuleException("dab_task_already_assigned", "One or more tasks already belong to an uncleaned DAB batch.", StatusCodes.Status409Conflict);
                }

                var sourceIds = new[] { request.DabAReagentBottleId.Trim(), request.DabBReagentBottleId.Trim() };
                var bottles = await dbContext.ReagentBottles
                    .Where(x => sourceIds.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                if (bottles.Count != 2)
                {
                    throw new BusinessRuleException("dab_source_bottle_not_found", "DAB A or DAB B source bottle was not found.", StatusCodes.Status404NotFound);
                }

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                if (bottles.Any(x => !x.Status.Equals("Available", StringComparison.OrdinalIgnoreCase)
                    || x.ExpirationDate < today))
                {
                    throw new BusinessRuleException("dab_source_bottle_unavailable", "DAB source bottles must be available and unexpired.", StatusCodes.Status409Conflict);
                }

                DabFormulaVolumes required;
                try
                {
                    required = DabFormula.CalculateRequired(taskIds.Count);
                }
                catch (OverflowException)
                {
                    throw new BusinessRuleException("dab_volume_invalid", "Calculated DAB volume exceeds the supported integer range.");
                }

                var dabABottle = bottles.Single(x => x.Id == sourceIds[0]);
                var dabBBottle = bottles.Single(x => x.Id == sourceIds[1]);

                var position = await SelectPositionAsync(request.PositionCode, cancellationToken);
                var now = DateTimeOffset.UtcNow;
                var batch = new DabBatch
                {
                    DabMixPosition = position,
                    DabMixPositionId = position.Id,
                    PositionCode = position.Code,
                    DabAReagentBottle = dabABottle,
                    DabAReagentBottleId = dabABottle.Id,
                    DabBReagentBottle = dabBBottle,
                    DabBReagentBottleId = dabBBottle.Id,
                    CreatedByUserId = actor.UserId,
                    Status = DabBatchStatus.PendingPreparation,
                    CleaningStatus = DabCleaningStatus.NotRequired,
                    SlideCount = taskIds.Count,
                    TotalRequiredVolumeUl = required.TotalVolumeUl,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                await ReserveBottleSourceAsync(
                    batch,
                    "DabA",
                    dabABottle.ReagentCode,
                    dabABottle.Id,
                    required.DabAVolumeUl,
                    request.CommandId,
                    actor,
                    now,
                    cancellationToken);
                await ReserveBottleSourceAsync(
                    batch,
                    "DabB",
                    dabBBottle.ReagentCode,
                    dabBBottle.Id,
                    required.DabBVolumeUl,
                    request.CommandId,
                    actor,
                    now,
                    cancellationToken);
                batch.ReagentReservations.Add(new ReagentReservation
                {
                    ReagentCode = "WATER",
                    ReservationKind = ReagentReservationKind.DabBatch,
                    SourceRole = "Water",
                    Status = ReagentReservationStatus.Reserved,
                    CommandId = request.CommandId,
                    CreatedByUserId = actor.UserId,
                    RequiredVolumeUl = required.WaterVolumeUl,
                    ReservedVolumeUl = required.WaterVolumeUl,
                    CreatedAtUtc = now
                });
                foreach (var task in tasks.OrderBy(x => x.TaskCode))
                {
                    batch.Tasks.Add(new DabBatchTask
                    {
                        StainingTask = task,
                        StainingTaskId = task.Id,
                        RequiredVolumeUl = DabFormula.VolumePerSlideUl,
                        CreatedAtUtc = now
                    });
                }

                position.Status = DabMixPositionStatus.Occupied;
                position.ActiveDabBatchId = batch.Id;
                position.UpdatedAtUtc = now;
                dbContext.DabBatches.Add(batch);
                AddAudit(actor, request.CommandId, "dab.batch.create", batch, new
                {
                    batch.PositionCode,
                    taskIds,
                    batch.DabAReagentBottleId,
                    batch.DabBReagentBottleId,
                    batch.TotalRequiredVolumeUl,
                    reservations = batch.ReagentReservations.Select(x => new
                    {
                        x.SourceRole,
                        x.ReagentCode,
                        x.ReagentBottleId,
                        x.ReservedVolumeUl
                    }),
                    ratio = "1:1:18"
                }, now);

                return Result(batch, request.CommandId, "DAB batch created and mix position occupied.");
            },
            cancellationToken);
    }

    public Task<DabBatchResponse> StartPreparationAsync(
        string batchId,
        DabBatchCommandRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(batchId, request.CommandId, "dab.batch.preparation.start", request, actor,
            (batch, now) =>
            {
                RequireStatus(batch, DabBatchStatus.PendingPreparation);
                batch.Status = DabBatchStatus.Preparing;
                batch.UpdatedAtUtc = now;
                AddAudit(actor, request.CommandId, "dab.batch.preparation.start", batch, new { batch.PositionCode }, now);
                return "DAB preparation started.";
            }, cancellationToken);
    }

    public Task<DabBatchResponse> CompletePreparationAsync(
        string batchId,
        CompleteDabPreparationRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(batchId, request.CommandId, "dab.batch.preparation.complete", request, actor,
            (batch, now) =>
            {
                RequireStatus(batch, DabBatchStatus.Preparing);
                if (request.ActualPreparedVolumeUl != batch.TotalRequiredVolumeUl)
                {
                    throw new BusinessRuleException("dab_prepared_volume_mismatch", "Actual prepared volume must match the reserved total volume in DAB lifecycle phase 08.");
                }

                DabFormulaVolumes actual;
                try
                {
                    actual = DabFormula.Calculate(request.ActualPreparedVolumeUl);
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new BusinessRuleException("dab_volume_invalid", "Actual prepared volume must be a positive multiple of 20 uL.");
                }

                RequireReservedSources(batch, actual);
                batch.ActualPreparedVolumeUl = actual.TotalVolumeUl;
                batch.DabAVolumeUl = actual.DabAVolumeUl;
                batch.DabBVolumeUl = actual.DabBVolumeUl;
                batch.WaterVolumeUl = actual.WaterVolumeUl;
                batch.UsedVolumeUl = 0;
                batch.RemainingVolumeUl = actual.TotalVolumeUl;
                batch.PreparedAtUtc = now;
                batch.ExpiresAtUtc = now.AddHours(DabFormula.ValidityHours);
                batch.Status = DabBatchStatus.Available;
                batch.UpdatedAtUtc = now;
                AddAudit(actor, request.CommandId, "dab.batch.preparation.complete", batch, new
                {
                    batch.ActualPreparedVolumeUl,
                    batch.DabAVolumeUl,
                    batch.DabBVolumeUl,
                    batch.WaterVolumeUl,
                    batch.PreparedAtUtc,
                    batch.ExpiresAtUtc
                }, now);
                return "DAB preparation completed and is available for three hours.";
            }, cancellationToken);
    }

    public Task<DabBatchResponse> ConsumeAsync(
        string batchId,
        ConsumeDabBatchRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(batchId, request.CommandId, "dab.batch.consume", request, actor,
            (batch, now) =>
            {
                RequireStatus(batch, DabBatchStatus.Available);
                if (batch.ExpiresAtUtc is null || batch.ExpiresAtUtc <= now)
                {
                    throw new BusinessRuleException("dab_batch_expired", "DAB batch has expired and must be marked expired before cleaning.", StatusCodes.Status409Conflict);
                }

                if (request.VolumeUl <= 0 || request.VolumeUl > batch.RemainingVolumeUl)
                {
                    throw new BusinessRuleException("dab_usage_volume_invalid", "Usage volume must be positive and cannot exceed remaining volume.");
                }

                var taskId = string.IsNullOrWhiteSpace(request.StainingTaskId) ? null : request.StainingTaskId.Trim();
                if (taskId is not null && batch.Tasks.All(x => x.StainingTaskId != taskId))
                {
                    throw new BusinessRuleException("dab_task_not_in_batch", "Usage task does not belong to this DAB batch.", StatusCodes.Status409Conflict);
                }

                batch.UsedVolumeUl += request.VolumeUl;
                batch.RemainingVolumeUl -= request.VolumeUl;
                batch.UpdatedAtUtc = now;
                dbContext.DabBatchUsages.Add(new DabBatchUsage
                {
                    DabBatch = batch,
                    StainingTaskId = taskId,
                    CommandId = request.CommandId,
                    CreatedByUserId = actor.UserId,
                    VolumeUl = request.VolumeUl,
                    CreatedAtUtc = now
                });
                if (batch.RemainingVolumeUl == 0)
                {
                    batch.Status = DabBatchStatus.Depleted;
                    RequireCleaning(batch, now);
                }

                AddAudit(actor, request.CommandId, "dab.batch.consume", batch, new
                {
                    request.VolumeUl,
                    taskId,
                    batch.UsedVolumeUl,
                    batch.RemainingVolumeUl,
                    batch.Status
                }, now);
                return batch.Status == DabBatchStatus.Depleted
                    ? "DAB batch depleted; cleaning is required."
                    : "DAB usage recorded.";
            }, cancellationToken);
    }

    public Task<DabBatchResponse> MarkExpiredAsync(
        string batchId,
        DabBatchCommandRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(batchId, request.CommandId, "dab.batch.expire", request, actor,
            (batch, now) =>
            {
                RequireStatus(batch, DabBatchStatus.Available);
                if (batch.ExpiresAtUtc is null || batch.ExpiresAtUtc > now)
                {
                    throw new BusinessRuleException("dab_batch_not_expired", "DAB batch has not reached its expiry time.", StatusCodes.Status409Conflict);
                }

                batch.Status = DabBatchStatus.Expired;
                batch.UpdatedAtUtc = now;
                RequireCleaning(batch, now);
                AddAudit(actor, request.CommandId, "dab.batch.expire", batch, new { batch.ExpiresAtUtc }, now);
                return "DAB batch marked expired; cleaning is required.";
            }, cancellationToken);
    }

    public Task<DabBatchResponse> FailAsync(
        string batchId,
        FailDabBatchRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(batchId, request.CommandId, "dab.batch.fail", request, actor,
            (batch, now) =>
            {
                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    throw new BusinessRuleException("reason_required", "Failure reason is required.");
                }

                var previousStatus = batch.Status;
                if (previousStatus is not (DabBatchStatus.PendingPreparation or DabBatchStatus.Preparing or DabBatchStatus.Available))
                {
                    throw InvalidTransition(batch.Status, DabBatchStatus.Failed);
                }

                batch.Status = DabBatchStatus.Failed;
                batch.UpdatedAtUtc = now;
                RequireManualResolution(batch, now);
                MarkReservationsForFailure(batch, previousStatus, now);
                AddAudit(actor, request.CommandId, "dab.batch.fail", batch, new { reason = request.Reason.Trim() }, now);
                return "DAB batch marked failed; manual resolution is required before the mix position can be released.";
            }, cancellationToken);
    }

    public Task<DabBatchResponse> StartCleaningAsync(
        string batchId,
        DabBatchCommandRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(batchId, request.CommandId, "dab.batch.cleaning.start", request, actor,
            (batch, now) =>
            {
                if (batch.Status is not (DabBatchStatus.Depleted or DabBatchStatus.Expired or DabBatchStatus.Failed or DabBatchStatus.Unknown))
                {
                    throw InvalidTransition(batch.Status, DabBatchStatus.AwaitingCleaning);
                }

                batch.Status = DabBatchStatus.AwaitingCleaning;
                batch.CleaningStatus = DabCleaningStatus.Required;
                batch.UpdatedAtUtc = now;
                batch.DabMixPosition!.Status = DabMixPositionStatus.AwaitingCleaning;
                batch.DabMixPosition.UpdatedAtUtc = now;
                AddAudit(actor, request.CommandId, "dab.batch.cleaning.start", batch, new { batch.PositionCode }, now);
                return "DAB cleaning started.";
            }, cancellationToken);
    }

    public Task<DabBatchResponse> ConfirmCleaningAsync(
        string batchId,
        DabBatchCommandRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(batchId, request.CommandId, "dab.batch.cleaning.confirm", request, actor,
            (batch, now) =>
            {
                RequireStatus(batch, DabBatchStatus.AwaitingCleaning);
                batch.Status = DabBatchStatus.Cleaned;
                batch.CleaningStatus = DabCleaningStatus.Confirmed;
                batch.CleaningConfirmedAtUtc = now;
                batch.UpdatedAtUtc = now;
                batch.DabMixPosition!.Status = DabMixPositionStatus.Available;
                batch.DabMixPosition.ActiveDabBatchId = null;
                batch.DabMixPosition.UpdatedAtUtc = now;
                AddAudit(actor, request.CommandId, "dab.batch.cleaning.confirm", batch, new { batch.PositionCode }, now);
                return "DAB cleaning confirmed and mix position released.";
            }, cancellationToken);
    }

    private Task<DabBatchResponse> MutateAsync<TRequest>(
        string batchId,
        string commandId,
        string operation,
        TRequest request,
        AuthenticatedUser actor,
        Func<DabBatch, DateTimeOffset, string> mutate,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        return idempotencyService.RunAsync(
            commandId,
            operation,
            new { batchId, request },
            actor,
            async () =>
            {
                var batch = await BatchQuery(asTracking: true)
                    .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
                if (batch is null)
                {
                    throw new BusinessRuleException("dab_batch_not_found", "DAB batch was not found.", StatusCodes.Status404NotFound);
                }

                var message = mutate(batch, DateTimeOffset.UtcNow);
                return Result(batch, commandId, message);
            },
            cancellationToken);
    }

    private async Task<DabMixPosition> SelectPositionAsync(string? requestedCode, CancellationToken cancellationToken)
    {
        var positions = await dbContext.DabMixPositions
            .Where(x => x.PositionNo >= 1 && x.PositionNo <= 8)
            .OrderBy(x => x.PositionNo)
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(requestedCode))
        {
            var code = requestedCode.Trim().ToUpperInvariant();
            var requested = positions.SingleOrDefault(x => x.Code == code);
            if (requested is null || !requested.IsEnabled || requested.Status == DabMixPositionStatus.Disabled)
            {
                throw new BusinessRuleException("dab_position_unavailable", "Requested DAB mix position is unavailable.", StatusCodes.Status409Conflict);
            }

            if (!await IsPositionFreeAsync(requested, cancellationToken))
            {
                throw new BusinessRuleException("dab_position_occupied", $"DAB mix position {code} is already occupied.", StatusCodes.Status409Conflict);
            }

            return requested;
        }

        foreach (var position in positions.Where(x => x.IsEnabled && x.Status == DabMixPositionStatus.Available && x.ActiveDabBatchId == null))
        {
            if (await IsPositionFreeAsync(position, cancellationToken))
            {
                return position;
            }
        }

        throw new BusinessRuleException("dab_positions_unavailable", "All DAB mix positions M1-M8 are unavailable or awaiting cleaning.", StatusCodes.Status409Conflict);
    }

    private async Task<bool> IsPositionFreeAsync(DabMixPosition position, CancellationToken cancellationToken)
    {
        if (position.Status != DabMixPositionStatus.Available || position.ActiveDabBatchId is not null)
        {
            return false;
        }

        return !await dbContext.DabBatches.AnyAsync(
            x => x.DabMixPositionId == position.Id && x.Status != DabBatchStatus.Cleaned,
            cancellationToken);
    }

    private async Task ReserveBottleSourceAsync(
        DabBatch batch,
        string sourceRole,
        string reagentCode,
        string preferredBottleId,
        int requiredVolumeUl,
        string commandId,
        AuthenticatedUser actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (requiredVolumeUl <= 0)
        {
            throw new BusinessRuleException("dab_volume_invalid", "DAB source reservation volume must be positive.");
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var candidates = (await dbContext.ReagentBottles
            .Where(x => x.ReagentCode == reagentCode
                && x.Status == "Available"
                && x.ExpirationDate >= today)
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.Id == preferredBottleId)
            .ThenBy(x => x.ExpirationDate)
            .ThenBy(x => x.CreatedAtUtc)
            .ToList();
        if (candidates.All(x => x.Id != preferredBottleId))
        {
            throw new BusinessRuleException("dab_source_bottle_unavailable", "Requested DAB source bottle is unavailable or expired.", StatusCodes.Status409Conflict);
        }

        var candidateIds = candidates.Select(x => x.Id).ToList();
        var activeReservations = await dbContext.ReagentReservations
            .Where(x => x.ReagentBottleId != null
                && candidateIds.Contains(x.ReagentBottleId)
                && x.Status == ReagentReservationStatus.Reserved)
            .GroupBy(x => x.ReagentBottleId!)
            .Select(x => new { ReagentBottleId = x.Key, ReservedVolumeUl = x.Sum(y => y.ReservedVolumeUl) })
            .ToDictionaryAsync(x => x.ReagentBottleId, x => x.ReservedVolumeUl, cancellationToken);

        var remainingToReserve = requiredVolumeUl;
        foreach (var bottle in candidates)
        {
            var alreadyReserved = activeReservations.GetValueOrDefault(bottle.Id);
            var availableVolumeUl = bottle.RemainingVolumeUl - alreadyReserved;
            if (availableVolumeUl <= 0)
            {
                continue;
            }

            var reservedVolumeUl = Math.Min(remainingToReserve, availableVolumeUl);
            batch.ReagentReservations.Add(new ReagentReservation
            {
                ReagentBottleId = bottle.Id,
                ReagentCode = reagentCode,
                ReservationKind = ReagentReservationKind.DabBatch,
                SourceRole = sourceRole,
                Status = ReagentReservationStatus.Reserved,
                CommandId = commandId,
                CreatedByUserId = actor.UserId,
                RequiredVolumeUl = reservedVolumeUl,
                ReservedVolumeUl = reservedVolumeUl,
                CreatedAtUtc = now
            });

            remainingToReserve -= reservedVolumeUl;
            if (remainingToReserve == 0)
            {
                return;
            }
        }

        throw new BusinessRuleException("dab_source_volume_insufficient", "DAB source bottle volume is insufficient after active reservations are considered.", StatusCodes.Status409Conflict);
    }

    private static void RequireReservedSources(DabBatch batch, DabFormulaVolumes actual)
    {
        var reservedByRole = batch.ReagentReservations
            .Where(x => x.Status == ReagentReservationStatus.Reserved)
            .GroupBy(x => x.SourceRole)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.ReservedVolumeUl), StringComparer.OrdinalIgnoreCase);

        if (reservedByRole.GetValueOrDefault("DabA") < actual.DabAVolumeUl
            || reservedByRole.GetValueOrDefault("DabB") < actual.DabBVolumeUl
            || reservedByRole.GetValueOrDefault("Water") < actual.WaterVolumeUl)
        {
            throw new BusinessRuleException("dab_source_reservation_insufficient", "DAB source reservations are insufficient for the actual prepared formula.", StatusCodes.Status409Conflict);
        }
    }

    private static void MarkReservationsForFailure(DabBatch batch, string previousStatus, DateTimeOffset now)
    {
        var status = previousStatus == DabBatchStatus.PendingPreparation
            ? ReagentReservationStatus.Released
            : ReagentReservationStatus.NeedsManualResolution;
        foreach (var reservation in batch.ReagentReservations.Where(x => x.Status == ReagentReservationStatus.Reserved))
        {
            reservation.Status = status;
            reservation.UpdatedAtUtc = now;
        }
    }

    private IQueryable<DabBatch> BatchQuery(bool asTracking)
    {
        IQueryable<DabBatch> query = dbContext.DabBatches
            .Include(x => x.DabMixPosition)
            .Include(x => x.DabAReagentBottle)
            .Include(x => x.DabBReagentBottle)
            .Include(x => x.Tasks)
            .Include(x => x.ReagentReservations)
            .ThenInclude(x => x.ReagentBottle);
        return asTracking ? query : query.AsNoTracking();
    }

    private static void RequireStatus(DabBatch batch, string expected)
    {
        if (batch.Status != expected)
        {
            throw InvalidTransition(batch.Status, expected);
        }
    }

    private static BusinessRuleException InvalidTransition(string current, string requested)
    {
        return new BusinessRuleException(
            "dab_status_transition_invalid",
            $"DAB batch cannot transition from {current} to {requested}.",
            StatusCodes.Status409Conflict);
    }

    private static void RequireCleaning(DabBatch batch, DateTimeOffset now)
    {
        batch.CleaningStatus = DabCleaningStatus.Required;
        batch.DabMixPosition!.Status = DabMixPositionStatus.AwaitingCleaning;
        batch.DabMixPosition.UpdatedAtUtc = now;
    }

    private static void RequireManualResolution(DabBatch batch, DateTimeOffset now)
    {
        batch.CleaningStatus = DabCleaningStatus.NeedsManualResolution;
        batch.DabMixPosition!.Status = DabMixPositionStatus.NeedsManualResolution;
        batch.DabMixPosition.UpdatedAtUtc = now;
    }

    private void AddAudit(
        AuthenticatedUser actor,
        string commandId,
        string action,
        DabBatch batch,
        object details,
        DateTimeOffset now)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.UserId,
            Action = action,
            EntityType = "DabBatch",
            EntityId = batch.Id,
            Message = JsonSerializer.Serialize(new { commandId, details }, JsonOptions),
            CreatedAtUtc = now
        });
    }

    private static CommandExecutionResult<DabBatchResponse> Result(DabBatch batch, string commandId, string message)
    {
        ValidateStateInvariants(batch);
        return new CommandExecutionResult<DabBatchResponse>(
            ToResponse(batch, commandId, false, message),
            "DabBatch",
            batch.Id);
    }

    private static void ValidateStateInvariants(DabBatch batch)
    {
        var position = batch.DabMixPosition
            ?? throw new BusinessRuleException("dab_position_not_loaded", "DAB mix position state must be loaded before lifecycle mutation.");
        switch (batch.Status)
        {
            case DabBatchStatus.PendingPreparation:
            case DabBatchStatus.Preparing:
            case DabBatchStatus.Available:
                if (position.Status != DabMixPositionStatus.Occupied
                    || position.ActiveDabBatchId != batch.Id
                    || batch.CleaningStatus != DabCleaningStatus.NotRequired)
                {
                    throw new BusinessRuleException("dab_state_invariant_violation", "Usable or preparing DAB batches must occupy exactly one mix position and require no cleaning.", StatusCodes.Status409Conflict);
                }
                break;
            case DabBatchStatus.Depleted:
            case DabBatchStatus.Expired:
            case DabBatchStatus.AwaitingCleaning:
                if (position.Status != DabMixPositionStatus.AwaitingCleaning
                    || position.ActiveDabBatchId != batch.Id
                    || batch.CleaningStatus != DabCleaningStatus.Required)
                {
                    throw new BusinessRuleException("dab_state_invariant_violation", "Depleted or expired DAB batches must keep their position awaiting cleaning.", StatusCodes.Status409Conflict);
                }
                break;
            case DabBatchStatus.Failed:
            case DabBatchStatus.Unknown:
                if (position.Status != DabMixPositionStatus.NeedsManualResolution
                    || position.ActiveDabBatchId != batch.Id
                    || batch.CleaningStatus != DabCleaningStatus.NeedsManualResolution)
                {
                    throw new BusinessRuleException("dab_state_invariant_violation", "Failed or unknown DAB batches must keep their position locked for manual resolution.", StatusCodes.Status409Conflict);
                }
                break;
            case DabBatchStatus.Cleaned:
                if (position.Status != DabMixPositionStatus.Available
                    || position.ActiveDabBatchId is not null
                    || batch.CleaningStatus != DabCleaningStatus.Confirmed)
                {
                    throw new BusinessRuleException("dab_state_invariant_violation", "Cleaned DAB batches must release the mix position only after cleaning confirmation.", StatusCodes.Status409Conflict);
                }
                break;
            case DabBatchStatus.LegacyUnverified:
                break;
            default:
                throw new BusinessRuleException("dab_state_unknown", "DAB batch status is not recognized.", StatusCodes.Status409Conflict);
        }
    }

    private static DabBatchResponse ToResponse(DabBatch batch, string? commandId, bool replayed, string message)
    {
        return new DabBatchResponse(
            true,
            commandId,
            replayed,
            batch.Id,
            batch.DabMixPositionId,
            batch.PositionCode,
            batch.Status,
            batch.CleaningStatus,
            batch.DabAReagentBottleId,
            batch.DabAReagentBottle?.FullBarcode,
            batch.DabBReagentBottleId,
            batch.DabBReagentBottle?.FullBarcode,
            batch.Tasks.Select(x => x.StainingTaskId).OrderBy(x => x).ToList(),
            batch.SlideCount,
            batch.VolumePerSlideUl,
            batch.LineReserveVolumeUl,
            batch.DabARatioParts,
            batch.DabBRatioParts,
            batch.WaterRatioParts,
            batch.TotalRequiredVolumeUl,
            batch.ActualPreparedVolumeUl,
            batch.DabAVolumeUl,
            batch.DabBVolumeUl,
            batch.WaterVolumeUl,
            batch.UsedVolumeUl,
            batch.RemainingVolumeUl,
            batch.PreparedAtUtc,
            batch.ExpiresAtUtc,
            batch.CleaningConfirmedAtUtc,
            batch.CreatedAtUtc,
            batch.UpdatedAtUtc,
            batch.ReagentReservations
                .Select(x => new DabReservationResponse(
                    x.Id,
                    x.ReagentCode,
                    x.SourceRole,
                    x.Status,
                    x.ReagentBottleId,
                    x.ReagentBottle?.FullBarcode,
                    x.ReservedVolumeUl))
                .OrderBy(x => x.SourceRole)
                .ThenBy(x => x.ReagentBottleId)
                .ToList(),
            message);
    }
}
