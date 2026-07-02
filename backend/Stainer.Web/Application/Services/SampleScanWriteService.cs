using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class SampleScanWriteService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    IDeviceAdapter deviceAdapter,
    DeviceCommunicationPersistenceService communicationPersistence,
    HospitalBarcodeNormalizer hospitalBarcodeNormalizer)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<SampleScanSessionResponse> ScanAsync(
        MockSampleScanRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "sample.mock_scan",
            request,
            actor,
            async () =>
            {
                if (!string.Equals(deviceAdapter.Mode, DeviceModes.Mock, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessRuleException("mock_device_mode_required", "Sample scanner Mock requires DeviceMode=Mock.", StatusCodes.Status409Conflict);
                }

                var now = DateTimeOffset.UtcNow;
                var count = Math.Clamp(request.Count <= 0 ? 1 : request.Count, 1, 16);
                var session = new SampleScanSession
                {
                    SessionCode = $"SSCAN-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32],
                    Status = "Completed",
                    StartedAtUtc = now,
                    CompletedAtUtc = now,
                    CreatedByUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId
                };
                dbContext.SampleScanSessions.Add(session);

                for (var index = 0; index < count; index++)
                {
                    var scenario = NormalizeScenario(request.Scenario, index);
                    var plannedRaw = PlannedRawCode(scenario, request.RawCode, index);
                    var slotCode = request.SlotCode ?? $"S{index + 1:00}";
                    if (scenario is "Timeout" or "Disconnect")
                    {
                        await deviceAdapter.ConfigureFaultAsync(
                            new DeviceFaultCommand(
                                DeviceModules.SampleScanner,
                                scenario == "Timeout" ? DeviceFaultTypes.TimeoutNextCommand : DeviceFaultTypes.Disconnect,
                                scenario == "Timeout" ? "sample_scanner_timeout" : "device_disconnected",
                                scenario == "Timeout" ? "Sample scanner timed out." : "Sample scanner disconnected.",
                                $"Sample scanner Mock scenario: {scenario}.",
                                $"{request.CommandId}-fault-{index}",
                                actor.UserId,
                                actor.Username),
                            cancellationToken);
                    }

                    var operationRequest = new DeviceOperationRequest(
                        new DeviceCommandContext(request.CommandId, session.Id, actor.Username, nameof(SampleScanWriteService)),
                        DeviceModules.SampleScanner,
                        "scan",
                        new Dictionary<string, object?>
                        {
                            ["scenario"] = scenario,
                            ["rawCode"] = plannedRaw,
                            ["slotCode"] = slotCode
                        });
                    var communicationRecord = communicationPersistence.Begin(operationRequest);
                    var deviceResult = await deviceAdapter.ScanSampleAsync(operationRequest, cancellationToken);
                    communicationPersistence.Complete(communicationRecord, deviceResult);

                    var item = BuildItem(session, slotCode, scenario, plannedRaw, deviceResult);
                    session.Items.Add(item);
                    dbContext.SampleScanItems.Add(item);
                }

                dbContext.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
                    Action = "sample.mock_scan",
                    EntityType = "SampleScanSession",
                    EntityId = session.Id,
                    Message = JsonSerializer.Serialize(new
                    {
                        request.CommandId,
                        session.SessionCode,
                        count,
                        items = session.Items.Select(x => new
                        {
                            x.SlotCode,
                            x.ScanKind,
                            x.ScanStatus,
                            x.RawCode,
                            x.NormalizedCode,
                            x.PrimaryAntibodyCode,
                            x.ErrorReason,
                            x.DeviceStatus,
                            x.ScannedAtUtc
                        })
                    }, JsonOptions),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });

                return new CommandExecutionResult<SampleScanSessionResponse>(
                    ToResponse(request.CommandId, false, session, "Sample scanner Mock result recorded."),
                    "SampleScanSession",
                    session.Id);
            },
            cancellationToken);
    }

    private SampleScanItem BuildItem(
        SampleScanSession session,
        string slotCode,
        string scenario,
        string? rawCode,
        DeviceCommandResult deviceResult)
    {
        var now = DateTimeOffset.UtcNow;
        if (!deviceResult.Ok)
        {
            var disconnected = deviceResult.ErrorCode?.Contains("disconnect", StringComparison.OrdinalIgnoreCase) == true
                || deviceResult.Data.TryGetValue("faultType", out var faultType)
                    && string.Equals(faultType?.ToString(), DeviceFaultTypes.Disconnect, StringComparison.OrdinalIgnoreCase);
            return new SampleScanItem
            {
                SampleScanSession = session,
                SlotCode = slotCode,
                ScanKind = scenario == "HospitalQr" ? SampleScanKind.HospitalQr : SampleScanKind.TonglingPrimaryAntibody,
                ScanStatus = disconnected
                    ? SampleScanStatus.DeviceDisconnected
                    : deviceResult.Status == DeviceCommandStatuses.TimedOut ? SampleScanStatus.TimedOut : SampleScanStatus.Failed,
                RawCode = rawCode,
                ErrorReason = deviceResult.Message,
                DeviceStatus = deviceResult.Status,
                ScannedAtUtc = now,
                CreatedAtUtc = now
            };
        }

        return scenario switch
        {
            "Empty" => ValidatedItem(session, slotCode, SampleScanKind.Empty, SampleScanStatus.Empty, null, null, null, "Empty sample code.", deviceResult.Status, now),
            "Damaged" => ValidatedItem(session, slotCode, SampleScanKind.Damaged, SampleScanStatus.Invalid, rawCode, null, null, "Sample barcode is damaged or unreadable.", deviceResult.Status, now),
            "HospitalQr" => BuildHospitalItem(session, slotCode, rawCode, deviceResult.Status, now),
            _ => BuildTonglingItem(session, slotCode, rawCode, deviceResult.Status, now)
        };
    }

    private SampleScanItem BuildTonglingItem(
        SampleScanSession session,
        string slotCode,
        string? rawCode,
        string deviceStatus,
        DateTimeOffset now)
    {
        var normalized = (rawCode ?? string.Empty).Trim();
        if (normalized.Length == 3 && normalized.All(char.IsDigit))
        {
            return ValidatedItem(session, slotCode, SampleScanKind.TonglingPrimaryAntibody, SampleScanStatus.Valid, rawCode, normalized, normalized, null, deviceStatus, now);
        }

        return ValidatedItem(session, slotCode, SampleScanKind.TonglingPrimaryAntibody, SampleScanStatus.Invalid, rawCode, normalized, null, "Tongling primary antibody code must be exactly 3 digits.", deviceStatus, now);
    }

    private SampleScanItem BuildHospitalItem(
        SampleScanSession session,
        string slotCode,
        string? rawCode,
        string deviceStatus,
        DateTimeOffset now)
    {
        try
        {
            var normalized = hospitalBarcodeNormalizer.Normalize(rawCode);
            return ValidatedItem(session, slotCode, SampleScanKind.HospitalQr, SampleScanStatus.Valid, rawCode, normalized, null, null, deviceStatus, now);
        }
        catch (BusinessRuleException ex)
        {
            return ValidatedItem(session, slotCode, SampleScanKind.HospitalQr, SampleScanStatus.Invalid, rawCode, null, null, ex.Message, deviceStatus, now);
        }
    }

    private static SampleScanItem ValidatedItem(
        SampleScanSession session,
        string slotCode,
        string scanKind,
        string scanStatus,
        string? rawCode,
        string? normalizedCode,
        string? primaryAntibodyCode,
        string? errorReason,
        string deviceStatus,
        DateTimeOffset now)
    {
        return new SampleScanItem
        {
            SampleScanSession = session,
            SlotCode = slotCode,
            ScanKind = scanKind,
            ScanStatus = scanStatus,
            RawCode = rawCode,
            NormalizedCode = normalizedCode,
            PrimaryAntibodyCode = primaryAntibodyCode,
            ErrorReason = errorReason,
            DeviceStatus = deviceStatus,
            ScannedAtUtc = now,
            CreatedAtUtc = now
        };
    }

    private static string NormalizeScenario(string? scenario, int index)
    {
        var value = (scenario ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Mixed", StringComparison.OrdinalIgnoreCase))
        {
            return (index % 4) switch
            {
                1 => "HospitalQr",
                2 => "Empty",
                3 => "Damaged",
                _ => "Tongling"
            };
        }

        return value.ToLowerInvariant() switch
        {
            "tongling" or "tonglingprimary" or "tonglingprimaryantibody" => "Tongling",
            "hospital" or "hospitalqr" or "hospitalbarcode" => "HospitalQr",
            "empty" => "Empty",
            "damaged" or "invalid" => "Damaged",
            "timeout" => "Timeout",
            "disconnect" or "devicedisconnected" => "Disconnect",
            _ => "Tongling"
        };
    }

    private static string? PlannedRawCode(string scenario, string? rawCode, int index)
    {
        if (!string.IsNullOrWhiteSpace(rawCode))
        {
            return rawCode;
        }

        return scenario switch
        {
            "Tongling" => (index + 1).ToString("000"),
            "HospitalQr" => $"HOSP-MOCK-{index + 1:000}",
            "Damaged" => "###BROKEN###",
            "Timeout" => $"TIMEOUT-{index + 1:000}",
            "Disconnect" => $"DISCONNECT-{index + 1:000}",
            _ => null
        };
    }

    private static SampleScanSessionResponse ToResponse(string commandId, bool replayed, SampleScanSession session, string message)
    {
        var items = session.Items.OrderBy(x => x.CreatedAtUtc).ToList();
        return new SampleScanSessionResponse(
            true,
            commandId,
            replayed,
            session.Id,
            session.SessionCode,
            session.Status,
            items.Count(x => x.ScanStatus == SampleScanStatus.Valid),
            items.Count(x => x.ScanStatus == SampleScanStatus.Empty),
            items.Count(x => x.ScanStatus == SampleScanStatus.Invalid),
            items.Count(x => x.ScanStatus == SampleScanStatus.TimedOut),
            items.Count(x => x.ScanStatus == SampleScanStatus.DeviceDisconnected),
            items.Select(x => new SampleScanItemResponse(
                x.Id,
                x.SlotCode,
                x.ScanKind,
                x.ScanStatus,
                x.RawCode,
                x.NormalizedCode,
                x.PrimaryAntibodyCode,
                x.ErrorReason,
                x.DeviceStatus,
                x.ScannedAtUtc)).ToList(),
            message);
    }
}
