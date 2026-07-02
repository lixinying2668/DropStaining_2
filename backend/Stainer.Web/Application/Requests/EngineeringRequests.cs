using System.Text.Json;

namespace Stainer.Web.Application.Requests;

public sealed record StartEngineeringSessionRequest(
    string CommandId,
    string Password,
    string Reason,
    string Target,
    bool DangerousOperationConfirmed = false,
    int? DurationMinutes = null);

public sealed record EndEngineeringSessionRequest(
    string CommandId,
    string Reason,
    string Target);

public sealed record PreviewEngineeringConfigImportRequest(
    string ConfigType,
    string TargetCode,
    JsonElement Payload);

public sealed record ApplyEngineeringConfigImportRequest(
    string CommandId,
    string ConfigType,
    string TargetCode,
    string Reason,
    JsonElement Payload,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record DeactivateCoordinateProfileVersionRequest(
    string CommandId,
    string Reason,
    string? Target = null,
    bool DangerousOperationConfirmed = false);

public sealed record DisableLiquidClassVersionRequest(
    string CommandId,
    string Reason,
    string? Target = null,
    bool DangerousOperationConfirmed = false);
