using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class DeviceModeService(
    IConfiguration configuration,
    IDeviceAdapter deviceAdapter,
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    SafetyLogWriter safetyLogWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string CurrentMode => deviceAdapter.Mode;

    public string ConfiguredMode => DeviceModes.Normalize(configuration["Device:Mode"]);

    public bool RealDeviceHealthCheckComplete => bool.TryParse(configuration["Device:RealHealthCheckComplete"], out var value) && value;

    public bool IsMock => CurrentMode == DeviceModes.Mock;

    public bool IsReal => CurrentMode == DeviceModes.Real;

    public bool CanStartRuns => IsMock || RealDeviceHealthCheckComplete;

    public async Task<DeviceModeStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var pendingRequestedMode = await LatestRequestedModeAsync(cancellationToken);
        var mode = CurrentMode;
        var configuredMode = ConfiguredMode;
        var realHealthOk = RealDeviceHealthCheckComplete;
        return new DeviceModeStatusResponse(
            mode,
            configuredMode,
            mode == DeviceModes.Mock,
            mode == DeviceModes.Real,
            realHealthOk,
            mode == DeviceModes.Mock || realHealthOk,
            true,
            pendingRequestedMode,
            configuredMode == mode ? "Configuration" : deviceAdapter.Name,
            mode == DeviceModes.Real && !realHealthOk
                ? "Real mode is configured, but device health check has not completed. Run start is blocked."
                : configuredMode != mode
                ? $"Configured DeviceMode is {configuredMode}, effective mode is {mode} through {deviceAdapter.Name}."
                : $"Device mode is {mode}.");
    }

    public Task<DeviceModeChangeResponse> RequestModeChangeAsync(
        DeviceModeChangeRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "device.mode.change_request",
            request,
            actor,
            async () =>
            {
                var requestedMode = DeviceModes.Normalize(request.DeviceMode);
                if (requestedMode is not (DeviceModes.Mock or DeviceModes.Real))
                {
                    throw new BusinessRuleException("device_mode_invalid", "DeviceMode must be Mock or Real.", StatusCodes.Status400BadRequest);
                }

                var reason = request.Reason?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new BusinessRuleException("reason_required", "Changing DeviceMode requires a reason.", StatusCodes.Status400BadRequest);
                }

                var currentMode = ConfiguredMode;
                dbContext.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
                    Action = "device.mode.change_requested",
                    EntityType = "DeviceMode",
                    EntityId = requestedMode,
                    Message = JsonSerializer.Serialize(new
                    {
                        commandId = request.CommandId,
                        currentMode,
                        requestedMode,
                        reason,
                        restartRequired = true,
                        realDeviceHealthCheckComplete = RealDeviceHealthCheckComplete
                    }, JsonOptions),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });

                await safetyLogWriter.WriteAsync(
                    "runtime",
                    "Information",
                    $"DeviceMode change requested: {currentMode} -> {requestedMode}",
                    new SafetyLogContext(
                        CommandId: request.CommandId,
                        DeviceMode: currentMode,
                        Actor: actor.Username,
                        Source: "DeviceModeService"),
                    cancellationToken: cancellationToken);

                return new CommandExecutionResult<DeviceModeChangeResponse>(
                    new DeviceModeChangeResponse(
                        true,
                        request.CommandId,
                        false,
                        currentMode,
                        requestedMode,
                        true,
                        "DeviceMode change was recorded. Update configuration and restart the service for it to take effect."),
                    "DeviceMode",
                    requestedMode);
            },
            cancellationToken);
    }

    public void EnsureRunStartAllowed()
    {
        if (IsReal && !RealDeviceHealthCheckComplete)
        {
            throw new BusinessRuleException("real_device_health_required", "Real mode requires completed device health checks before a run can start.", StatusCodes.Status409Conflict);
        }
    }

    private async Task<string?> LatestRequestedModeAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.AuditLogs
            .AsNoTracking()
            .Where(x => x.Action == "device.mode.change_requested")
            .Select(x => new { x.Message, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var latest = rows.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        if (latest is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(latest.Message);
            return document.RootElement.TryGetProperty("requestedMode", out var mode) ? mode.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

}

public static class DeviceModes
{
    public const string Mock = "Mock";
    public const string Real = "Real";

    public static string Normalize(string? value)
    {
        return string.Equals(value, Real, StringComparison.OrdinalIgnoreCase) ? Real : Mock;
    }
}
