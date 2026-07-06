using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class CoordinateProfileLifecycleService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> RequiredTargetPointCodes { get; } = BuildRequiredTargetPointCodes();

    public async Task<(string VersionId, string SnapshotJson)> FreezeCurrentActiveVersionAsync(CancellationToken cancellationToken = default)
    {
        var version = await LoadActiveVersionAsync(cancellationToken);
        return (version.Id, CoordinateSnapshotFactory.Create(version));
    }

    public async Task EnsureRunCoordinateUsableAsync(
        string runId,
        string deviceMode,
        CancellationToken cancellationToken = default)
    {
        var run = await dbContext.MachineRuns
            .AsNoTracking()
            .Include(x => x.CoordinateProfileVersion)
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new BusinessRuleException("run_not_found", "Run was not found.", StatusCodes.Status404NotFound);
        var version = run.CoordinateProfileVersion;
        if (version is null
            || string.IsNullOrWhiteSpace(run.CoordinateSnapshotJson)
            || run.CoordinateSnapshotJson == "{}")
        {
            throw new BusinessRuleException(
                "run_coordinate_snapshot_required",
                "Run does not have a frozen coordinate version snapshot.",
                StatusCodes.Status409Conflict);
        }

        if (DeviceModes.Normalize(deviceMode) == DeviceModes.Mock)
        {
            return;
        }

        if (version.VerificationStatus != CoordinateVersionVerificationStatus.EngineerVerified
            || version.UsageScope != CoordinateVersionUsageScope.RealEligible
            || version.PublishedAtUtc is null)
        {
            throw new BusinessRuleException(
                "coordinate_version_not_real_verified",
                $"Real mode rejects coordinate version {version.VersionLabel}: engineer verification and publication are required before robot movement, liquid detection, aspiration, or dispensing.",
                StatusCodes.Status409Conflict);
        }

        var points = await dbContext.CoordinatePoints
            .AsNoTracking()
            .Where(x => x.CoordinateProfileVersionId == version.Id)
            .ToListAsync(cancellationToken);
        var readiness = EvaluateRealReadiness(version, points);
        if (!readiness.Ok)
        {
            throw new BusinessRuleException(
                "coordinate_version_not_real_ready",
                readiness.BlockingReasons[0],
                StatusCodes.Status409Conflict);
        }
    }

    public Task<CoordinateProfileVersionMutationResponse> CreateVersionAsync(
        CreateCoordinateProfileVersionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "coordinate_profile.version.create",
            request,
            actor,
            async () =>
            {
                RequireEngineer(actor);
                var reason = RequireValue(request.Reason, "reason");
                var profileCode = RequireValue(request.ProfileCode, "profileCode");
                var profile = await dbContext.CoordinateProfiles
                    .Include(x => x.Versions)
                    .SingleOrDefaultAsync(x => x.Code == profileCode, cancellationToken)
                    ?? throw new BusinessRuleException("coordinate_profile_not_found", "Coordinate profile was not found.", StatusCodes.Status404NotFound);

                if (profile.Status == CoordinateProfileStatus.NeedsManualResolution)
                {
                    throw new BusinessRuleException("coordinate_profile_needs_manual_resolution", "Coordinate profile needs manual resolution before creating a version.", StatusCodes.Status409Conflict);
                }

                var sourceVersion = await LoadSourceVersionAsync(profile.Id, request.SourceVersionId, cancellationToken);
                var versionNo = profile.Versions.Count == 0 ? 1 : profile.Versions.Max(x => x.VersionNo) + 1;
                var versionLabel = RequireValue(request.VersionLabel, "versionLabel");
                if (profile.Versions.Any(x => x.VersionLabel == versionLabel))
                {
                    throw new BusinessRuleException("coordinate_version_label_exists", "Coordinate version label already exists.", StatusCodes.Status409Conflict);
                }

                var version = new CoordinateProfileVersion
                {
                    CoordinateProfile = profile,
                    VersionNo = versionNo,
                    VersionLabel = versionLabel,
                    Status = CoordinateProfileVersionStatus.Draft,
                    UsageScope = CoordinateVersionUsageScope.MockOnly,
                    VerificationStatus = CoordinateVersionVerificationStatus.Unverified,
                    SourceVersionId = sourceVersion.Id,
                    ChangeReason = reason,
                    ChangeSummaryJson = "{}",
                    ValidationResultJson = NormalizeJson(request.ValidationResultJson),
                    CreatedByUserId = actor.UserId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                dbContext.CoordinateProfileVersions.Add(version);

                var sourcePoints = await dbContext.CoordinatePoints
                    .AsNoTracking()
                    .Where(x => x.CoordinateProfileVersionId == sourceVersion.Id)
                    .OrderBy(x => x.PointCode)
                    .ToListAsync(cancellationToken);
                if (sourcePoints.Count == 0)
                {
                    sourcePoints = await dbContext.CoordinatePoints
                        .AsNoTracking()
                        .Where(x => x.CoordinateProfileId == profile.Id)
                        .OrderBy(x => x.PointCode)
                        .ToListAsync(cancellationToken);
                }

                var requestedPoints = request.TargetPoints
                    .GroupBy(x => NormalizeCode(x.PointCode), StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
                var changes = new List<object>();
                foreach (var source in sourcePoints)
                {
                    var target = CopyPoint(source, version);
                    if (requestedPoints.Remove(source.PointCode, out var update))
                    {
                        ApplyUpdate(target, update);
                        changes.Add(BuildDiff(source, target));
                    }

                    version.TargetPoints.Add(target);
                }

                foreach (var update in requestedPoints.Values)
                {
                    var point = NewPoint(profile.Id, version, update);
                    version.TargetPoints.Add(point);
                    changes.Add(new { pointCode = point.PointCode, action = "added" });
                }

                version.ChangeSummaryJson = JsonSerializer.Serialize(new { reason, changes }, JsonOptions);
                AddAudit(actor, "coordinate_profile.version.create", "CoordinateProfileVersion", version.Id, new
                {
                    profile.Code,
                    version.VersionNo,
                    version.VersionLabel,
                    sourceVersionId = sourceVersion.Id,
                    reason,
                    changes
                });

                var response = ToMutationResponse(request.CommandId, version, "Coordinate profile version created.");
                return new CommandExecutionResult<CoordinateProfileVersionMutationResponse>(response, "CoordinateProfileVersion", version.Id);
            },
            cancellationToken);
    }

    public Task<CoordinateProfileVersionMutationResponse> PublishAsync(
        string versionId,
        PublishCoordinateProfileVersionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "coordinate_profile.version.publish",
            new { versionId, request },
            actor,
            async () =>
            {
                RequireEngineer(actor);
                var reason = RequireValue(request.Reason, "reason");
                var validation = NormalizeJson(RequireValue(request.ValidationResultJson, "validationResultJson"));
                var version = await LoadVersionWithPointsAsync(versionId, cancellationToken);
                if (version.Status != CoordinateProfileVersionStatus.Draft)
                {
                    throw new BusinessRuleException("coordinate_version_not_draft", "Only Draft coordinate versions can be published.", StatusCodes.Status409Conflict);
                }

                ValidateRequiredTargetPoints(version.TargetPoints);
                version.Status = CoordinateProfileVersionStatus.Published;
                version.UsageScope = CoordinateVersionUsageScope.RealEligible;
                version.VerificationStatus = CoordinateVersionVerificationStatus.EngineerVerified;
                version.ValidationResultJson = validation;
                version.PublishedByUserId = actor.UserId;
                version.PublishedAtUtc = DateTimeOffset.UtcNow;
                AddAudit(actor, "coordinate_profile.version.publish", "CoordinateProfileVersion", version.Id, new
                {
                    version.CoordinateProfile?.Code,
                    version.VersionLabel,
                    reason,
                    version.UsageScope,
                    version.VerificationStatus,
                    validationResultJson = validation
                });

                var response = ToMutationResponse(request.CommandId, version, "Coordinate profile version published.");
                return new CommandExecutionResult<CoordinateProfileVersionMutationResponse>(response, "CoordinateProfileVersion", version.Id);
            },
            cancellationToken);
    }

    public Task<CoordinateProfileVersionMutationResponse> ActivateAsync(
        string versionId,
        ActivateCoordinateProfileVersionRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "coordinate_profile.version.activate",
            new { versionId, request },
            actor,
            async () =>
            {
                RequireEngineer(actor);
                var reason = RequireValue(request.Reason, "reason");
                var version = await LoadVersionWithPointsAsync(versionId, cancellationToken);
                if (version.Status != CoordinateProfileVersionStatus.Published)
                {
                    throw new BusinessRuleException("coordinate_version_not_published", "Only Published coordinate versions can be activated.", StatusCodes.Status409Conflict);
                }

                ValidateRequiredTargetPoints(version.TargetPoints);
                var profile = version.CoordinateProfile!;
                var activeVersionIds = await dbContext.CoordinateProfileVersions
                    .AsNoTracking()
                    .Where(x => x.CoordinateProfileId == profile.Id && x.IsActive && x.Id != version.Id)
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                var now = DateTimeOffset.UtcNow;
                if (activeVersionIds.Count > 0)
                {
                    await dbContext.CoordinateProfileVersions
                        .Where(x => activeVersionIds.Contains(x.Id))
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(x => x.IsActive, false)
                                .SetProperty(x => x.Status, CoordinateProfileVersionStatus.Retired)
                                .SetProperty(x => x.RetiredAtUtc, x => x.RetiredAtUtc ?? now),
                            cancellationToken);
                }

                version.IsActive = true;
                version.Status = CoordinateProfileVersionStatus.Active;
                version.ActivatedByUserId = actor.UserId;
                version.ActivatedAtUtc = now;
                profile.IsActive = true;
                profile.Status = CoordinateProfileStatus.Enabled;
                profile.ActiveVersionId = version.Id;
                AddAudit(actor, "coordinate_profile.version.activate", "CoordinateProfileVersion", version.Id, new
                {
                    profile.Code,
                    version.VersionLabel,
                    version.UsageScope,
                    version.VerificationStatus,
                    retiredVersionIds = activeVersionIds,
                    reason
                });

                var response = ToMutationResponse(request.CommandId, version, "Coordinate profile version activated.");
                return new CommandExecutionResult<CoordinateProfileVersionMutationResponse>(response, "CoordinateProfileVersion", version.Id);
            },
            cancellationToken);
    }

    public Task<EngineeringWriteResponse> CalibratePointAsNewVersionAsync(
        CalibrateCoordinatePointRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "engineering.coordinate.calibrate",
            request,
            actor,
            async () =>
            {
                RequireEngineer(actor);
                var reason = RequireValue(request.Reason, "reason");
                var profileCode = RequireValue(request.ProfileCode, "profileCode");
                var pointCode = NormalizeCode(request.PointCode);
                var sourceVersion = await LoadActiveVersionAsync(cancellationToken);
                if (!string.Equals(sourceVersion.CoordinateProfile?.Code, profileCode, StringComparison.Ordinal))
                {
                    throw new BusinessRuleException("coordinate_profile_not_active", "Requested coordinate profile is not the active profile.", StatusCodes.Status409Conflict);
                }

                var sourcePoint = sourceVersion.TargetPoints.SingleOrDefault(x => x.PointCode == pointCode);
                if (sourcePoint is null)
                {
                    throw new BusinessRuleException("coordinate_point_not_found", "Coordinate point was not found.", StatusCodes.Status404NotFound);
                }

                var profile = sourceVersion.CoordinateProfile!;
                var versionNo = await dbContext.CoordinateProfileVersions
                    .Where(x => x.CoordinateProfileId == profile.Id)
                    .MaxAsync(x => (int?)x.VersionNo, cancellationToken) + 1 ?? 1;
                var version = new CoordinateProfileVersion
                {
                    CoordinateProfile = profile,
                    VersionNo = versionNo,
                    VersionLabel = $"cal-{versionNo}",
                    Status = CoordinateProfileVersionStatus.Draft,
                    UsageScope = CoordinateVersionUsageScope.MockOnly,
                    VerificationStatus = CoordinateVersionVerificationStatus.Unverified,
                    SourceVersionId = sourceVersion.Id,
                    ChangeReason = reason,
                    ValidationResultJson = JsonSerializer.Serialize(new { status = "PointCalibrated", pointCode }, JsonOptions),
                    CreatedByUserId = actor.UserId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                dbContext.CoordinateProfileVersions.Add(version);

                CoordinatePoint? calibratedPoint = null;
                foreach (var source in sourceVersion.TargetPoints.OrderBy(x => x.PointCode))
                {
                    var target = CopyPoint(source, version);
                    if (source.PointCode == pointCode)
                    {
                        target.CalibratedXUm = request.CalibratedXUm;
                        target.CalibratedYUm = request.CalibratedYUm;
                        target.CalibratedZUm = request.CalibratedZUm;
                        target.SafeZUm = request.SafeZUm;
                        target.LiquidDetectZUm = request.AspirateZUm;
                        target.DispenseZUm = request.DispenseZUm;
                        target.RequiresCalibration = false;
                        target.ValidationStatus = CoordinateTargetPointValidationStatus.Validated;
                        target.ValidationMessage = reason;
                        calibratedPoint = target;
                    }

                    version.TargetPoints.Add(target);
                }

                if (calibratedPoint is null)
                {
                    throw new BusinessRuleException("coordinate_point_not_found", "Coordinate point was not found.", StatusCodes.Status404NotFound);
                }

                var diff = BuildDiff(sourcePoint, calibratedPoint);
                version.ChangeSummaryJson = JsonSerializer.Serialize(new { reason, changes = new[] { diff } }, JsonOptions);
                dbContext.CoordinateCalibrationHistory.Add(new CoordinateCalibrationHistory
                {
                    CoordinatePoint = calibratedPoint,
                    CoordinateProfileVersion = version,
                    SourceCoordinateProfileVersionId = sourceVersion.Id,
                    PreviousXUm = sourcePoint.CalibratedXUm,
                    PreviousYUm = sourcePoint.CalibratedYUm,
                    NewXUm = request.CalibratedXUm,
                    NewYUm = request.CalibratedYUm,
                    NewZUm = request.CalibratedZUm,
                    SafeZUm = request.SafeZUm,
                    LiquidDetectZUm = request.AspirateZUm,
                    DispenseZUm = request.DispenseZUm,
                    ChangeSummaryJson = version.ChangeSummaryJson,
                    ValidationResultJson = version.ValidationResultJson,
                    Reason = reason,
                    CalibratedByUserId = actor.UserId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });

                AddAudit(actor, "engineering.coordinate.calibrate", "CoordinateProfileVersion", version.Id, new
                {
                    profile.Code,
                    sourceVersionId = sourceVersion.Id,
                    pointCode,
                    diff,
                    reason
                });

                return new CommandExecutionResult<EngineeringWriteResponse>(
                    new EngineeringWriteResponse(true, request.CommandId, false, calibratedPoint.Id, "Coordinate point calibrated as a new coordinate version draft."),
                    "CoordinatePoint",
                    calibratedPoint.Id);
            },
            cancellationToken);
    }

    private async Task<CoordinateProfileVersion> LoadActiveVersionAsync(CancellationToken cancellationToken)
    {
        var versions = await dbContext.CoordinateProfileVersions
            .Include(x => x.CoordinateProfile)
            .Include(x => x.TargetPoints)
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);
        var version = versions.SingleOrDefault();
        if (version is null || version.Status != CoordinateProfileVersionStatus.Active || version.CoordinateProfile?.Status == CoordinateProfileStatus.NeedsManualResolution)
        {
            throw new BusinessRuleException("active_coordinate_version_required", "A published and active coordinate profile version is required.", StatusCodes.Status409Conflict);
        }

        return version;
    }

    private async Task<CoordinateProfileVersion> LoadSourceVersionAsync(string profileId, string? sourceVersionId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sourceVersionId))
        {
            return await dbContext.CoordinateProfileVersions
                .Include(x => x.TargetPoints)
                .SingleOrDefaultAsync(x => x.Id == sourceVersionId.Trim() && x.CoordinateProfileId == profileId, cancellationToken)
                ?? throw new BusinessRuleException("source_coordinate_version_not_found", "Source coordinate version was not found.", StatusCodes.Status404NotFound);
        }

        return await dbContext.CoordinateProfileVersions
            .Include(x => x.TargetPoints)
            .Where(x => x.CoordinateProfileId == profileId && x.IsActive)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("active_coordinate_version_required", "A source active coordinate version is required.", StatusCodes.Status409Conflict);
    }

    private async Task<CoordinateProfileVersion> LoadVersionWithPointsAsync(string versionId, CancellationToken cancellationToken)
    {
        var normalized = RequireValue(versionId, "versionId");
        return await dbContext.CoordinateProfileVersions
            .Include(x => x.CoordinateProfile)
            .Include(x => x.TargetPoints)
            .SingleOrDefaultAsync(x => x.Id == normalized, cancellationToken)
            ?? throw new BusinessRuleException("coordinate_version_not_found", "Coordinate profile version was not found.", StatusCodes.Status404NotFound);
    }

    private static CoordinatePoint CopyPoint(CoordinatePoint source, CoordinateProfileVersion version)
    {
        return new CoordinatePoint
        {
            CoordinateProfileId = version.CoordinateProfileId,
            CoordinateProfileVersion = version,
            PointCode = source.PointCode,
            PointType = source.PointType,
            PresetXUm = source.PresetXUm,
            PresetYUm = source.PresetYUm,
            CalibratedXUm = source.CalibratedXUm,
            CalibratedYUm = source.CalibratedYUm,
            CalibratedZUm = source.CalibratedZUm,
            SafeZUm = source.SafeZUm,
            LiquidDetectZUm = source.LiquidDetectZUm,
            DispenseZUm = source.DispenseZUm,
            ActionOffsetXUm = source.ActionOffsetXUm,
            ActionOffsetYUm = source.ActionOffsetYUm,
            ActionOffsetZUm = source.ActionOffsetZUm,
            RequiresCalibration = source.RequiresCalibration,
            ValidationStatus = source.ValidationStatus,
            ValidationMessage = source.ValidationMessage,
            IsEnabled = source.IsEnabled,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static CoordinatePoint NewPoint(string profileId, CoordinateProfileVersion version, CoordinateTargetPointInput input)
    {
        var point = new CoordinatePoint
        {
            CoordinateProfileId = profileId,
            CoordinateProfileVersion = version,
            PointCode = NormalizeCode(input.PointCode),
            PointType = string.IsNullOrWhiteSpace(input.PointType) ? "LogicalTarget" : input.PointType.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        ApplyUpdate(point, input);
        return point;
    }

    private static void ApplyUpdate(CoordinatePoint point, CoordinateTargetPointInput input)
    {
        point.PointType = string.IsNullOrWhiteSpace(input.PointType) ? point.PointType : input.PointType.Trim();
        point.CalibratedXUm = input.XUm;
        point.CalibratedYUm = input.YUm;
        point.CalibratedZUm = input.ZUm;
        point.SafeZUm = input.SafeZUm;
        point.LiquidDetectZUm = input.LiquidDetectZUm;
        point.DispenseZUm = input.DispenseZUm;
        point.ActionOffsetXUm = input.ActionOffsetXUm;
        point.ActionOffsetYUm = input.ActionOffsetYUm;
        point.ActionOffsetZUm = input.ActionOffsetZUm;
        point.RequiresCalibration = input.XUm is null || input.YUm is null;
        point.ValidationStatus = point.RequiresCalibration
            ? CoordinateTargetPointValidationStatus.NeedsCalibration
            : CoordinateTargetPointValidationStatus.Validated;
        point.ValidationMessage = point.RequiresCalibration ? "Coordinate point needs calibration." : "Coordinate point supplied by coordinate version request.";
        point.IsEnabled = input.IsEnabled ?? true;
        point.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static object BuildDiff(CoordinatePoint before, CoordinatePoint after)
    {
        return new
        {
            pointCode = before.PointCode,
            before = new
            {
                before.CalibratedXUm,
                before.CalibratedYUm,
                before.CalibratedZUm,
                before.SafeZUm,
                before.LiquidDetectZUm,
                before.DispenseZUm,
                before.ActionOffsetXUm,
                before.ActionOffsetYUm,
                before.ActionOffsetZUm,
                before.ValidationStatus
            },
            after = new
            {
                after.CalibratedXUm,
                after.CalibratedYUm,
                after.CalibratedZUm,
                after.SafeZUm,
                after.LiquidDetectZUm,
                after.DispenseZUm,
                after.ActionOffsetXUm,
                after.ActionOffsetYUm,
                after.ActionOffsetZUm,
                after.ValidationStatus
            }
        };
    }

    private static void ValidateRequiredTargetPoints(IEnumerable<CoordinatePoint> points)
    {
        var codes = points
            .Where(x => x.IsEnabled)
            .Select(x => x.PointCode)
            .ToHashSet(StringComparer.Ordinal);
        var missing = RequiredTargetPointCodes.Where(x => !codes.Contains(x)).ToArray();
        if (missing.Length > 0)
        {
            throw new BusinessRuleException("coordinate_target_points_incomplete", $"Coordinate version is missing required target points: {string.Join(", ", missing)}.", StatusCodes.Status409Conflict);
        }
    }

    private static CoordinateRealReadinessResult EvaluateRealReadiness(
        CoordinateProfileVersion version,
        IEnumerable<CoordinatePoint> points)
    {
        var label = string.IsNullOrWhiteSpace(version.VersionLabel) ? version.Id : version.VersionLabel;
        var blocking = new List<string>();
        var pointByCode = points
            .Where(x => x.IsEnabled)
            .GroupBy(x => x.PointCode, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        foreach (var requiredCode in RequiredTargetPointCodes)
        {
            if (!pointByCode.TryGetValue(requiredCode, out var point))
            {
                blocking.Add($"{label}: {requiredCode} 缺少坐标点，禁止 Real 运动");
                continue;
            }

            AddMissingPointReadiness(blocking, label, point);
        }

        AddMissingVersionReadiness(blocking, label, version.ValidationResultJson);
        return new CoordinateRealReadinessResult(blocking.Count == 0, blocking);
    }

    private static void AddMissingPointReadiness(List<string> blocking, string versionLabel, CoordinatePoint point)
    {
        if (point.CalibratedXUm is null || point.CalibratedYUm is null)
        {
            blocking.Add($"{versionLabel}: {point.PointCode} 缺少 XY 坐标，禁止 Real 运动");
        }

        if (point.CalibratedZUm is null)
        {
            blocking.Add($"{versionLabel}: {point.PointCode} 缺少 Z 高度，禁止 Real 运动");
        }

        if (point.SafeZUm is null)
        {
            blocking.Add($"{versionLabel}: {point.PointCode} 缺少安全高度，禁止 Real 运动");
        }

        if (point.LiquidDetectZUm is null)
        {
            blocking.Add($"{versionLabel}: {point.PointCode} 缺少探液/吸液高度，禁止 Real 运动");
        }

        if (point.DispenseZUm is null)
        {
            blocking.Add($"{versionLabel}: {point.PointCode} 缺少分液高度，禁止 Real 运动");
        }

        if (point.ActionOffsetXUm is null || point.ActionOffsetYUm is null || point.ActionOffsetZUm is null)
        {
            blocking.Add($"{versionLabel}: {point.PointCode} 缺少动作偏移，禁止 Real 运动");
        }

        if (point.RequiresCalibration || point.ValidationStatus != CoordinateTargetPointValidationStatus.Validated)
        {
            blocking.Add($"{versionLabel}: {point.PointCode} 校准验证未通过，禁止 Real 运动");
        }
    }

    private static void AddMissingVersionReadiness(List<string> blocking, string versionLabel, string validationResultJson)
    {
        var requiredFlags = new (string Name, string Message)[]
        {
            ("xyImported", "XY 未导入"),
            ("requiredHeightsComplete", "必需高度未完整配置"),
            ("calibrationVerified", "校准验证未通过"),
            ("safetyParametersComplete", "安全参数未完整配置"),
            ("speedLimitsConfigured", "速度限制未配置"),
            ("accelerationLimitsConfigured", "加速度限制未配置"),
            ("softLimitsConfigured", "软限位未配置")
        };

        foreach (var (name, message) in requiredFlags)
        {
            if (!ReadBooleanFlag(validationResultJson, name))
            {
                blocking.Add($"{versionLabel}: {message}，禁止 Real 运动");
            }
        }
    }

    private static bool ReadBooleanFlag(string validationResultJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(validationResultJson))
        {
            return false;
        }

        try
        {
            using var json = JsonDocument.Parse(validationResultJson);
            if (!json.RootElement.TryGetProperty(propertyName, out var value))
            {
                return false;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> BuildRequiredTargetPointCodes()
    {
        var codes = new List<string>();
        codes.AddRange(Enumerable.Range(1, 40).Select(x => $"R{x}"));
        foreach (var drawer in new[] { "A", "B", "C", "D" })
        {
            codes.AddRange(Enumerable.Range(1, 4).Select(x => $"{drawer}-{x:00}"));
        }

        codes.Add("SampleScan");
        codes.AddRange(["WashInnerLeft", "WashInnerRight", "WashOuterLeft", "WashOuterRight"]);
        codes.AddRange(Enumerable.Range(1, 8).Select(x => $"M{x}"));
        codes.AddRange(["DabA", "DabB"]);
        return codes;
    }

    private sealed record CoordinateRealReadinessResult(
        bool Ok,
        IReadOnlyList<string> BlockingReasons);

    private static CoordinateProfileVersionMutationResponse ToMutationResponse(string commandId, CoordinateProfileVersion version, string message)
    {
        return new CoordinateProfileVersionMutationResponse(
            true,
            commandId,
            false,
            version.CoordinateProfileId,
            version.Id,
            version.VersionNo,
            version.VersionLabel,
            version.Status,
            version.IsActive,
            version.UsageScope,
            version.VerificationStatus,
            message);
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

    private static void RequireEngineer(AuthenticatedUser actor)
    {
        if (!actor.HasRole("engineer") && !actor.HasRole("admin"))
        {
            throw new BusinessRuleException("coordinate_permission_denied", "Coordinate changes require engineer or admin role.", StatusCodes.Status403Forbidden);
        }
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

    private static string NormalizeCode(string? value)
    {
        return RequireValue(value, "pointCode");
    }

    private static string NormalizeJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "{}";
        }

        using var _ = JsonDocument.Parse(value);
        return value.Trim();
    }
}
