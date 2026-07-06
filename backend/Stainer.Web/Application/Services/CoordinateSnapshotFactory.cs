using System.Text.Json;
using Stainer.Web.Domain.Entities;

namespace Stainer.Web.Application.Services;

internal static class CoordinateSnapshotFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(CoordinateProfileVersion version)
    {
        var profile = version.CoordinateProfile;
        var snapshot = new
        {
            coordinateProfileId = version.CoordinateProfileId,
            coordinateProfileCode = profile?.Code,
            coordinateProfileName = profile?.Name,
            coordinateProfileVersionId = version.Id,
            version.VersionNo,
            version.VersionLabel,
            version.Status,
            version.IsActive,
            version.UsageScope,
            version.VerificationStatus,
            needle1OriginUm = new { x = 0, y = 0 },
            needle2NominalOffsetUm = new { x = 0, y = 25000 },
            targetPointCount = version.TargetPoints.Count,
            targetPoints = version.TargetPoints
                .OrderBy(x => x.PointType)
                .ThenBy(x => x.PointCode)
                .Select(x => new
                {
                    x.PointCode,
                    x.PointType,
                    x.CalibratedXUm,
                    x.CalibratedYUm,
                    x.CalibratedZUm,
                    x.SafeZUm,
                    liquidDetectZUm = x.LiquidDetectZUm,
                    x.DispenseZUm,
                    x.ActionOffsetXUm,
                    x.ActionOffsetYUm,
                    x.ActionOffsetZUm,
                    x.ValidationStatus,
                    x.RequiresCalibration,
                    x.IsEnabled
                })
                .ToArray()
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }
}
