using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class CoordinateProfileVersioningTests
{
    [Fact]
    public async Task Coordinate_versions_publish_activate_and_keep_history_immutable()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "coordinate-versioning-main");

        var profiles = await client.GetFromJsonAsync<List<CoordinateProfileResponse>>("/api/engineering/coordinate-profiles");
        var profile = Assert.Single(profiles!, x => x.Code == ReferenceDataSeeder.DefaultCoordinateProfileCode);
        var originalVersionId = profile.ActiveVersionId;
        Assert.NotNull(originalVersionId);
        var originalVersion = profile.Versions.Single(x => x.Id == originalVersionId);
        Assert.Equal(CoordinateProfileVersionStatus.Active, originalVersion.Status);
        Assert.Equal(CoordinateVersionUsageScope.MockOnly, originalVersion.UsageScope);
        Assert.Equal(CoordinateVersionVerificationStatus.Unverified, originalVersion.VerificationStatus);
        Assert.Contains(profile.Points, x => x.PointCode == "SampleScan");
        Assert.Contains(profile.Points, x => x.PointCode == "DabA");
        Assert.Contains(profile.Points, x => x.PointCode == "DabB");

        var created = await PostJsonAsync<CoordinateProfileVersionMutationResponse>(client, "/api/engineering/coordinate-profile-versions", new
        {
            commandId = "cmd-coordinate-version-create-001",
            profileCode = profile.Code,
            sourceVersionId = originalVersionId,
            versionLabel = "2",
            reason = "integration coordinate adjustment",
            validationResultJson = "{\"status\":\"DraftValidated\"}",
            targetPoints = new[]
            {
                new
                {
                    pointCode = "R1",
                    pointType = "ReagentRackPosition",
                    xUm = 111L,
                    yUm = 222L,
                    zUm = 333L,
                    safeZUm = 1000L,
                    liquidDetectZUm = 900L,
                    dispenseZUm = 800L,
                    actionOffsetXUm = 1L,
                    actionOffsetYUm = 2L,
                    actionOffsetZUm = 3L,
                    isEnabled = true
                }
            }
        });
        Assert.Equal(CoordinateProfileVersionStatus.Draft, created.Status);

        var published = await PostJsonAsync<CoordinateProfileVersionMutationResponse>(client, $"/api/engineering/coordinate-profile-versions/{created.CoordinateProfileVersionId}/publish", new
        {
            commandId = "cmd-coordinate-version-publish-001",
            reason = "validated on engineering fixture",
            validationResultJson = "{\"status\":\"Passed\",\"fixture\":\"integration\"}"
        });
        Assert.Equal(CoordinateProfileVersionStatus.Published, published.Status);
        Assert.Equal(CoordinateVersionUsageScope.RealEligible, published.UsageScope);
        Assert.Equal(CoordinateVersionVerificationStatus.EngineerVerified, published.VerificationStatus);

        var activated = await PostJsonAsync<CoordinateProfileVersionMutationResponse>(client, $"/api/engineering/coordinate-profile-versions/{created.CoordinateProfileVersionId}/activate", new
        {
            commandId = "cmd-coordinate-version-activate-001",
            reason = "activate for subsequent batches"
        });
        Assert.Equal(CoordinateProfileVersionStatus.Active, activated.Status);
        Assert.True(activated.IsActive);
        Assert.Equal(CoordinateVersionUsageScope.RealEligible, activated.UsageScope);
        Assert.Equal(CoordinateVersionVerificationStatus.EngineerVerified, activated.VerificationStatus);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var oldR1 = await dbContext.CoordinatePoints.AsNoTracking().SingleAsync(x => x.CoordinateProfileVersionId == originalVersionId && x.PointCode == "R1");
        var newR1 = await dbContext.CoordinatePoints.SingleAsync(x => x.CoordinateProfileVersionId == created.CoordinateProfileVersionId && x.PointCode == "R1");
        Assert.Null(oldR1.CalibratedXUm);
        Assert.Equal(111L, newR1.CalibratedXUm);
        Assert.Equal(222L, newR1.CalibratedYUm);
        Assert.Equal(333L, newR1.CalibratedZUm);
        Assert.True(await dbContext.AuditLogs.AnyAsync(x => x.Action == "coordinate_profile.version.activate" && x.EntityId == created.CoordinateProfileVersionId));

        newR1.CalibratedXUm = 999L;
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_allows_multiple_inactive_versions_but_rejects_second_active_for_same_profile()
    {
        await using var factory = CreateFactory();
        _ = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var profile = await dbContext.CoordinateProfiles.Include(x => x.Versions).SingleAsync(x => x.Code == ReferenceDataSeeder.DefaultCoordinateProfileCode);
        var nextVersionNo = profile.Versions.Max(x => x.VersionNo) + 1;
        dbContext.CoordinateProfileVersions.AddRange(
            NewVersion(profile.Id, nextVersionNo, "inactive-draft", CoordinateProfileVersionStatus.Draft, false),
            NewVersion(profile.Id, nextVersionNo + 1, "inactive-retired", CoordinateProfileVersionStatus.Retired, false),
            NewVersion(profile.Id, nextVersionNo + 2, "inactive-manual", CoordinateProfileVersionStatus.NeedsManualResolution, false));
        await dbContext.SaveChangesAsync();

        dbContext.CoordinateProfileVersions.Add(
            NewVersion(profile.Id, nextVersionNo + 3, "duplicate-active", CoordinateProfileVersionStatus.Active, true));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        Assert.Equal(1, await dbContext.CoordinateProfileVersions.CountAsync(x => x.CoordinateProfileId == profile.Id && x.IsActive));
        Assert.Equal(3, await dbContext.CoordinateProfileVersions.CountAsync(x => x.CoordinateProfileId == profile.Id && !x.IsActive && x.VersionNo >= nextVersionNo));
    }

    [Fact]
    public async Task Mock_allows_unverified_default_coordinates_but_real_mode_rejects_them()
    {
        await using var factory = CreateFactory();
        _ = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var version = await dbContext.CoordinateProfileVersions.SingleAsync(x => x.IsActive);
        Assert.Equal(CoordinateVersionUsageScope.MockOnly, version.UsageScope);
        Assert.Equal(CoordinateVersionVerificationStatus.Unverified, version.VerificationStatus);

        var run = new MachineRun
        {
            RunCode = $"RUN-COORD-GUARD-{Guid.NewGuid():N}"[..30],
            Status = RuntimeLedgerStatus.Created,
            CoordinateProfileVersionId = version.Id,
            CoordinateSnapshotJson = $$"""{"coordinateProfileVersionId":"{{version.Id}}"}""",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.MachineRuns.Add(run);
        await dbContext.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<CoordinateProfileLifecycleService>();
        await service.EnsureRunCoordinateUsableAsync(run.Id, DeviceModes.Mock);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.EnsureRunCoordinateUsableAsync(run.Id, DeviceModes.Real));
        Assert.Equal("coordinate_version_not_real_verified", exception.Code);
        Assert.Contains("engineer verification and publication", exception.Message);
    }

    [Fact]
    public async Task Channel_batches_freeze_coordinate_version_and_run_uses_batch_snapshot()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await LoginAsync(client, "admin", "admin");
        await OpenEngineeringSessionAsync(client, "coordinate-versioning-freeze");

        var profiles = await client.GetFromJsonAsync<List<CoordinateProfileResponse>>("/api/engineering/coordinate-profiles");
        var originalVersionId = Assert.Single(profiles!, x => x.Code == ReferenceDataSeeder.DefaultCoordinateProfileCode).ActiveVersionId!;

        _ = await PostJsonAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-coordinate-freeze-batch-a",
            drawerCode = "A"
        });
        var batchASelection = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-coordinate-freeze-select-a",
            drawerCode = "A",
            experimentType = StainingTaskType.He
        });

        var created = await PostJsonAsync<CoordinateProfileVersionMutationResponse>(client, "/api/engineering/coordinate-profile-versions", new
        {
            commandId = "cmd-coordinate-freeze-version-create",
            profileCode = ReferenceDataSeeder.DefaultCoordinateProfileCode,
            sourceVersionId = originalVersionId,
            versionLabel = "freeze-test",
            reason = "activate after first batch",
            validationResultJson = "{\"status\":\"DraftValidated\"}",
            targetPoints = new[]
            {
                new
                {
                    pointCode = "R2",
                    pointType = "ReagentRackPosition",
                    xUm = 444L,
                    yUm = 555L,
                    zUm = 666L,
                    safeZUm = 1000L,
                    liquidDetectZUm = 900L,
                    dispenseZUm = 800L,
                    actionOffsetXUm = (long?)null,
                    actionOffsetYUm = (long?)null,
                    actionOffsetZUm = (long?)null,
                    isEnabled = true
                }
            }
        });
        _ = await PostJsonAsync<CoordinateProfileVersionMutationResponse>(client, $"/api/engineering/coordinate-profile-versions/{created.CoordinateProfileVersionId}/publish", new
        {
            commandId = "cmd-coordinate-freeze-version-publish",
            reason = "publish freeze test",
            validationResultJson = "{\"status\":\"Passed\"}"
        });
        _ = await PostJsonAsync<CoordinateProfileVersionMutationResponse>(client, $"/api/engineering/coordinate-profile-versions/{created.CoordinateProfileVersionId}/activate", new
        {
            commandId = "cmd-coordinate-freeze-version-activate",
            reason = "activate freeze test"
        });

        _ = await PostJsonAsync<ChannelBatchActivationResponse>(client, "/api/channel-batches/active", new
        {
            commandId = "cmd-coordinate-freeze-batch-b",
            drawerCode = "B"
        });
        var batchBSelection = await PostJsonAsync<ChannelBatchWorkflowResponse>(client, "/api/channel-batches/experiment-type-selection", new
        {
            commandId = "cmd-coordinate-freeze-select-b",
            drawerCode = "B",
            experimentType = StainingTaskType.He
        });

        var task = await PostJsonAsync<TaskCreationResponse>(client, "/api/tasks/he", new
        {
            commandId = "cmd-coordinate-freeze-task-b",
            workflowVersionId = batchBSelection.WorkflowVersionId,
            drawerCode = "B",
            slotCode = "B-01"
        });
        Assert.False(task.RequiresSelection);
        Assert.NotNull(task.TaskId);

        var run = await PostJsonAsync<MachineRunResponse>(client, "/api/runs", new
        {
            commandId = "cmd-coordinate-freeze-run-create",
            stainingTaskIds = new[] { task.TaskId }
        });
        var runDetail = await client.GetFromJsonAsync<MachineRunDetailResponse>($"/api/runs/{run.RunId}");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
        var batchA = await dbContext.ChannelBatches.AsNoTracking().SingleAsync(x => x.Id == batchASelection.ChannelBatchId);
        var batchB = await dbContext.ChannelBatches.AsNoTracking().SingleAsync(x => x.Id == batchBSelection.ChannelBatchId);
        Assert.Equal(originalVersionId, batchA.CoordinateProfileVersionId);
        Assert.Equal(CoordinateSelectionStatus.Frozen, batchA.CoordinateSelectionStatus);
        Assert.Equal(created.CoordinateProfileVersionId, batchB.CoordinateProfileVersionId);
        Assert.Equal(CoordinateSelectionStatus.Frozen, batchB.CoordinateSelectionStatus);
        Assert.Equal(created.CoordinateProfileVersionId, runDetail!.CoordinateProfileVersionId);
        Assert.Contains(created.CoordinateProfileVersionId, runDetail.CoordinateSnapshotJson);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-coordinate-version-tests", Guid.NewGuid().ToString("N"), "stainer.db");
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

    private static CoordinateProfileVersion NewVersion(
        string profileId,
        int versionNo,
        string versionLabel,
        string status,
        bool isActive)
    {
        return new CoordinateProfileVersion
        {
            CoordinateProfileId = profileId,
            VersionNo = versionNo,
            VersionLabel = versionLabel,
            Status = status,
            IsActive = isActive,
            UsageScope = CoordinateVersionUsageScope.MockOnly,
            VerificationStatus = CoordinateVersionVerificationStatus.Unverified,
            ChangeReason = "coordinate uniqueness integration test",
            ChangeSummaryJson = "{}",
            ValidationResultJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
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
            reason = $"coordinate test {suffix}",
            target = "coordinate-profile"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
    }
}
