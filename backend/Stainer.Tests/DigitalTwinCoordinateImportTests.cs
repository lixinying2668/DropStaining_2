using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class DigitalTwinCoordinateImportTests
{
    [Fact]
    public async Task Preview_parses_full_csv_records_hash_and_confirmed_mappings()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");

        var preview = await PostAsync<DigitalTwinCoordinateImportResponse>(
            client,
            "/api/engineering/coordinates/digital-twin/import/preview",
            new PreviewDigitalTwinCoordinateImportRequest(BaselineCsvPath()));

        Assert.True(preview.Ok, string.Join("; ", preview.Errors));
        Assert.True(preview.DryRun);
        Assert.Equal(90, preview.TotalRows);
        Assert.Equal(70, preview.ExecutableTargetCount);
        Assert.Equal(20, preview.ReferenceOnlyCount);
        Assert.Equal(0, preview.RejectedCount);
        Assert.NotEmpty(preview.SourceFileHashSha256);
        Assert.Empty(preview.Errors);
        Assert.All(preview.Rows, row => Assert.Contains(row.Disposition, new[] { "ExecutableTarget", "ReferenceOnly" }));

        AssertRow(preview, "S11", "R1", "ExecutableTarget", 316_250, 16_500);
        AssertRow(preview, "S18", "R8", "ExecutableTarget", 316_250, 191_500);
        AssertRow(preview, "S21", "R9", "ExecutableTarget", 291_250, 16_500);
        AssertRow(preview, "S58", "R40", "ExecutableTarget", 216_250, 191_500);

        AssertRow(preview, "R11", "A-04", "ExecutableTarget", 135_250, 116_500);
        AssertRow(preview, "R14", "A-01", "ExecutableTarget", 135_250, 191_500);
        AssertRow(preview, "R44", "D-01", "ExecutableTarget", -14_750, 191_500);

        AssertRow(preview, "P11", "M1", "ExecutableTarget", 184_750, 66_500);
        AssertRow(preview, "P42", "M8", "ExecutableTarget", 170_750, 141_500);
        AssertRow(preview, "DAB-A", "DabA", "ExecutableTarget", 177_750, 166_500);
        AssertRow(preview, "DAB-B", "DabB", "ExecutableTarget", 177_750, 191_500);

        AssertRow(preview, "Needle1Wash", "WashOuterLeft", "ExecutableTarget", 184_750, 17_000);
        AssertRow(preview, "Needle2Wash", "WashInnerRight", "ExecutableTarget", 177_750, 42_000);
        AssertRow(preview, "Needle1", "Needle1", "ReferenceOnly", 0, 0);
        AssertRow(preview, "Needle2", "Needle2", "ReferenceOnly", 0, 25_000);

        Assert.Contains(preview.Rows, x => x.CsvLogicalLabel == "Mixer1" && x.TargetPointCode == "Mixer1" && x.Disposition == "ReferenceOnly");
        Assert.Contains(preview.Rows, x => x.CsvLogicalLabel == "Mixer4" && x.TargetPointCode == "Mixer4" && x.Disposition == "ReferenceOnly");
        Assert.Contains(preview.Rows, x => x.TargetPointCode == "ReagentScannerCamera" && x.Disposition == "ReferenceOnly");
    }

    [Fact]
    public async Task Apply_creates_draft_version_once_and_preserves_existing_version_and_run_snapshot()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "digital-twin-apply");

        string oldVersionId;
        string runId;
        const string snapshot = "{\"coordinateProfileVersionId\":\"legacy\",\"marker\":\"before-digital-twin\"}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
            oldVersionId = (await dbContext.CoordinateProfileVersions.SingleAsync(x => x.IsActive)).Id;
            var run = new MachineRun
            {
                RunCode = $"RUN-DT-SNAPSHOT-{Guid.NewGuid():N}"[..30],
                Status = RuntimeLedgerStatus.Created,
                CoordinateProfileVersionId = oldVersionId,
                CoordinateSnapshotJson = snapshot,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.MachineRuns.Add(run);
            await dbContext.SaveChangesAsync();
            runId = run.Id;
        }

        var applied = await PostAsync<DigitalTwinCoordinateImportResponse>(
            client,
            "/api/engineering/coordinates/digital-twin/import",
            new ApplyDigitalTwinCoordinateImportRequest(
                "cmd-digital-twin-apply-1",
                BaselineCsvPath(),
                "import digital twin xy baseline"));
        Assert.True(applied.Ok, string.Join("; ", applied.Errors));
        Assert.False(applied.DryRun);
        Assert.False(applied.ExistingVersionReused);
        Assert.NotNull(applied.CoordinateProfileVersionId);

        var repeated = await PostAsync<DigitalTwinCoordinateImportResponse>(
            client,
            "/api/engineering/coordinates/digital-twin/import",
            new ApplyDigitalTwinCoordinateImportRequest(
                "cmd-digital-twin-apply-2",
                BaselineCsvPath(),
                "repeat same digital twin xy baseline"));
        Assert.True(repeated.Ok, string.Join("; ", repeated.Errors));
        Assert.True(repeated.ExistingVersionReused);
        Assert.Equal(applied.CoordinateProfileVersionId, repeated.CoordinateProfileVersionId);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<StainerDbContext>();
        Assert.Equal(1, await verify.CoordinateProfileVersions.CountAsync(x => x.VersionLabel == "DigitalTwinXY-v1"));
        Assert.Equal(90, await verify.CoordinatePoints.CountAsync(x => x.CoordinateProfileVersionId == applied.CoordinateProfileVersionId));

        var version = await verify.CoordinateProfileVersions.SingleAsync(x => x.Id == applied.CoordinateProfileVersionId);
        Assert.Equal(CoordinateProfileVersionStatus.Draft, version.Status);
        Assert.Equal(CoordinateVersionUsageScope.MockOnly, version.UsageScope);
        Assert.Equal(CoordinateVersionVerificationStatus.Unverified, version.VerificationStatus);
        Assert.Null(version.PublishedAtUtc);
        Assert.Equal(oldVersionId, (await verify.CoordinateProfiles.SingleAsync(x => x.Code == ReferenceDataSeeder.DefaultCoordinateProfileCode)).ActiveVersionId);
        Assert.Equal(snapshot, (await verify.MachineRuns.SingleAsync(x => x.Id == runId)).CoordinateSnapshotJson);

        using var changeSummary = JsonDocument.Parse(version.ChangeSummaryJson);
        Assert.Equal("DigitalTwinCoordinateImport", changeSummary.RootElement.GetProperty("importKind").GetString());
        Assert.Equal(applied.SourceFileHashSha256, changeSummary.RootElement.GetProperty("source").GetProperty("sha256").GetString());
        Assert.Equal(90, changeSummary.RootElement.GetProperty("rowDispositions").GetArrayLength());
        Assert.True(await verify.AuditLogs.AnyAsync(x => x.Action == "coordinate.digital_twin_import.apply" && x.EntityId == version.Id));
    }

    [Fact]
    public async Task Operator_cannot_apply_digital_twin_import()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "operator", "operator");

        var response = await client.PostAsJsonAsync(
            "/api/engineering/coordinates/digital-twin/import",
            new ApplyDigitalTwinCoordinateImportRequest(
                "cmd-digital-twin-operator-denied",
                BaselineCsvPath(),
                "operator should not edit coordinates"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Real_gate_rejects_imported_version_until_heights_safety_and_validation_are_complete()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "digital-twin-real-gate");

        var applied = await PostAsync<DigitalTwinCoordinateImportResponse>(
            client,
            "/api/engineering/coordinates/digital-twin/import",
            new ApplyDigitalTwinCoordinateImportRequest(
                "cmd-digital-twin-real-gate",
                BaselineCsvPath(),
                "import for real gate test"));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var version = await dbContext.CoordinateProfileVersions.SingleAsync(x => x.Id == applied.CoordinateProfileVersionId);
        version.Status = CoordinateProfileVersionStatus.Published;
        version.UsageScope = CoordinateVersionUsageScope.RealEligible;
        version.VerificationStatus = CoordinateVersionVerificationStatus.EngineerVerified;
        version.PublishedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        var run = await AddRunForVersionAsync(dbContext, version.Id, "RUN-DT-REAL-BLOCK");
        var service = scope.ServiceProvider.GetRequiredService<CoordinateProfileLifecycleService>();
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.EnsureRunCoordinateUsableAsync(run.Id, DeviceModes.Real));
        Assert.Equal("coordinate_version_not_real_ready", exception.Code);
        Assert.Contains("禁止 Real 运动", exception.Message);
        Assert.Contains("缺少", exception.Message);

        var readyImport = await PostAsync<DigitalTwinCoordinateImportResponse>(
            client,
            "/api/engineering/coordinates/digital-twin/import",
            new ApplyDigitalTwinCoordinateImportRequest(
                "cmd-digital-twin-real-ready",
                BaselineCsvPath(),
                "import for complete real readiness test",
                VersionLabel: "DigitalTwinXY-ready"));
        var readyVersion = await dbContext.CoordinateProfileVersions.SingleAsync(x => x.Id == readyImport.CoordinateProfileVersionId);
        CompleteRequiredRealReadiness(dbContext, readyVersion.Id);
        readyVersion.Status = CoordinateProfileVersionStatus.Published;
        readyVersion.UsageScope = CoordinateVersionUsageScope.RealEligible;
        readyVersion.VerificationStatus = CoordinateVersionVerificationStatus.EngineerVerified;
        readyVersion.PublishedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        var readyRun = await AddRunForVersionAsync(dbContext, readyVersion.Id, "RUN-DT-REAL-PASS");
        await service.EnsureRunCoordinateUsableAsync(readyRun.Id, DeviceModes.Real);
    }

    private static void AssertRow(
        DigitalTwinCoordinateImportResponse response,
        string logicalLabel,
        string targetPointCode,
        string disposition,
        long xUm,
        long yUm)
    {
        Assert.Contains(response.Rows, x =>
            x.CsvLogicalLabel == logicalLabel
            && x.TargetPointCode == targetPointCode
            && x.Disposition == disposition
            && x.MachineXUm == xUm
            && x.MachineYUm == yUm);
    }

    private static async Task<MachineRun> AddRunForVersionAsync(StainerDbContext dbContext, string versionId, string prefix)
    {
        var run = new MachineRun
        {
            RunCode = $"{prefix}-{Guid.NewGuid():N}"[..30],
            Status = RuntimeLedgerStatus.Created,
            CoordinateProfileVersionId = versionId,
            CoordinateSnapshotJson = $$"""{"coordinateProfileVersionId":"{{versionId}}","targetPoints":[]}""",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.MachineRuns.Add(run);
        await dbContext.SaveChangesAsync();
        return run;
    }

    private static void CompleteRequiredRealReadiness(StainerDbContext dbContext, string versionId)
    {
        var version = dbContext.CoordinateProfileVersions.Single(x => x.Id == versionId);
        var points = dbContext.CoordinatePoints.Where(x => x.CoordinateProfileVersionId == versionId).ToList();
        var profileId = version.CoordinateProfileId;
        var scanner = points.SingleOrDefault(x => x.PointCode == "ReagentScannerCamera");
        if (points.All(x => x.PointCode != "SampleScan"))
        {
            points.Add(new CoordinatePoint
            {
                CoordinateProfileId = profileId,
                CoordinateProfileVersionId = versionId,
                PointCode = "SampleScan",
                PointType = "SampleScanPosition",
                PresetXUm = scanner?.PresetXUm ?? 342_000,
                PresetYUm = scanner?.PresetYUm ?? 211_000,
                CalibratedXUm = scanner?.CalibratedXUm ?? 342_000,
                CalibratedYUm = scanner?.CalibratedYUm ?? 211_000,
                IsEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.CoordinatePoints.Add(points[^1]);
        }

        foreach (var point in points.Where(x => CoordinateProfileLifecycleService.RequiredTargetPointCodes.Contains(x.PointCode, StringComparer.Ordinal)))
        {
            point.IsEnabled = true;
            point.CalibratedXUm ??= point.PresetXUm ?? 1;
            point.CalibratedYUm ??= point.PresetYUm ?? 1;
            point.CalibratedZUm = 10_000;
            point.SafeZUm = 20_000;
            point.LiquidDetectZUm = 6_000;
            point.DispenseZUm = 5_000;
            point.ActionOffsetXUm = 0;
            point.ActionOffsetYUm = 0;
            point.ActionOffsetZUm = 0;
            point.RequiresCalibration = false;
            point.ValidationStatus = CoordinateTargetPointValidationStatus.Validated;
            point.ValidationMessage = "Engineering test completed real readiness.";
        }

        version.ValidationResultJson = JsonSerializer.Serialize(new
        {
            xyImported = true,
            requiredHeightsComplete = true,
            calibrationVerified = true,
            safetyParametersComplete = true,
            speedLimitsConfigured = true,
            accelerationLimitsConfigured = true,
            softLimitsConfigured = true
        });
    }

    private static string BaselineCsvPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "coordinate-baseline", "position_coordinates_with_slide_rectangles_numbered.csv");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Digital twin coordinate baseline CSV was not found.");
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-digital-twin-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={databasePath}");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}"
                    });
                });
            });
    }

    private static async Task LoginAsync(HttpClient client, string username, string role)
    {
        var response = await client.PostAsJsonAsync("/api/login", new
        {
            username,
            password = "123456",
            role
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task OpenEngineeringSessionAsync(HttpClient client, string suffix)
    {
        var response = await client.PostAsJsonAsync("/api/engineering/session", new
        {
            commandId = $"cmd-engineering-session-{suffix}",
            password = "123456",
            reason = $"digital twin test {suffix}",
            target = "coordinate-digital-twin"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
    }
}
