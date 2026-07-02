using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class EngineeringConfigService(
    StainerDbContext dbContext,
    EngineeringQueryService queryService,
    CoordinateProfileLifecycleService coordinateProfileLifecycleService,
    EngineeringWriteService engineeringWriteService,
    CommandIdempotencyService idempotencyService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ActiveRuntimeStatuses =
    [
        RuntimeLedgerStatus.Created,
        RuntimeLedgerStatus.Pending,
        RuntimeLedgerStatus.Running,
        RuntimeLedgerStatus.Paused,
        RuntimeLedgerStatus.Faulted,
        RuntimeLedgerStatus.WaitingUnload
    ];

    public async Task<EngineeringConfigExportResponse> ExportAsync(CancellationToken cancellationToken = default)
    {
        return new EngineeringConfigExportResponse(
            DateTimeOffset.UtcNow,
            await queryService.ListCoordinateProfilesAsync(cancellationToken),
            await queryService.ListLiquidClassesAsync(cancellationToken));
    }

    public async Task<CoordinateProfileVersionResponse?> GetCoordinateVersionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var profiles = await queryService.ListCoordinateProfilesAsync(cancellationToken);
        return profiles.SelectMany(x => x.Versions).SingleOrDefault(x => x.Id == versionId);
    }

    public async Task<IReadOnlyList<EngineeringConfigDiffItemResponse>> DiffCoordinateVersionAsync(
        string versionId,
        string? sourceVersionId,
        CancellationToken cancellationToken = default)
    {
        var profiles = await queryService.ListCoordinateProfilesAsync(cancellationToken);
        var versions = profiles.SelectMany(x => x.Versions).ToList();
        var target = versions.SingleOrDefault(x => x.Id == versionId)
            ?? throw new BusinessRuleException("coordinate_version_not_found", "Coordinate profile version was not found.", StatusCodes.Status404NotFound);
        var source = string.IsNullOrWhiteSpace(sourceVersionId)
            ? versions.SingleOrDefault(x => x.Id == target.SourceVersionId)
            : versions.SingleOrDefault(x => x.Id == sourceVersionId.Trim());
        return DiffCoordinatePoints(source?.TargetPoints ?? [], target.TargetPoints);
    }

    public async Task<LiquidClassVersionResponse?> GetLiquidClassVersionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var profiles = await queryService.ListLiquidClassesAsync(cancellationToken);
        return profiles.SelectMany(x => x.Versions).SingleOrDefault(x => x.Id == versionId);
    }

    public async Task<IReadOnlyList<EngineeringConfigDiffItemResponse>> DiffLiquidClassVersionAsync(
        string versionId,
        string? sourceVersionId,
        CancellationToken cancellationToken = default)
    {
        var profiles = await queryService.ListLiquidClassesAsync(cancellationToken);
        var versions = profiles.SelectMany(x => x.Versions).ToList();
        var target = versions.SingleOrDefault(x => x.Id == versionId)
            ?? throw new BusinessRuleException("liquid_class_version_not_found", "Liquid Class version was not found.", StatusCodes.Status404NotFound);
        var source = string.IsNullOrWhiteSpace(sourceVersionId)
            ? versions.SingleOrDefault(x => x.Id == target.SourceVersionId)
            : versions.SingleOrDefault(x => x.Id == sourceVersionId.Trim());
        return DiffLiquidVersions(source, target);
    }

    public Task<EngineeringConfigImportPreviewResponse> PreviewImportAsync(
        PreviewEngineeringConfigImportRequest request,
        CancellationToken cancellationToken = default)
    {
        return BuildPreviewAsync(NormalizeConfigType(request.ConfigType), RequireValue(request.TargetCode, "targetCode"), request.Payload, cancellationToken);
    }

    public async Task<object> ApplyImportAsync(
        ApplyEngineeringConfigImportRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        var configType = NormalizeConfigType(request.ConfigType);
        var targetCode = RequireValue(request.TargetCode, "targetCode");
        var preview = await BuildPreviewAsync(configType, targetCode, request.Payload, cancellationToken);
        if (!preview.Ok)
        {
            throw new BusinessRuleException("engineering_config_import_invalid", string.Join(" ", preview.Errors), StatusCodes.Status400BadRequest);
        }

        if (configType == EngineeringConfigTypes.CoordinateProfile)
        {
            var payload = DeserializePayload<CoordinateImportPayload>(request.Payload, "coordinate_payload_invalid");
            return await coordinateProfileLifecycleService.CreateVersionAsync(
                new CreateCoordinateProfileVersionRequest(
                    request.CommandId,
                    targetCode,
                    payload.SourceVersionId,
                    preview.VersionLabel,
                    request.Reason,
                    payload.TargetPoints ?? [],
                    payload.ValidationResultJson,
                    request.Target,
                    request.DangerousOperationConfirmed),
                actor,
                cancellationToken);
        }

        var liquidPayload = DeserializePayload<LiquidClassImportPayload>(request.Payload, "liquid_class_payload_invalid");
        return await engineeringWriteService.SaveLiquidClassAsync(
            new SaveLiquidClassRequest(
                request.CommandId,
                targetCode,
                RequireValue(liquidPayload.Name, "name"),
                liquidPayload.AspirateSpeedUlPerSecond,
                liquidPayload.DispenseSpeedUlPerSecond,
                liquidPayload.LeadingAirGapUl,
                liquidPayload.TrailingAirGapUl,
                liquidPayload.ExcessVolumeUl,
                liquidPayload.PreWetCycles,
                liquidPayload.MixCycles,
                false,
                request.Reason,
                liquidPayload.SourceVersionId,
                preview.VersionLabel,
                liquidPayload.LiquidDetectionEnabled,
                liquidPayload.LiquidDetectionSensitivityPercent,
                liquidPayload.LiquidDetectionSpeedUmPerSecond,
                liquidPayload.AspirateDelayMs,
                liquidPayload.DispenseDelayMs,
                liquidPayload.BlowoutVolumeUl,
                liquidPayload.BlowoutDelayMs,
                liquidPayload.VolumeAdjustmentUl,
                request.Target,
                request.DangerousOperationConfirmed),
            actor,
            cancellationToken);
    }

    public Task<CoordinateProfileVersionMutationResponse> DeactivateCoordinateVersionAsync(
        string versionId,
        DeactivateCoordinateProfileVersionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "coordinate_profile.version.deactivate",
            new { versionId, request },
            actor,
            async () =>
            {
                var version = await dbContext.CoordinateProfileVersions
                    .Include(x => x.CoordinateProfile)
                    .SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
                    ?? throw new BusinessRuleException("coordinate_version_not_found", "Coordinate profile version was not found.", StatusCodes.Status404NotFound);
                await EnsureCoordinateVersionNotReferencedByActiveWorkAsync(version.Id, cancellationToken);
                var reason = RequireValue(request.Reason, "reason");
                var now = DateTimeOffset.UtcNow;
                version.Status = CoordinateProfileVersionStatus.Retired;
                version.IsActive = false;
                version.RetiredAtUtc ??= now;
                if (version.CoordinateProfile?.ActiveVersionId == version.Id)
                {
                    version.CoordinateProfile.ActiveVersionId = null;
                    version.CoordinateProfile.IsActive = false;
                    version.CoordinateProfile.Status = CoordinateProfileStatus.Disabled;
                }

                AddAudit(actor, "coordinate_profile.version.deactivate", "CoordinateProfileVersion", version.Id, new
                {
                    request.CommandId,
                    reason,
                    target = request.Target ?? version.Id
                });
                return new CommandExecutionResult<CoordinateProfileVersionMutationResponse>(
                    new CoordinateProfileVersionMutationResponse(
                        true,
                        request.CommandId,
                        false,
                        version.CoordinateProfileId,
                        version.Id,
                        version.VersionNo,
                        version.VersionLabel,
                        version.Status,
                        version.IsActive,
                        version.UsageScope,
                        version.VerificationStatus,
                        "Coordinate profile version deactivated."),
                    "CoordinateProfileVersion",
                    version.Id);
            },
            cancellationToken);
    }

    public Task<LiquidClassVersionMutationResponse> DisableLiquidClassVersionAsync(
        string versionId,
        DisableLiquidClassVersionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "engineering.liquid_class.version.disable",
            new { versionId, request },
            actor,
            async () =>
            {
                var version = await dbContext.LiquidClassVersions
                    .Include(x => x.LiquidClassProfile)
                    .SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
                    ?? throw new BusinessRuleException("liquid_class_version_not_found", "Liquid Class version was not found.", StatusCodes.Status404NotFound);
                if (version.Status != LiquidClassVersionStatus.Enabled)
                {
                    throw new BusinessRuleException("liquid_class_version_not_enabled", "Only enabled Liquid Class versions can be disabled.", StatusCodes.Status409Conflict);
                }

                await EnsureLiquidClassVersionNotReferencedByActiveWorkAsync(version.Id, cancellationToken);
                var reason = RequireValue(request.Reason, "reason");
                version.Status = LiquidClassVersionStatus.Published;
                var profile = version.LiquidClassProfile!;
                if (profile.EnabledVersionId == version.Id)
                {
                    profile.EnabledVersionId = null;
                    profile.IsEnabled = false;
                    profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }

                AddAudit(actor, "engineering.liquid_class.version.disable", "LiquidClassVersion", version.Id, new
                {
                    request.CommandId,
                    reason,
                    target = request.Target ?? version.Id
                });
                return new CommandExecutionResult<LiquidClassVersionMutationResponse>(
                    new LiquidClassVersionMutationResponse(
                        true,
                        request.CommandId,
                        false,
                        profile.Id,
                        version.Id,
                        version.VersionNo,
                        version.VersionLabel,
                        version.Status,
                        false,
                        "Liquid Class version disabled."),
                    "LiquidClassVersion",
                    version.Id);
            },
            cancellationToken);
    }

    private async Task<EngineeringConfigImportPreviewResponse> BuildPreviewAsync(
        string configType,
        string targetCode,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        return configType == EngineeringConfigTypes.CoordinateProfile
            ? await BuildCoordinatePreviewAsync(targetCode, payload, cancellationToken)
            : await BuildLiquidClassPreviewAsync(targetCode, payload, cancellationToken);
    }

    private async Task<EngineeringConfigImportPreviewResponse> BuildCoordinatePreviewAsync(
        string targetCode,
        JsonElement payloadJson,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        CoordinateImportPayload payload;
        try
        {
            payload = DeserializePayload<CoordinateImportPayload>(payloadJson, "coordinate_payload_invalid");
        }
        catch (BusinessRuleException ex)
        {
            return Preview(false, EngineeringConfigTypes.CoordinateProfile, targetCode, null, string.Empty, [], [ex.Message]);
        }

        var profile = await dbContext.CoordinateProfiles
            .AsNoTracking()
            .Include(x => x.Versions)
            .ThenInclude(x => x.TargetPoints)
            .SingleOrDefaultAsync(x => x.Code == targetCode, cancellationToken);
        if (profile is null)
        {
            errors.Add("Coordinate profile was not found.");
        }

        var versionLabel = RequireValueOrDefault(payload.VersionLabel, $"import-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
        if (profile?.Versions.Any(x => x.VersionLabel == versionLabel) == true)
        {
            errors.Add("Coordinate version label already exists.");
        }

        var source = ResolveCoordinateSource(profile, payload.SourceVersionId);
        if (source is null)
        {
            errors.Add("Source coordinate version was not found.");
        }

        if (payload.TargetPoints is null || payload.TargetPoints.Count == 0)
        {
            errors.Add("At least one coordinate target point is required.");
        }

        var diffs = source is null || payload.TargetPoints is null
            ? []
            : DiffCoordinateImport(source.TargetPoints, payload.TargetPoints);
        return Preview(errors.Count == 0, EngineeringConfigTypes.CoordinateProfile, targetCode, source?.Id, versionLabel, diffs, errors);
    }

    private async Task<EngineeringConfigImportPreviewResponse> BuildLiquidClassPreviewAsync(
        string targetCode,
        JsonElement payloadJson,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        LiquidClassImportPayload payload;
        try
        {
            payload = DeserializePayload<LiquidClassImportPayload>(payloadJson, "liquid_class_payload_invalid");
        }
        catch (BusinessRuleException ex)
        {
            return Preview(false, EngineeringConfigTypes.LiquidClass, targetCode, null, string.Empty, [], [ex.Message]);
        }

        var profile = await dbContext.LiquidClassProfiles
            .AsNoTracking()
            .Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.Code == targetCode, cancellationToken);
        var source = ResolveLiquidSource(profile, payload.SourceVersionId);
        var versionLabel = RequireValueOrDefault(payload.VersionLabel, $"import-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
        if (profile?.Versions.Any(x => x.VersionLabel == versionLabel) == true)
        {
            errors.Add("Liquid Class version label already exists.");
        }

        if (string.IsNullOrWhiteSpace(payload.Name) && source is null)
        {
            errors.Add("name is required for a new Liquid Class.");
        }

        ValidateRange(errors, "aspirateSpeedUlPerSecond", payload.AspirateSpeedUlPerSecond ?? source?.AspirateSpeedUlPerSecond, 1, 10_000);
        ValidateRange(errors, "dispenseSpeedUlPerSecond", payload.DispenseSpeedUlPerSecond ?? source?.DispenseSpeedUlPerSecond, 1, 10_000);
        ValidateRange(errors, "preWetCycles", payload.PreWetCycles ?? source?.PreWetCycles, 0, 20);
        ValidateRange(errors, "mixCycles", payload.MixCycles ?? source?.MixCycles, 0, 20);
        var diffs = DiffLiquidImport(source, payload);
        return Preview(errors.Count == 0, EngineeringConfigTypes.LiquidClass, targetCode, source?.Id, versionLabel, diffs, errors);
    }

    private async Task EnsureCoordinateVersionNotReferencedByActiveWorkAsync(string versionId, CancellationToken cancellationToken)
    {
        var referenced = await dbContext.ChannelBatches.AsNoTracking().AnyAsync(
                x => x.CoordinateProfileVersionId == versionId && ActiveRuntimeStatuses.Contains(x.Status),
                cancellationToken)
            || await dbContext.MachineRuns.AsNoTracking().AnyAsync(
                x => x.CoordinateProfileVersionId == versionId && ActiveRuntimeStatuses.Contains(x.Status),
                cancellationToken);
        if (referenced)
        {
            throw new BusinessRuleException("coordinate_version_active_reference", "Coordinate version is referenced by active work and cannot be modified or deactivated.", StatusCodes.Status409Conflict);
        }
    }

    private async Task EnsureLiquidClassVersionNotReferencedByActiveWorkAsync(string versionId, CancellationToken cancellationToken)
    {
        var referenced = await dbContext.ChannelBatches.AsNoTracking().AnyAsync(
                x => x.LiquidClassSnapshotJson.Contains(versionId) && ActiveRuntimeStatuses.Contains(x.Status),
                cancellationToken)
            || await dbContext.MachineRuns.AsNoTracking().AnyAsync(
                x => x.LiquidClassSnapshotJson.Contains(versionId) && ActiveRuntimeStatuses.Contains(x.Status),
                cancellationToken)
            || await dbContext.DeviceCommandExecutions.AsNoTracking().AnyAsync(
                x => x.LiquidClassVersionId == versionId && x.Status != DeviceCommandStatus.Completed && x.Status != DeviceCommandStatus.Failed && x.Status != DeviceCommandStatus.Unknown,
                cancellationToken);
        if (referenced)
        {
            throw new BusinessRuleException("liquid_class_version_active_reference", "Liquid Class version is referenced by active work and cannot be disabled.", StatusCodes.Status409Conflict);
        }
    }

    private static CoordinateProfileVersion? ResolveCoordinateSource(CoordinateProfile? profile, string? sourceVersionId)
    {
        if (profile is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(sourceVersionId)
            ? profile.Versions.SingleOrDefault(x => x.IsActive)
            : profile.Versions.SingleOrDefault(x => x.Id == sourceVersionId.Trim());
    }

    private static LiquidClassVersion? ResolveLiquidSource(LiquidClassProfile? profile, string? sourceVersionId)
    {
        if (profile is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(sourceVersionId)
            ? profile.Versions.SingleOrDefault(x => x.Id == profile.EnabledVersionId)
            : profile.Versions.SingleOrDefault(x => x.Id == sourceVersionId.Trim());
    }

    private static IReadOnlyList<EngineeringConfigDiffItemResponse> DiffCoordinateImport(
        IEnumerable<CoordinatePoint> sourcePoints,
        IEnumerable<CoordinateTargetPointInput> requestedPoints)
    {
        var source = sourcePoints.ToDictionary(x => x.PointCode, StringComparer.Ordinal);
        var diffs = new List<EngineeringConfigDiffItemResponse>();
        foreach (var input in requestedPoints)
        {
            var pointCode = RequireValue(input.PointCode, "pointCode");
            if (!source.TryGetValue(pointCode, out var before))
            {
                diffs.Add(new EngineeringConfigDiffItemResponse($"targetPoints.{pointCode}", null, JsonSerializer.Serialize(input, JsonOptions), "Added"));
                continue;
            }

            AddDiff(diffs, $"targetPoints.{pointCode}.xUm", before.CalibratedXUm?.ToString(), input.XUm?.ToString());
            AddDiff(diffs, $"targetPoints.{pointCode}.yUm", before.CalibratedYUm?.ToString(), input.YUm?.ToString());
            AddDiff(diffs, $"targetPoints.{pointCode}.zUm", before.CalibratedZUm?.ToString(), input.ZUm?.ToString());
            AddDiff(diffs, $"targetPoints.{pointCode}.safeZUm", before.SafeZUm?.ToString(), input.SafeZUm?.ToString());
            AddDiff(diffs, $"targetPoints.{pointCode}.liquidDetectZUm", before.LiquidDetectZUm?.ToString(), input.LiquidDetectZUm?.ToString());
            AddDiff(diffs, $"targetPoints.{pointCode}.dispenseZUm", before.DispenseZUm?.ToString(), input.DispenseZUm?.ToString());
            AddDiff(diffs, $"targetPoints.{pointCode}.isEnabled", before.IsEnabled.ToString(), input.IsEnabled?.ToString());
        }

        return diffs;
    }

    private static IReadOnlyList<EngineeringConfigDiffItemResponse> DiffCoordinatePoints(
        IEnumerable<CoordinatePointResponse> sourcePoints,
        IEnumerable<CoordinatePointResponse> targetPoints)
    {
        var source = sourcePoints.ToDictionary(x => x.PointCode, StringComparer.Ordinal);
        var diffs = new List<EngineeringConfigDiffItemResponse>();
        foreach (var target in targetPoints)
        {
            if (!source.TryGetValue(target.PointCode, out var before))
            {
                diffs.Add(new EngineeringConfigDiffItemResponse($"targetPoints.{target.PointCode}", null, JsonSerializer.Serialize(target, JsonOptions), "Added"));
                continue;
            }

            AddDiff(diffs, $"targetPoints.{target.PointCode}.xUm", before.CalibratedXUm?.ToString(), target.CalibratedXUm?.ToString());
            AddDiff(diffs, $"targetPoints.{target.PointCode}.yUm", before.CalibratedYUm?.ToString(), target.CalibratedYUm?.ToString());
            AddDiff(diffs, $"targetPoints.{target.PointCode}.zUm", before.CalibratedZUm?.ToString(), target.CalibratedZUm?.ToString());
            AddDiff(diffs, $"targetPoints.{target.PointCode}.isEnabled", before.IsEnabled.ToString(), target.IsEnabled.ToString());
        }

        return diffs;
    }

    private static IReadOnlyList<EngineeringConfigDiffItemResponse> DiffLiquidImport(LiquidClassVersion? source, LiquidClassImportPayload payload)
    {
        var diffs = new List<EngineeringConfigDiffItemResponse>();
        AddDiff(diffs, "name", source?.Name, payload.Name);
        AddDiff(diffs, "aspirateSpeedUlPerSecond", source?.AspirateSpeedUlPerSecond.ToString(), payload.AspirateSpeedUlPerSecond?.ToString());
        AddDiff(diffs, "dispenseSpeedUlPerSecond", source?.DispenseSpeedUlPerSecond.ToString(), payload.DispenseSpeedUlPerSecond?.ToString());
        AddDiff(diffs, "leadingAirGapUl", source?.LeadingAirGapUl.ToString(), payload.LeadingAirGapUl?.ToString());
        AddDiff(diffs, "trailingAirGapUl", source?.TrailingAirGapUl.ToString(), payload.TrailingAirGapUl?.ToString());
        AddDiff(diffs, "volumeAdjustmentUl", source?.VolumeAdjustmentUl.ToString(), payload.VolumeAdjustmentUl?.ToString());
        AddDiff(diffs, "preWetCycles", source?.PreWetCycles.ToString(), payload.PreWetCycles?.ToString());
        AddDiff(diffs, "mixCycles", source?.MixCycles.ToString(), payload.MixCycles?.ToString());
        return diffs;
    }

    private static IReadOnlyList<EngineeringConfigDiffItemResponse> DiffLiquidVersions(LiquidClassVersionResponse? source, LiquidClassVersionResponse target)
    {
        var diffs = new List<EngineeringConfigDiffItemResponse>();
        AddDiff(diffs, "name", source?.Name, target.Name);
        AddDiff(diffs, "aspirateSpeedUlPerSecond", source?.AspirateSpeedUlPerSecond.ToString(), target.AspirateSpeedUlPerSecond.ToString());
        AddDiff(diffs, "dispenseSpeedUlPerSecond", source?.DispenseSpeedUlPerSecond.ToString(), target.DispenseSpeedUlPerSecond.ToString());
        AddDiff(diffs, "leadingAirGapUl", source?.LeadingAirGapUl.ToString(), target.LeadingAirGapUl.ToString());
        AddDiff(diffs, "trailingAirGapUl", source?.TrailingAirGapUl.ToString(), target.TrailingAirGapUl.ToString());
        AddDiff(diffs, "volumeAdjustmentUl", source?.VolumeAdjustmentUl.ToString(), target.VolumeAdjustmentUl.ToString());
        AddDiff(diffs, "preWetCycles", source?.PreWetCycles.ToString(), target.PreWetCycles.ToString());
        AddDiff(diffs, "mixCycles", source?.MixCycles.ToString(), target.MixCycles.ToString());
        return diffs;
    }

    private static void AddDiff(List<EngineeringConfigDiffItemResponse> diffs, string path, string? before, string? after)
    {
        if (string.IsNullOrWhiteSpace(after) || string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        diffs.Add(new EngineeringConfigDiffItemResponse(path, before, after, before is null ? "Added" : "Changed"));
    }

    private void AddAudit(AuthenticatedUser actor, string action, string entityType, string entityId, object details)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Message = JsonSerializer.Serialize(details, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static EngineeringConfigImportPreviewResponse Preview(
        bool ok,
        string configType,
        string targetCode,
        string? sourceVersionId,
        string versionLabel,
        IReadOnlyList<EngineeringConfigDiffItemResponse> diffs,
        IReadOnlyList<string> errors)
    {
        return new EngineeringConfigImportPreviewResponse(
            ok,
            configType,
            targetCode,
            sourceVersionId,
            versionLabel,
            diffs.Count > 0,
            diffs,
            errors);
    }

    private static T DeserializePayload<T>(JsonElement payload, string code)
    {
        try
        {
            return payload.Deserialize<T>(JsonOptions)
                ?? throw new BusinessRuleException(code, "Configuration import payload is empty.", StatusCodes.Status400BadRequest);
        }
        catch (JsonException ex)
        {
            throw new BusinessRuleException(code, $"Configuration import payload is invalid: {ex.Message}", StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidateRange(List<string> errors, string name, int? value, int minimum, int maximum)
    {
        if (value is null)
        {
            return;
        }

        if (value < minimum || value > maximum)
        {
            errors.Add($"{name} must be between {minimum} and {maximum}.");
        }
    }

    private static string NormalizeConfigType(string configType)
    {
        var normalized = RequireValue(configType, "configType");
        if (string.Equals(normalized, "coordinate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "coordinate-profile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, EngineeringConfigTypes.CoordinateProfile, StringComparison.OrdinalIgnoreCase))
        {
            return EngineeringConfigTypes.CoordinateProfile;
        }

        if (string.Equals(normalized, "liquid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "liquid-class", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, EngineeringConfigTypes.LiquidClass, StringComparison.OrdinalIgnoreCase))
        {
            return EngineeringConfigTypes.LiquidClass;
        }

        throw new BusinessRuleException("config_type_invalid", "Config type must be coordinate-profile or liquid-class.", StatusCodes.Status400BadRequest);
    }

    private static string RequireValueOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string RequireValue(string? value, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BusinessRuleException($"{fieldName}_required", $"{fieldName} is required.", StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private sealed record CoordinateImportPayload(
        string? VersionLabel,
        string? SourceVersionId,
        string? ValidationResultJson,
        IReadOnlyList<CoordinateTargetPointInput>? TargetPoints);

    private sealed record LiquidClassImportPayload(
        string? VersionLabel,
        string? SourceVersionId,
        string? Name,
        bool? LiquidDetectionEnabled,
        int? LiquidDetectionSensitivityPercent,
        int? LiquidDetectionSpeedUmPerSecond,
        int? AspirateSpeedUlPerSecond,
        int? AspirateDelayMs,
        int? DispenseSpeedUlPerSecond,
        int? DispenseDelayMs,
        int? LeadingAirGapUl,
        int? TrailingAirGapUl,
        int? ExcessVolumeUl,
        int? BlowoutVolumeUl,
        int? BlowoutDelayMs,
        int? VolumeAdjustmentUl,
        int? PreWetCycles,
        int? MixCycles);

    private static class EngineeringConfigTypes
    {
        public const string CoordinateProfile = "coordinate-profile";
        public const string LiquidClass = "liquid-class";
    }
}
