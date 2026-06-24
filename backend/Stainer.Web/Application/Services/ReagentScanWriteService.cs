using System.Text.Json;
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

    public Task<ReagentScanConfirmationResponse> ConfirmScanAsync(
        ConfirmReagentScanRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
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
}
