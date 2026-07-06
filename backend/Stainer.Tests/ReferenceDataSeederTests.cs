using Microsoft.EntityFrameworkCore;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Tests;

public sealed class ReferenceDataSeederTests
{
    [Fact]
    public async Task Seeder_creates_required_physical_layout_and_roles()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var seeder = new ReferenceDataSeeder(dbContext);

        await seeder.SeedAsync();

        Assert.Equal(3, await dbContext.Roles.CountAsync());
        Assert.True(await dbContext.Roles.AnyAsync(x => x.Code == "operator"));
        Assert.True(await dbContext.Roles.AnyAsync(x => x.Code == "engineer"));
        Assert.True(await dbContext.Roles.AnyAsync(x => x.Code == "admin"));

        Assert.Equal(4, await dbContext.Drawers.CountAsync());
        Assert.Equal(16, await dbContext.PhysicalSlots.CountAsync());
        Assert.Equal(40, await dbContext.ReagentRackPositions.CountAsync());
        Assert.Equal(8, await dbContext.DabMixPositions.CountAsync());
        Assert.Equal(4, await dbContext.WashPositions.CountAsync());
    }

    [Fact]
    public async Task Seeder_creates_correct_heatboard_and_heatpoint_mapping()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        await new ReferenceDataSeeder(dbContext).SeedAsync();

        var drawerMap = await dbContext.Drawers.ToDictionaryAsync(x => x.Code, x => x.HeatBoardId);
        Assert.Equal(0, drawerMap["A"]);
        Assert.Equal(1, drawerMap["B"]);
        Assert.Equal(2, drawerMap["C"]);
        Assert.Equal(3, drawerMap["D"]);

        var slots = await dbContext.PhysicalSlots.Include(x => x.Drawer).ToListAsync();
        foreach (var slot in slots)
        {
            Assert.Equal(slot.SlotNo - 1, slot.HeatPointId);
            Assert.Equal(slot.SlotNo, slot.VerticalOrderFromBottom);
            Assert.Matches("^[A-D]-0[1-4]$", slot.Code);
        }
    }

    [Fact]
    public async Task Seeder_creates_correct_reagent_position_and_channel_mapping()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        await new ReferenceDataSeeder(dbContext).SeedAsync();

        var positions = await dbContext.ReagentRackPositions.OrderBy(x => x.PositionNo).ToListAsync();
        Assert.Equal(40, positions.Count);

        foreach (var position in positions)
        {
            var expectedColumn = ((position.PositionNo - 1) / 8) + 1;
            var expectedRow = ((position.PositionNo - 1) % 8) + 1;
            Assert.Equal($"R{position.PositionNo}", position.Code);
            Assert.Equal(expectedColumn, position.ColumnNo);
            Assert.Equal(expectedRow, position.RowNo);
            Assert.Equal(expectedColumn, position.ScannerChannelNo);
            Assert.Equal($"ch{expectedColumn}", position.ScannerChannelCode);
        }
    }

    [Fact]
    public async Task Seeder_creates_needle_coordinates_and_unset_workpoint_coordinates()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        await new ReferenceDataSeeder(dbContext).SeedAsync();

        var profile = await dbContext.CoordinateProfiles
            .Include(x => x.CoordinatePoints)
            .SingleAsync(x => x.Code == ReferenceDataSeeder.DefaultCoordinateProfileCode);

        Assert.True(profile.IsActive);
        Assert.Equal(CoordinateProfileStatus.Enabled, profile.Status);
        Assert.NotNull(profile.ActiveVersionId);
        var activeVersion = await dbContext.CoordinateProfileVersions.SingleAsync(x => x.Id == profile.ActiveVersionId);
        Assert.Equal(CoordinateProfileVersionStatus.Active, activeVersion.Status);
        Assert.Equal(CoordinateVersionUsageScope.MockOnly, activeVersion.UsageScope);
        Assert.Equal(CoordinateVersionVerificationStatus.Unverified, activeVersion.VerificationStatus);

        var needle1 = profile.CoordinatePoints.Single(x => x.PointCode == "Needle1");
        Assert.Equal(0, needle1.PresetXUm);
        Assert.Equal(0, needle1.PresetYUm);
        Assert.Equal(0, needle1.CalibratedXUm);
        Assert.Equal(0, needle1.CalibratedYUm);

        var needle2 = profile.CoordinatePoints.Single(x => x.PointCode == "Needle2");
        Assert.Equal(0, needle2.PresetXUm);
        Assert.Equal(25000, needle2.PresetYUm);
        Assert.Equal(0, needle2.CalibratedXUm);
        Assert.Equal(25000, needle2.CalibratedYUm);

        var r1 = profile.CoordinatePoints.Single(x => x.PointCode == "R1");
        Assert.Null(r1.PresetXUm);
        Assert.Null(r1.PresetYUm);
        Assert.True(r1.RequiresCalibration);
    }

    [Fact]
    public async Task Seeder_is_idempotent()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var seeder = new ReferenceDataSeeder(dbContext);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.Equal(3, await dbContext.Roles.CountAsync());
        Assert.Equal(4, await dbContext.Drawers.CountAsync());
        Assert.Equal(16, await dbContext.PhysicalSlots.CountAsync());
        Assert.Equal(40, await dbContext.ReagentRackPositions.CountAsync());
        Assert.Equal(8, await dbContext.DabMixPositions.CountAsync());
        Assert.Equal(4, await dbContext.WashPositions.CountAsync());
        Assert.Equal(73, await dbContext.CoordinatePoints.CountAsync());
        Assert.Equal(1, await dbContext.CoordinateProfileVersions.CountAsync(x => x.IsActive));
    }

    [Fact]
    public async Task Seeder_creates_manual_acceptance_published_he_ihc_workflows_and_001_mapping()
    {
        await using var dbContext = await CreateMigratedContextAsync();

        await new ReferenceDataSeeder(dbContext).SeedAsync();

        var he = await dbContext.WorkflowDefinitions
            .Include(x => x.Versions)
            .ThenInclude(x => x.Steps)
            .Include(x => x.Versions)
            .ThenInclude(x => x.ReagentRequirements)
            .SingleAsync(x => x.Code == ReferenceDataSeeder.ManualHeWorkflowCode);
        var heVersion = he.Versions.Single(x => x.VersionNo == 1);
        Assert.Equal("测试 HE 流程", he.Name);
        Assert.Equal(StainingTaskType.He, he.WorkflowType);
        Assert.Equal(WorkflowVersionStatus.Published, heVersion.Status);
        Assert.Equal(StainingTaskType.He, heVersion.DefaultExperimentType);
        Assert.Equal("1", heVersion.VersionLabel);
        Assert.NotNull(heVersion.PublishedAtUtc);
        Assert.True(heVersion.Steps.Count >= 2);
        Assert.Contains(heVersion.Steps, x => x.MajorStepCode == "HEMATOXYLIN" && x.ReagentCode == "HEM");
        Assert.Contains(heVersion.Steps, x => x.MajorStepCode == "TERMINAL_WASH");

        var ihc = await dbContext.WorkflowDefinitions
            .Include(x => x.Versions)
            .ThenInclude(x => x.Steps)
            .Include(x => x.Versions)
            .ThenInclude(x => x.ReagentRequirements)
            .SingleAsync(x => x.Code == ReferenceDataSeeder.ManualIhcWorkflowCode);
        var ihcVersion = ihc.Versions.Single(x => x.VersionNo == 1);
        Assert.Equal("测试 IHC 001-A", ihc.Name);
        Assert.Equal(StainingTaskType.Ihc, ihc.WorkflowType);
        Assert.Equal(WorkflowVersionStatus.Published, ihcVersion.Status);
        Assert.Equal(StainingTaskType.Ihc, ihcVersion.DefaultExperimentType);
        Assert.Equal("1", ihcVersion.VersionLabel);
        Assert.Equal(9, ihcVersion.Steps.Count);
        Assert.Contains(ihcVersion.Steps, x => x.MajorStepCode == "BLOCKING");
        Assert.Contains(ihcVersion.Steps, x => x.MajorStepCode == "PRIMARY_ANTIBODY" && x.ReagentCode == "P01");
        Assert.Contains(ihcVersion.Steps, x => x.MajorStepCode == "SECONDARY_ANTIBODY" && x.ReagentCode == "SEC");
        Assert.Contains(ihcVersion.Steps, x => x.MajorStepCode == "DAB" && x.ActionType == "Dab");
        Assert.Contains(ihcVersion.Steps, x => x.MajorStepCode == "FINAL_WASH");
        Assert.All(heVersion.Steps.Concat(ihcVersion.Steps), x => Assert.InRange(x.DurationSeconds ?? 0, 3, 5));

        var requiredCodes = heVersion.ReagentRequirements
            .Concat(ihcVersion.ReagentRequirements)
            .Select(x => x.ReagentCode)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        Assert.Equal(["BLK", "DAB", "HEM", "P01", "SEC", "WAS"], requiredCodes);
        foreach (var reagentCode in requiredCodes)
        {
            Assert.True(await dbContext.ReagentDefinitions.AnyAsync(x => x.ReagentCode == reagentCode && x.IsEnabled));
        }

        var mapping = await dbContext.PrimaryAntibodyWorkflowMappings
            .Include(x => x.WorkflowVersion)
            .ThenInclude(x => x!.WorkflowDefinition)
            .SingleAsync(x => x.PrimaryAntibodyCode == ReferenceDataSeeder.ManualPrimaryAntibodyCode && x.WorkflowVersionId == ihcVersion.Id);
        Assert.True(mapping.IsEnabled);
        Assert.Equal(WorkflowVersionStatus.Published, mapping.WorkflowVersion!.Status);
        Assert.Equal(StainingTaskType.Ihc, mapping.WorkflowVersion.WorkflowDefinition!.WorkflowType);
    }

    [Fact]
    public async Task Manual_acceptance_seed_is_idempotent()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var seeder = new ReferenceDataSeeder(dbContext);

        await seeder.SeedAsync();
        var firstSummary = await seeder.GetManualAcceptanceSeedSummaryAsync();
        await seeder.SeedAsync();
        var secondSummary = await seeder.GetManualAcceptanceSeedSummaryAsync();

        Assert.Equal(firstSummary.HeWorkflowVersionId, secondSummary.HeWorkflowVersionId);
        Assert.Equal(firstSummary.IhcWorkflowVersionId, secondSummary.IhcWorkflowVersionId);
        Assert.Equal(firstSummary.PrimaryAntibodyWorkflowVersionId, secondSummary.PrimaryAntibodyWorkflowVersionId);
        Assert.Equal(firstSummary.RequiredReagentCodes, secondSummary.RequiredReagentCodes);
        Assert.True(secondSummary.HeIsDefault);
        Assert.True(secondSummary.IhcIsDefault);
        Assert.Equal(1, await dbContext.WorkflowDefinitions.CountAsync(x => x.Code == ReferenceDataSeeder.ManualHeWorkflowCode));
        Assert.Equal(1, await dbContext.WorkflowDefinitions.CountAsync(x => x.Code == ReferenceDataSeeder.ManualIhcWorkflowCode));
        Assert.Equal(1, await dbContext.WorkflowVersions.CountAsync(x => x.WorkflowDefinition!.Code == ReferenceDataSeeder.ManualHeWorkflowCode && x.VersionNo == 1));
        Assert.Equal(1, await dbContext.WorkflowVersions.CountAsync(x => x.WorkflowDefinition!.Code == ReferenceDataSeeder.ManualIhcWorkflowCode && x.VersionNo == 1));
        Assert.Equal(1, await dbContext.WorkflowVersions.CountAsync(x => x.DefaultExperimentType == StainingTaskType.He));
        Assert.Equal(1, await dbContext.WorkflowVersions.CountAsync(x => x.DefaultExperimentType == StainingTaskType.Ihc));
        Assert.Equal(2, await dbContext.WorkflowSteps.CountAsync(x => x.WorkflowVersionId == firstSummary.HeWorkflowVersionId));
        Assert.Equal(9, await dbContext.WorkflowSteps.CountAsync(x => x.WorkflowVersionId == firstSummary.IhcWorkflowVersionId));
        Assert.Equal(1, await dbContext.PrimaryAntibodyWorkflowMappings.CountAsync(x =>
            x.PrimaryAntibodyCode == ReferenceDataSeeder.ManualPrimaryAntibodyCode
            && x.WorkflowVersionId == firstSummary.IhcWorkflowVersionId));

        foreach (var reagentCode in firstSummary.RequiredReagentCodes)
        {
            Assert.Equal(1, await dbContext.ReagentDefinitions.CountAsync(x => x.ReagentCode == reagentCode));
        }
    }

    [Fact]
    public async Task Sqlite_foreign_keys_are_enforced()
    {
        await using var dbContext = await CreateMigratedContextAsync();

        dbContext.PhysicalSlots.Add(new PhysicalSlot
        {
            DrawerId = Guid.NewGuid().ToString(),
            Code = "BAD-01",
            SlotNo = 1,
            VerticalOrderFromBottom = 1,
            HeatPointId = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private static async Task<StainerDbContext> CreateMigratedContextAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-reference-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        var connectionString = $"Data Source={databasePath}";
        var options = new DbContextOptionsBuilder<StainerDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaConnectionInterceptor())
            .Options;
        var dbContext = new StainerDbContext(options);
        DatabaseInitializer.EnsureDatabaseDirectory(connectionString);
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }
}
