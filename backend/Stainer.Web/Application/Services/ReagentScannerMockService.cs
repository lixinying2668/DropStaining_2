using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class ReagentScannerMockService(
    StainerDbContext dbContext,
    IDeviceAdapter deviceAdapter,
    DeviceCommunicationPersistenceService communicationPersistence,
    ReagentScanWriteService scanWriteService)
{
    public async Task<MockReagentScanResponse> ScanAsync(
        MockReagentScanRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("mock_device_mode_required", "Reagent scanner Mock requires DeviceMode=Mock.", StatusCodes.Status409Conflict);
        }

        var positions = await ResolvePositionsAsync(request, cancellationToken);
        var sessionId = request.ScanSessionId;
        ReagentScanSessionMutationResponse? sessionMutation = null;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionMutation = await scanWriteService.StartSessionAsync(
                new StartReagentScanSessionRequest($"{request.CommandId}-start"),
                actor,
                cancellationToken);
            sessionId = sessionMutation.Session.ScanSessionId;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new BusinessRuleException("reagent_scan_session_not_found", "Reagent scan session was not created.");
        }

        var results = new List<ReagentScanConfirmationResponse>();
        foreach (var position in positions)
        {
            var scenario = NormalizeScenario(request.Scenario, position.PositionNo);
            string? requestedBarcode = null;
            request.BarcodesByPosition?.TryGetValue(position.Code, out requestedBarcode);
            var rawBarcode = requestedBarcode ?? PlannedBarcode(position, scenario);
            if (scenario is "Timeout" or "Disconnect")
            {
                await deviceAdapter.ConfigureFaultAsync(
                    new DeviceFaultCommand(
                        DeviceModules.ReagentScanner,
                        scenario == "Timeout" ? DeviceFaultTypes.TimeoutNextCommand : DeviceFaultTypes.Disconnect,
                        scenario == "Timeout" ? "reagent_scanner_timeout" : "device_disconnected",
                        scenario == "Timeout" ? "Reagent scanner timed out." : "Reagent scanner disconnected.",
                        $"Reagent scanner Mock scenario: {scenario}.",
                        $"{request.CommandId}-fault-{position.Code}",
                        actor.UserId,
                        actor.Username),
                    cancellationToken);
            }

            var operationRequest = new DeviceOperationRequest(
                new DeviceCommandContext(request.CommandId, sessionId, actor.Username, nameof(ReagentScannerMockService)),
                DeviceModules.ReagentScanner,
                "scan",
                new Dictionary<string, object?>
                {
                    ["position"] = position.Code,
                    ["scenario"] = scenario,
                    ["rawBarcode"] = rawBarcode
                });
            var communicationRecord = communicationPersistence.Begin(operationRequest);
            await dbContext.SaveChangesAsync(cancellationToken);
            var deviceResult = await deviceAdapter.ScanReagentAsync(operationRequest, cancellationToken);
            await communicationPersistence.TryPersistCompletionAsync(communicationRecord, deviceResult, cancellationToken);

            var (scanResult, errorReason) = ToScanResult(scenario, deviceResult);
            var response = await scanWriteService.ConfirmScanAsync(
                new ConfirmReagentScanRequest(
                    $"{request.CommandId}-{position.Code}",
                    [
                        new ReagentScanInputItem(
                            position.Code,
                            scanResult,
                            scanResult == ReagentScanResult.Empty ? null : rawBarcode,
                            position.Code,
                            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                            errorReason)
                    ],
                    sessionId),
                actor,
                cancellationToken);
            results.Add(response);
        }

        if (ShouldComplete(request))
        {
            sessionMutation = await scanWriteService.CompleteSessionAsync(
                sessionId,
                new CompleteReagentScanSessionRequest($"{request.CommandId}-complete"),
                actor,
                cancellationToken);
        }
        else
        {
            sessionMutation = await scanWriteService.StartSessionAsync(
                new StartReagentScanSessionRequest($"{request.CommandId}-summary"),
                actor,
                cancellationToken);
        }

        return new MockReagentScanResponse(
            true,
            request.CommandId,
            sessionId,
            positions.Count,
            results,
            sessionMutation.Session,
            "Reagent scanner Mock result recorded through formal scan session.");
    }

    private async Task<IReadOnlyList<ReagentRackPosition>> ResolvePositionsAsync(
        MockReagentScanRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ReagentRackPositions.AsQueryable();
        var scope = (request.Scope ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(request.Position)
            || scope.Equals("position", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("single", StringComparison.OrdinalIgnoreCase))
        {
            var positionCode = (request.Position ?? "R1").Trim().ToUpperInvariant();
            var position = await query.SingleOrDefaultAsync(x => x.Code == positionCode, cancellationToken);
            if (position is null)
            {
                throw new BusinessRuleException("reagent_position_not_found", "Reagent rack position was not found.");
            }

            return [position];
        }

        if (request.ColumnNo is not null
            || scope.StartsWith("ch", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("column", StringComparison.OrdinalIgnoreCase))
        {
            var columnNo = request.ColumnNo
                ?? (int.TryParse(scope.Replace("ch", string.Empty, StringComparison.OrdinalIgnoreCase), out var parsed) ? parsed : 1);
            if (columnNo is < 1 or > 5)
            {
                throw new BusinessRuleException("reagent_scan_column_invalid", "Reagent scanner column must be between ch1 and ch5.");
            }

            return await query
                .Where(x => x.ScannerChannelNo == columnNo)
                .OrderBy(x => x.PositionNo)
                .ToListAsync(cancellationToken);
        }

        return await query.OrderBy(x => x.PositionNo).ToListAsync(cancellationToken);
    }

    private static bool ShouldComplete(MockReagentScanRequest request)
    {
        return string.IsNullOrWhiteSpace(request.ScanSessionId)
            && ((request.Scope ?? string.Empty).Length == 0
                || string.Equals(request.Scope, "all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.Scope, "rack", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeScenario(string? scenario, int positionNo)
    {
        var value = (scenario ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Mixed", StringComparison.OrdinalIgnoreCase))
        {
            return (positionNo % 5) switch
            {
                0 => "Empty",
                1 => "Valid",
                2 => "Invalid",
                3 => "Illegal17",
                _ => "Valid"
            };
        }

        return value.ToLowerInvariant() switch
        {
            "empty" => "Empty",
            "valid" => "Valid",
            "invalid" or "bad" or "malformed" => "Invalid",
            "illegal17" or "unknown17" or "unknown" => "Illegal17",
            "timeout" => "Timeout",
            "disconnect" or "devicedisconnected" => "Disconnect",
            _ => "Valid"
        };
    }

    private static string? PlannedBarcode(ReagentRackPosition position, string scenario)
    {
        return scenario switch
        {
            "Empty" => null,
            "Invalid" => "BAD",
            "Illegal17" => $"ZZZ050{DateTime.UtcNow:yyyyMMdd}{position.PositionNo % 1000:000}",
            "Timeout" => $"HEM050{DateTime.UtcNow:yyyyMMdd}{position.PositionNo % 1000:000}",
            "Disconnect" => $"HEM050{DateTime.UtcNow:yyyyMMdd}{position.PositionNo % 1000:000}",
            _ => $"HEM050{DateTime.UtcNow:yyyyMMdd}{position.PositionNo % 1000:000}"
        };
    }

    private static (string ScanResult, string? ErrorReason) ToScanResult(string scenario, DeviceCommandResult deviceResult)
    {
        if (!deviceResult.Ok)
        {
            return (ReagentScanResult.Invalid, deviceResult.Message);
        }

        return scenario switch
        {
            "Empty" => (ReagentScanResult.Empty, null),
            "Invalid" => (ReagentScanResult.Invalid, "Barcode is damaged or unreadable."),
            "Timeout" => (ReagentScanResult.Invalid, "Reagent scanner timed out."),
            "Disconnect" => (ReagentScanResult.Invalid, "Reagent scanner disconnected."),
            _ => (ReagentScanResult.Valid, null)
        };
    }

}
