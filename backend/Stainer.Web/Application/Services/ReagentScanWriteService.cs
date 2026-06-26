using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class ReagentScanWriteService(
    StainerDbContext dbContext,
    IReagentBarcodeParser barcodeParser,
    CommandIdempotencyService idempotencyService,
    IRuntimeEventPublisher eventPublisher)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ReagentScanSessionMutationResponse> StartSessionAsync(
        StartReagentScanSessionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "reagent.scan_session.start",
            request,
            actor,
            async () =>
            {
                var activeSession = (await dbContext.ReagentScanSessions
                        .Include(x => x.CreatedByUser)
                        .Include(x => x.Items)
                        .ToListAsync(cancellationToken))
                    .Where(x => x.CompletedAtUtc is null && x.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.StartedAtUtc)
                    .FirstOrDefault();

                if (activeSession is not null)
                {
                    var existingSummary = await BuildSessionSummaryAsync(activeSession, null, cancellationToken);
                    return new CommandExecutionResult<ReagentScanSessionMutationResponse>(
                        new ReagentScanSessionMutationResponse(
                            true,
                            request.CommandId,
                            false,
                            existingSummary,
                            "Existing active reagent scan session returned."),
                        "ReagentScanSession",
                        activeSession.Id);
                }

                var now = DateTimeOffset.UtcNow;
                var session = new ReagentScanSession
                {
                    SessionCode = $"RSCAN-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}",
                    Status = "Active",
                    StartedAtUtc = now,
                    CreatedByUserId = actor.UserId
                };
                dbContext.ReagentScanSessions.Add(session);
                var summary = await BuildSessionSummaryAsync(session, actor.DisplayName, cancellationToken);

                dbContext.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actor.UserId,
                    Action = "reagent.scan_session.start",
                    EntityType = "ReagentScanSession",
                    EntityId = session.Id,
                    Message = JsonSerializer.Serialize(new
                    {
                        session.SessionCode,
                        session.Status,
                        summary.ScannedCount,
                        summary.ValidCount,
                        summary.InvalidCount,
                        summary.EmptyCount,
                        summary.UnscannedCount
                    }, JsonOptions),
                    CreatedAtUtc = now
                });
                PublishScanSessionChanged(session, summary, "Reagent scan session started.");

                return new CommandExecutionResult<ReagentScanSessionMutationResponse>(
                    new ReagentScanSessionMutationResponse(
                        true,
                        request.CommandId,
                        false,
                        summary,
                        "Reagent scan session started."),
                    "ReagentScanSession",
                    session.Id);
            },
            cancellationToken);
    }

    public Task<ReagentScanSessionMutationResponse> CompleteSessionAsync(
        string scanSessionId,
        CompleteReagentScanSessionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "reagent.scan_session.complete",
            new { scanSessionId, request.CommandId },
            actor,
            async () =>
            {
                var session = await dbContext.ReagentScanSessions
                    .Include(x => x.CreatedByUser)
                    .Include(x => x.Items)
                    .SingleOrDefaultAsync(x => x.Id == scanSessionId, cancellationToken);
                if (session is null)
                {
                    throw new BusinessRuleException("reagent_scan_session_not_found", "Reagent scan session was not found.", StatusCodes.Status404NotFound);
                }

                if (session.CompletedAtUtc is not null || !session.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessRuleException("reagent_scan_session_not_active", "Only Active reagent scan sessions can be completed.", StatusCodes.Status409Conflict);
                }

                var now = DateTimeOffset.UtcNow;
                session.Status = "Completed";
                session.CompletedAtUtc = now;
                var summary = await BuildSessionSummaryAsync(session, null, cancellationToken);

                dbContext.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actor.UserId,
                    Action = "reagent.scan_session.complete",
                    EntityType = "ReagentScanSession",
                    EntityId = session.Id,
                    Message = JsonSerializer.Serialize(new
                    {
                        session.SessionCode,
                        session.Status,
                        summary.ScannedCount,
                        summary.ValidCount,
                        summary.InvalidCount,
                        summary.EmptyCount,
                        summary.UnscannedCount,
                        summary.HasWarning
                    }, JsonOptions),
                    CreatedAtUtc = now
                });
                PublishScanSessionChanged(session, summary, summary.Message);

                return new CommandExecutionResult<ReagentScanSessionMutationResponse>(
                    new ReagentScanSessionMutationResponse(
                        true,
                        request.CommandId,
                        false,
                        summary,
                        summary.Message),
                    "ReagentScanSession",
                    session.Id);
            },
            cancellationToken);
    }

    public Task<ReagentScanConfirmationResponse> ConfirmScanAsync(
        ConfirmReagentScanRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.ScanSessionId))
        {
            return ConfirmActiveSessionPositionAsync(request, actor, cancellationToken);
        }

        return idempotencyService.RunAsync(
            request.CommandId,
            "reagent.scan_confirm",
            request,
            actor,
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var positions = await dbContext.ReagentRackPositions
                    .OrderBy(x => x.PositionNo)
                    .ToListAsync(cancellationToken);
                var inputByPosition = request.Items
                    .GroupBy(x => (x.Position ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

                var session = new ReagentScanSession
                {
                    SessionCode = $"SCAN-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..28],
                    Status = "Completed",
                    StartedAtUtc = now,
                    CompletedAtUtc = now,
                    CreatedByUserId = actor.UserId
                };
                dbContext.ReagentScanSessions.Add(session);

                var activePlacements = await dbContext.ReagentRackPlacements
                    .Where(x => x.RemovedAtUtc == null)
                    .ToListAsync(cancellationToken);

                var emptyCount = 0;
                var validCount = 0;
                var invalidCount = 0;

                foreach (var position in positions)
                {
                    inputByPosition.TryGetValue(position.Code, out var input);
                    var rawScanResult = (input?.ScanResult ?? ReagentScanResult.Empty).Trim().ToUpperInvariant();
                    var rawBarcode = string.IsNullOrWhiteSpace(input?.RawBarcode) ? null : input.RawBarcode.Trim();
                    var requestedEmpty = rawScanResult == ReagentScanResult.Empty || string.IsNullOrWhiteSpace(rawBarcode);
                    var parsed = requestedEmpty ? null : barcodeParser.Parse(rawBarcode);

                    var scanResult = ReagentScanResult.Empty;
                    var validationPassed = false;
                    var validationMessage = "Empty position.";
                    ReagentDefinition? definition = null;

                    if (!requestedEmpty)
                    {
                        if (rawScanResult == ReagentScanResult.Invalid || parsed is null || !parsed.IsValid)
                        {
                            scanResult = ReagentScanResult.Invalid;
                            validationMessage = parsed?.ValidationMessage ?? "Invalid scan result.";
                        }
                        else
                        {
                            definition = await dbContext.ReagentDefinitions
                                .SingleOrDefaultAsync(x => x.ReagentCode == parsed.ReagentCode, cancellationToken);
                            if (definition is null)
                            {
                                scanResult = ReagentScanResult.Invalid;
                                validationMessage = $"Unknown reagent code: {parsed.ReagentCode}.";
                            }
                            else
                            {
                                scanResult = ReagentScanResult.Valid;
                                validationPassed = true;
                                validationMessage = "OK";
                            }
                        }
                    }

                    switch (scanResult)
                    {
                        case ReagentScanResult.Valid:
                            validCount++;
                            break;
                        case ReagentScanResult.Invalid:
                            invalidCount++;
                            break;
                        default:
                            emptyCount++;
                            break;
                    }

                    var item = new ReagentScanItem
                    {
                        ReagentScanSession = session,
                        ReagentRackPositionId = position.Id,
                        ScannerChannelNo = position.ScannerChannelNo,
                        ScannerChannelCode = position.ScannerChannelCode,
                        LocatorCode = input?.LocatorCode ?? position.Code,
                        ScanResult = scanResult,
                        RawBarcode = rawBarcode,
                        ParsedReagentCode = parsed?.ReagentCode,
                        ParsedQuantityUl = parsed?.QuantityUl,
                        ParsedBatchNo = parsed?.ProductionBatchNo,
                        ParsedSerialNo = parsed?.SerialNo,
                        IsValidationPassed = validationPassed,
                        ValidationMessage = validationMessage,
                        CreatedAtUtc = now
                    };
                    session.Items.Add(item);

                    foreach (var placement in activePlacements.Where(x => x.ReagentRackPositionId == position.Id))
                    {
                        placement.RemovedAtUtc = now;
                    }

                    if (scanResult != ReagentScanResult.Valid || parsed is null || definition is null)
                    {
                        continue;
                    }

                    var bottle = await dbContext.ReagentBottles.SingleOrDefaultAsync(x => x.FullBarcode == parsed.RawText, cancellationToken);
                    if (bottle is null)
                    {
                        bottle = new ReagentBottle
                        {
                            ReagentDefinitionId = definition.Id,
                            FullBarcode = parsed.RawText,
                            ReagentCode = parsed.ReagentCode!,
                            ProductionBatchNo = parsed.ProductionBatchNo!,
                            SerialNo = parsed.SerialNo!,
                            InitialVolumeUl = parsed.QuantityUl!.Value,
                            RemainingVolumeUl = parsed.QuantityUl.Value,
                            ExpirationDate = input?.ExpirationDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                            Status = "Available",
                            FirstScannedAtUtc = now,
                            LastScannedAtUtc = now,
                            CreatedAtUtc = now
                        };
                        dbContext.ReagentBottles.Add(bottle);
                    }
                    else
                    {
                        bottle.LastScannedAtUtc = now;
                        bottle.Status = "Available";
                        bottle.UpdatedAtUtc = now;
                    }

                    foreach (var placement in activePlacements.Where(x => x.ReagentBottleId == bottle.Id))
                    {
                        placement.RemovedAtUtc = now;
                    }

                    dbContext.ReagentRackPlacements.Add(new ReagentRackPlacement
                    {
                        ReagentBottle = bottle,
                        ReagentRackPositionId = position.Id,
                        ReagentScanSession = session,
                        PlacedAtUtc = now,
                        CreatedAtUtc = now
                    });
                }

                dbContext.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actor.UserId,
                    Action = "reagent.scan_confirm",
                    EntityType = "ReagentScanSession",
                    EntityId = session.Id,
                    Message = JsonSerializer.Serialize(new { emptyCount, validCount, invalidCount }, JsonOptions),
                    CreatedAtUtc = now
                });
                eventPublisher.Publish(MachineEventMessage.Create(
                    MachineEventTypes.QrScanCompleted,
                    null,
                    "ReagentScanSession",
                    session.Id,
                    null,
                    new Dictionary<string, object?>
                    {
                        ["scanSessionId"] = session.Id,
                        ["emptyCount"] = emptyCount,
                        ["validCount"] = validCount,
                        ["invalidCount"] = invalidCount,
                        ["message"] = "Reagent scan confirmed."
                    }));

                return new CommandExecutionResult<ReagentScanConfirmationResponse>(
                    new ReagentScanConfirmationResponse(
                        true,
                        request.CommandId,
                        false,
                        session.Id,
                        emptyCount,
                        validCount,
                        invalidCount,
                        "Reagent scan confirmed."),
                    "ReagentScanSession",
                    session.Id);
            },
            cancellationToken);
    }

    private Task<ReagentScanConfirmationResponse> ConfirmActiveSessionPositionAsync(
        ConfirmReagentScanRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "reagent.scan_confirm",
            request,
            actor,
            async () =>
            {
                if (request.Items.Count != 1)
                {
                    throw new BusinessRuleException("single_reagent_scan_item_required", "Active scan session confirmation requires exactly one reagent rack position.", StatusCodes.Status400BadRequest);
                }

                var input = request.Items.Single();
                var positionCode = (input.Position ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(positionCode))
                {
                    throw new BusinessRuleException("reagent_position_required", "Reagent rack position is required.", StatusCodes.Status400BadRequest);
                }

                var session = await dbContext.ReagentScanSessions
                    .Include(x => x.CreatedByUser)
                    .Include(x => x.Items)
                    .SingleOrDefaultAsync(x => x.Id == request.ScanSessionId, cancellationToken);
                if (session is null)
                {
                    throw new BusinessRuleException("reagent_scan_session_not_found", "Reagent scan session was not found.", StatusCodes.Status404NotFound);
                }

                if (session.CompletedAtUtc is not null || !session.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessRuleException("reagent_scan_session_not_active", "Only Active reagent scan sessions can accept scan confirmation.", StatusCodes.Status409Conflict);
                }

                var position = await dbContext.ReagentRackPositions
                    .SingleOrDefaultAsync(x => x.Code == positionCode, cancellationToken);
                if (position is null)
                {
                    throw new BusinessRuleException("reagent_position_not_found", "Reagent rack position was not found.", StatusCodes.Status404NotFound);
                }

                var now = DateTimeOffset.UtcNow;
                var scan = await BuildScanItemAsync(session, position, input, now, cancellationToken);
                var existingItem = session.Items.SingleOrDefault(x => x.ReagentRackPositionId == position.Id);
                var isRescan = existingItem is not null;
                if (existingItem is null)
                {
                    session.Items.Add(scan.Item);
                    dbContext.ReagentScanItems.Add(scan.Item);
                }
                else
                {
                    existingItem.ScannerChannelNo = scan.Item.ScannerChannelNo;
                    existingItem.ScannerChannelCode = scan.Item.ScannerChannelCode;
                    existingItem.LocatorCode = scan.Item.LocatorCode;
                    existingItem.ScanResult = scan.Item.ScanResult;
                    existingItem.RawBarcode = scan.Item.RawBarcode;
                    existingItem.ParsedReagentCode = scan.Item.ParsedReagentCode;
                    existingItem.ParsedQuantityUl = scan.Item.ParsedQuantityUl;
                    existingItem.ParsedBatchNo = scan.Item.ParsedBatchNo;
                    existingItem.ParsedSerialNo = scan.Item.ParsedSerialNo;
                    existingItem.IsValidationPassed = scan.Item.IsValidationPassed;
                    existingItem.ValidationMessage = scan.Item.ValidationMessage;
                    existingItem.CreatedAtUtc = now;
                }

                var activePlacements = await dbContext.ReagentRackPlacements
                    .Where(x => x.RemovedAtUtc == null
                        && (x.ReagentRackPositionId == position.Id
                            || (scan.Bottle != null && x.ReagentBottleId == scan.Bottle.Id)))
                    .ToListAsync(cancellationToken);
                foreach (var placement in activePlacements)
                {
                    placement.RemovedAtUtc = now;
                }

                if (scan.Item.ScanResult == ReagentScanResult.Valid && scan.Bottle is not null)
                {
                    dbContext.ReagentRackPlacements.Add(new ReagentRackPlacement
                    {
                        ReagentBottle = scan.Bottle,
                        ReagentRackPositionId = position.Id,
                        ReagentScanSession = session,
                        PlacedAtUtc = now,
                        CreatedAtUtc = now
                    });
                }

                var summary = await BuildSessionSummaryAsync(session, null, cancellationToken);
                dbContext.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actor.UserId,
                    Action = isRescan ? "reagent.scan_rescan" : "reagent.scan_confirm",
                    EntityType = "ReagentScanSession",
                    EntityId = session.Id,
                    Message = JsonSerializer.Serialize(new
                    {
                        position = position.Code,
                        positionId = position.Id,
                        scan.Item.ScanResult,
                        scan.Item.ValidationMessage,
                        scan.Item.ParsedReagentCode,
                        isRescan
                    }, JsonOptions),
                    CreatedAtUtc = now
                });
                PublishScanSessionChanged(session, summary, isRescan ? "Reagent rack position rescanned." : "Reagent rack position scan confirmed.");
                PublishReagentChanged(session, position, scan.Item, scan.Bottle, isRescan);

                return new CommandExecutionResult<ReagentScanConfirmationResponse>(
                    new ReagentScanConfirmationResponse(
                        true,
                        request.CommandId,
                        false,
                        session.Id,
                        summary.EmptyCount,
                        summary.ValidCount,
                        summary.InvalidCount,
                        isRescan ? "Reagent rack position rescanned." : "Reagent rack position scan confirmed.",
                        position.Code,
                        scan.Item.ScanResult,
                        scan.Item.ValidationMessage),
                    "ReagentScanSession",
                    session.Id);
            },
            cancellationToken);
    }

    private async Task<PositionScanBuildResult> BuildScanItemAsync(
        ReagentScanSession session,
        ReagentRackPosition position,
        ReagentScanInputItem input,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var requestedResult = (input.ScanResult ?? ReagentScanResult.Valid).Trim().ToUpperInvariant();
        var rawBarcode = string.IsNullOrWhiteSpace(input.RawBarcode) ? null : input.RawBarcode.Trim();
        var requestedEmpty = requestedResult == ReagentScanResult.Empty;
        var parsed = requestedEmpty ? null : barcodeParser.Parse(rawBarcode);
        var scanResult = ReagentScanResult.Empty;
        var validationPassed = false;
        var validationMessage = "Empty position.";
        ReagentDefinition? definition = null;
        ReagentBottle? bottle = null;

        if (!requestedEmpty)
        {
            if (requestedResult == ReagentScanResult.Invalid || parsed is null || !parsed.IsValid)
            {
                scanResult = ReagentScanResult.Invalid;
                validationMessage = parsed?.ValidationMessage ?? "Invalid scan result.";
            }
            else
            {
                definition = await dbContext.ReagentDefinitions
                    .SingleOrDefaultAsync(x => x.ReagentCode == parsed.ReagentCode, cancellationToken);
                if (definition is null)
                {
                    scanResult = ReagentScanResult.Invalid;
                    validationMessage = $"Unknown reagent code: {parsed.ReagentCode}.";
                }
                else
                {
                    var expirationDate = input.ExpirationDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));
                    if (expirationDate < DateOnly.FromDateTime(DateTime.UtcNow))
                    {
                        scanResult = ReagentScanResult.Invalid;
                        validationMessage = $"Reagent is expired: {expirationDate:yyyy-MM-dd}.";
                    }
                    else
                    {
                        scanResult = ReagentScanResult.Valid;
                        validationPassed = true;
                        validationMessage = "OK";
                        bottle = await dbContext.ReagentBottles.SingleOrDefaultAsync(x => x.FullBarcode == parsed.RawText, cancellationToken);
                        if (bottle is null)
                        {
                            bottle = new ReagentBottle
                            {
                                ReagentDefinitionId = definition.Id,
                                FullBarcode = parsed.RawText,
                                ReagentCode = parsed.ReagentCode!,
                                ProductionBatchNo = parsed.ProductionBatchNo!,
                                SerialNo = parsed.SerialNo!,
                                InitialVolumeUl = parsed.QuantityUl!.Value,
                                RemainingVolumeUl = parsed.QuantityUl.Value,
                                ExpirationDate = expirationDate,
                                Status = "Available",
                                FirstScannedAtUtc = now,
                                LastScannedAtUtc = now,
                                CreatedAtUtc = now
                            };
                            dbContext.ReagentBottles.Add(bottle);
                        }
                        else
                        {
                            bottle.LastScannedAtUtc = now;
                            bottle.Status = "Available";
                            bottle.UpdatedAtUtc = now;
                        }
                    }
                }
            }
        }

        return new PositionScanBuildResult(
            new ReagentScanItem
            {
                ReagentScanSession = session,
                ReagentRackPositionId = position.Id,
                ScannerChannelNo = position.ScannerChannelNo,
                ScannerChannelCode = position.ScannerChannelCode,
                LocatorCode = input.LocatorCode ?? position.Code,
                ScanResult = scanResult,
                RawBarcode = rawBarcode,
                ParsedReagentCode = parsed?.ReagentCode,
                ParsedQuantityUl = parsed?.QuantityUl,
                ParsedBatchNo = parsed?.ProductionBatchNo,
                ParsedSerialNo = parsed?.SerialNo,
                IsValidationPassed = validationPassed,
                ValidationMessage = validationMessage,
                CreatedAtUtc = now
            },
            bottle);
    }

    private async Task<ReagentScanSessionSummaryResponse> BuildSessionSummaryAsync(
        ReagentScanSession session,
        string? createdByDisplayName,
        CancellationToken cancellationToken)
    {
        var totalPositionCount = await dbContext.ReagentRackPositions.CountAsync(cancellationToken);
        var items = session.Items.ToList();
        var validCount = items.Count(x => x.ScanResult.Equals(ReagentScanResult.Valid, StringComparison.OrdinalIgnoreCase));
        var invalidCount = items.Count(x => x.ScanResult.Equals(ReagentScanResult.Invalid, StringComparison.OrdinalIgnoreCase));
        var emptyCount = items.Count(x => x.ScanResult.Equals(ReagentScanResult.Empty, StringComparison.OrdinalIgnoreCase));
        var scannedPositionCount = items
            .Select(x => x.ReagentRackPositionId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var unscannedCount = Math.Max(0, totalPositionCount - scannedPositionCount);
        var completed = session.CompletedAtUtc is not null || session.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        var hasWarning = completed && unscannedCount > 0;
        var message = hasWarning
            ? $"Reagent scan session completed with {unscannedCount} unscanned position(s)."
            : completed
                ? "Reagent scan session completed."
                : "Reagent scan session is active.";

        return new ReagentScanSessionSummaryResponse(
            session.Id,
            session.SessionCode,
            session.Status,
            session.StartedAtUtc,
            session.CompletedAtUtc,
            session.CreatedByUserId,
            session.CreatedByUser?.DisplayName ?? createdByDisplayName,
            items.Count,
            validCount,
            invalidCount,
            emptyCount,
            unscannedCount,
            totalPositionCount,
            hasWarning,
            message);
    }

    private void PublishScanSessionChanged(ReagentScanSession session, ReagentScanSessionSummaryResponse summary, string message)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.ScanSessionChanged,
            null,
            "ReagentScanSession",
            session.Id,
            null,
            new Dictionary<string, object?>
            {
                ["scanSessionId"] = session.Id,
                ["sessionCode"] = session.SessionCode,
                ["status"] = session.Status,
                ["startedAtUtc"] = session.StartedAtUtc,
                ["completedAtUtc"] = session.CompletedAtUtc,
                ["scannedCount"] = summary.ScannedCount,
                ["validCount"] = summary.ValidCount,
                ["invalidCount"] = summary.InvalidCount,
                ["emptyCount"] = summary.EmptyCount,
                ["unscannedCount"] = summary.UnscannedCount,
                ["hasWarning"] = summary.HasWarning,
                ["message"] = message
            }));
    }

    private void PublishReagentChanged(
        ReagentScanSession session,
        ReagentRackPosition position,
        ReagentScanItem item,
        ReagentBottle? bottle,
        bool isRescan)
    {
        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.ReagentChanged,
            null,
            "ReagentRackPosition",
            position.Id,
            null,
            new Dictionary<string, object?>
            {
                ["scanSessionId"] = session.Id,
                ["position"] = position.Code,
                ["scanResult"] = item.ScanResult,
                ["validationMessage"] = item.ValidationMessage,
                ["isRescan"] = isRescan,
                ["message"] = isRescan ? "Reagent rack position rescanned." : "Reagent rack position scan confirmed."
            }));

        if (bottle is null)
        {
            return;
        }

        eventPublisher.Publish(MachineEventMessage.Create(
            MachineEventTypes.ReagentBottleChanged,
            null,
            "ReagentBottle",
            bottle.Id,
            null,
            new Dictionary<string, object?>
            {
                ["scanSessionId"] = session.Id,
                ["position"] = position.Code,
                ["reagentCode"] = bottle.ReagentCode,
                ["remainingVolumeUl"] = bottle.RemainingVolumeUl,
                ["message"] = "Reagent bottle placement changed."
            }));
    }

    private sealed record PositionScanBuildResult(ReagentScanItem Item, ReagentBottle? Bottle);
}
