using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class ScannerControlService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    IEnumerable<IDeviceByteTransport> transports)
{
    private const string Cr = "\r";
    private const int DefaultTimeoutMilliseconds = 5000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Encoding Ascii = Encoding.ASCII;
    private readonly IDeviceByteTransport? transport = transports.FirstOrDefault();

    public Task<ScannerControlResponse> RestartScannerAsync(
        string scannerProfileId,
        ScannerRestartRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            scannerProfileId,
            request.CommandId,
            request.Reason,
            "scanner_control.restart",
            "RestartScanner",
            [new ScannerControlCommand("restart", "RDCMXRB1")],
            actor,
            cancellationToken);
    }

    public Task<ScannerControlResponse> EnableCalibrationLightAsync(
        string scannerProfileId,
        ScannerCalibrationLightRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            scannerProfileId,
            request.CommandId,
            request.Reason,
            "scanner_control.calibration_light.enable",
            "EnableCalibrationLight",
            [new ScannerControlCommand("calibration-light.enable", "RDCMXEV2,P11")],
            actor,
            cancellationToken);
    }

    public Task<ScannerControlResponse> DisableCalibrationLightAsync(
        string scannerProfileId,
        ScannerCalibrationLightRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            scannerProfileId,
            request.CommandId,
            request.Reason,
            "scanner_control.calibration_light.disable",
            "DisableCalibrationLight",
            [new ScannerControlCommand("calibration-light.disable", "RDCMXEV2,P10")],
            actor,
            cancellationToken);
    }

    public Task<ScannerControlResponse> ApplyRoiAsync(
        string scannerProfileId,
        ScannerRoiRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        ValidateRoi(request);
        return ExecuteAsync(
            scannerProfileId,
            request.CommandId,
            request.Reason,
            "scanner_control.roi.apply",
            "ApplyROI",
            [
                new ScannerControlCommand("roi.width", $"CDOPSRW{request.Width}"),
                new ScannerControlCommand("roi.height", $"CDOPSRH{request.Height}"),
                new ScannerControlCommand("roi.left", $"CDOPSRL{request.Left}"),
                new ScannerControlCommand("roi.top", $"CDOPSRT{request.Top}")
            ],
            actor,
            cancellationToken);
    }

    public Task<ScannerControlResponse> TriggerScannerAsync(
        string scannerProfileId,
        ScannerTriggerRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var mode = (request.Mode ?? string.Empty).Trim().ToLowerInvariant();
        var commandText = mode switch
        {
            "single" => "RDCMXEV1,P11,P20",      // 单次扫描
            "continuous" => "RDCMXEV1,P11,P21",   // 连续扫描
            "stop" => "RDCMXEV1,P10",             // 停止扫描
            _ => throw new BusinessRuleException("scanner_trigger_mode_invalid", "Mode must be one of: single, continuous, stop.", StatusCodes.Status400BadRequest)
        };
        return ExecuteAsync(
            scannerProfileId,
            request.CommandId,
            request.Reason,
            "scanner_control.trigger",
            "TriggerScanner",
            [new ScannerControlCommand($"trigger.{mode}", commandText)],
            actor,
            cancellationToken);
    }

    // 读取条码：触发一次单次扫描(RDCMXEV1,P11,P20)，DCR55 回传的条码文本在返回值的 Steps[0].ResponseText。
    public Task<ScannerControlResponse> ReadBarcodeAsync(
        string scannerProfileId,
        ScannerBarcodeRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            scannerProfileId,
            request.CommandId,
            request.Reason,
            "scanner_control.barcode.read",
            "ReadBarcode",
            [new ScannerControlCommand("barcode.read", "RDCMXEV1,P11,P20")],
            actor,
            cancellationToken);
    }

    private Task<ScannerControlResponse> ExecuteAsync(
        string scannerProfileId,
        string commandId,
        string reason,
        string idempotencyOperation,
        string operation,
        IReadOnlyList<ScannerControlCommand> commands,
        AuthenticatedUser actor,
        CancellationToken cancellationToken)
    {
        var normalizedProfileId = RequireValue(scannerProfileId, "scannerProfileId", 36);
        return idempotencyService.RunAsync(
            RequireValue(commandId, "commandId", 128),
            idempotencyOperation,
            new { scannerProfileId = normalizedProfileId, reason, commands = commands.Select(x => new { x.Operation, x.CommandText }).ToArray() },
            actor,
            async () =>
            {
                var normalizedReason = RequireValue(reason, "reason", 2000);
                var profile = await RequireDcr55ProfileAsync(normalizedProfileId, cancellationToken);
                var response = await ExecuteTransportCommandsAsync(profile, operation, commandId.Trim(), commands, cancellationToken);
                AddAudit(actor, profile, operation, commandId.Trim(), normalizedReason, response);
                return new CommandExecutionResult<ScannerControlResponse>(response, "ScannerProfile", profile.Id);
            },
            cancellationToken);
    }

    private async Task<ScannerProfile> RequireDcr55ProfileAsync(string scannerProfileId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.ScannerProfiles
            .SingleOrDefaultAsync(x => x.Id == scannerProfileId, cancellationToken)
            ?? throw new BusinessRuleException("scanner_profile_not_found", "Scanner profile was not found.", StatusCodes.Status404NotFound);

        if (!profile.Enabled)
        {
            throw new BusinessRuleException("scanner_profile_disabled", "Scanner profile is disabled.", StatusCodes.Status409Conflict);
        }

        if (!string.Equals(profile.ScannerType, ScannerTypes.Dcr55, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("scanner_type_not_supported", "Only DCR55 scanner profiles support this control operation.", StatusCodes.Status409Conflict);
        }

        return profile;
    }

    private async Task<ScannerControlResponse> ExecuteTransportCommandsAsync(
        ScannerProfile profile,
        string operation,
        string commandId,
        IReadOnlyList<ScannerControlCommand> commands,
        CancellationToken cancellationToken)
    {
        if (transport is null)
        {
            return Failure(
                commandId,
                profile.Id,
                operation,
                DeviceByteTransportStatuses.NotConnected,
                "dcr55_control_transport_missing",
                "No DCR55 byte transport is registered.",
                "not-registered",
                []);
        }

        if (!transport.IsConfigured)
        {
            return Failure(
                commandId,
                profile.Id,
                operation,
                DeviceByteTransportStatuses.NotConnected,
                "dcr55_control_transport_not_configured",
                "DCR55 byte transport is not configured.",
                transport.Name,
                []);
        }

        var timeout = TimeSpan.FromMilliseconds(profile.TimeoutMilliseconds ?? DefaultTimeoutMilliseconds);
        var steps = new List<ScannerControlStepResponse>();
        foreach (var command in commands)
        {
            var commandText = command.CommandText + Cr;
            var requestBytes = Ascii.GetBytes(commandText);
            DeviceByteTransportResult result;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                result = await transport.ExchangeAsync(
                    new DeviceByteTransportRequest(DeviceByteTransportEndpoints.Dcr55, command.Operation, requestBytes),
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                result = new DeviceByteTransportResult(
                    DeviceByteTransportStatuses.TimedOut,
                    [],
                    "dcr55_control_timeout",
                    "DCR55 control command timed out.");
            }

            var step = ToStepResponse(command.Operation, commandText, result);
            steps.Add(step);
            if (!string.Equals(result.Status, DeviceByteTransportStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    commandId,
                    profile.Id,
                    operation,
                    result.Status,
                    result.ErrorCode ?? "dcr55_control_failed",
                    result.Message ?? "DCR55 control command failed.",
                    transport.Name,
                    steps);
            }
        }

        return new ScannerControlResponse(
            true,
            commandId,
            false,
            profile.Id,
            operation,
            DeviceByteTransportStatuses.Succeeded,
            null,
            "DCR55 scanner control command completed.",
            transport.Name,
            steps);
    }

    private static ScannerControlStepResponse ToStepResponse(
        string operation,
        string commandText,
        DeviceByteTransportResult result)
    {
        var responseBytes = result.ResponseChunks.SelectMany(x => x).ToArray();
        return new ScannerControlStepResponse(
            operation,
            commandText,
            result.Status,
            result.ErrorCode,
            result.Message,
            DecodeAscii(responseBytes),
            Convert.ToHexString(responseBytes));
    }

    private static ScannerControlResponse Failure(
        string commandId,
        string scannerProfileId,
        string operation,
        string status,
        string errorCode,
        string message,
        string transportName,
        IReadOnlyList<ScannerControlStepResponse> steps)
    {
        return new ScannerControlResponse(
            false,
            commandId,
            false,
            scannerProfileId,
            operation,
            status,
            errorCode,
            message,
            transportName,
            steps);
    }

    private void AddAudit(
        AuthenticatedUser actor,
        ScannerProfile profile,
        string operation,
        string commandId,
        string reason,
        ScannerControlResponse response)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
            Action = ToAuditAction(operation),
            EntityType = "ScannerProfile",
            EntityId = profile.Id,
            Message = JsonSerializer.Serialize(new
            {
                commandId,
                operation,
                reason,
                response.Ok,
                response.Status,
                response.ErrorCode,
                response.TransportName,
                commands = response.Steps.Select(x => new { x.Operation, x.CommandText, x.Status, x.ErrorCode })
            }, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static void ValidateRoi(ScannerRoiRequest request)
    {
        if (request.Left < 0 || request.Top < 0)
        {
            throw new BusinessRuleException("scanner_roi_position_invalid", "ROI left and top must be non-negative.", StatusCodes.Status400BadRequest);
        }

        if (request.Width <= 0 || request.Height <= 0)
        {
            throw new BusinessRuleException("scanner_roi_size_invalid", "ROI width and height must be positive.", StatusCodes.Status400BadRequest);
        }
    }

    private static string RequireValue(string? value, string fieldName, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException($"{ToCode(fieldName)}_required", $"{fieldName} is required.", StatusCodes.Status400BadRequest);
        }

        if (normalized.Length > maxLength)
        {
            throw new BusinessRuleException($"{ToCode(fieldName)}_too_long", $"{fieldName} must be at most {maxLength} characters.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static string ToAuditAction(string operation) => operation switch
    {
        "RestartScanner" => "scanner_control.restart",
        "EnableCalibrationLight" => "scanner_control.calibration_light.enable",
        "DisableCalibrationLight" => "scanner_control.calibration_light.disable",
        "ApplyROI" => "scanner_control.roi.apply",
        "TriggerScanner" => "scanner_control.trigger",
        "ReadBarcode" => "scanner_control.barcode.read",
        _ => "scanner_control.command"
    };

    private static string DecodeAscii(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        return bytes.Any(x => x > 0x7F) ? string.Empty : Ascii.GetString(bytes);
    }

    private static string ToCode(string fieldName)
    {
        return string.Concat(fieldName.Select((ch, index) =>
            char.IsUpper(ch) && index > 0 ? $"_{char.ToLowerInvariant(ch)}" : char.ToLowerInvariant(ch).ToString()));
    }

    private sealed record ScannerControlCommand(string Operation, string CommandText);
}
