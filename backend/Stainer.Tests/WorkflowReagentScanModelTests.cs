using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Services;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;
using Stainer.Web.Infrastructure.Repositories;

namespace Stainer.Tests;

public sealed class WorkflowReagentScanModelTests
{
    [Fact]
    public async Task Primary_antibody_code_can_map_to_multiple_published_ihc_workflows()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var repository = new EfWorkflowRepository(dbContext);

        var firstVersion = await CreatePublishedWorkflowVersionAsync(dbContext, "WF-MAP-1", 1);
        var secondVersion = await CreatePublishedWorkflowVersionAsync(dbContext, "WF-MAP-2", 1);

        dbContext.PrimaryAntibodyWorkflowMappings.AddRange(
            new PrimaryAntibodyWorkflowMapping
            {
                PrimaryAntibodyCode = "PA1",
                WorkflowVersionId = firstVersion.Id,
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            new PrimaryAntibodyWorkflowMapping
            {
                PrimaryAntibodyCode = "PA1",
                WorkflowVersionId = secondVersion.Id,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var mappedVersions = await repository.GetPublishedVersionsForPrimaryAntibodyAsync("PA1");

        Assert.Equal(2, mappedVersions.Count);
        Assert.All(mappedVersions, x => Assert.Equal(WorkflowVersionStatus.Published, x.Status));
    }

    [Fact]
    public async Task Published_workflow_version_can_be_modified_in_place()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        var version = await CreatePublishedWorkflowVersionAsync(dbContext, "WF-LOCKED", 1);

        version.ChangeNote = "approved direct edit";

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.WorkflowVersions.SingleAsync(x => x.Id == version.Id);
        Assert.Equal(WorkflowVersionStatus.Published, persisted.Status);
        Assert.Equal("approved direct edit", persisted.ChangeNote);
    }

    [Fact]
    public void Reagent_barcode_parser_converts_quantity_to_microliters()
    {
        var parser = new ReagentBarcodeParser();

        var fiveMl = parser.Parse("ABC05020260101001");
        var ninetyNinePointNineMl = parser.Parse("XYZ99920260101099");

        Assert.True(fiveMl.IsValid);
        Assert.Equal(5000, fiveMl.QuantityUl);
        Assert.Equal("ABC", fiveMl.ReagentCode);
        Assert.Equal("20260101", fiveMl.ProductionBatchNo);
        Assert.Equal("001", fiveMl.SerialNo);

        Assert.True(ninetyNinePointNineMl.IsValid);
        Assert.Equal(99900, ninetyNinePointNineMl.QuantityUl);
    }

    [Fact]
    public async Task Scan_session_records_empty_valid_and_invalid_items()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        await new ReferenceDataSeeder(dbContext).SeedAsync();

        var positions = await dbContext.ReagentRackPositions
            .Where(x => x.PositionNo <= 3)
            .OrderBy(x => x.PositionNo)
            .ToListAsync();

        dbContext.ReagentScanSessions.Add(new ReagentScanSession
        {
            SessionCode = "SCAN-THREE-STATES",
            Status = "Completed",
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Items =
            {
                CreateScanItem(positions[0], ReagentScanResult.Empty, null, false, "Empty position."),
                CreateScanItem(positions[1], ReagentScanResult.Valid, "ABC05020260101001", true, "OK"),
                CreateScanItem(positions[2], ReagentScanResult.Invalid, "BAD", false, "Barcode text must be 17 characters.")
            }
        });
        await dbContext.SaveChangesAsync();

        var resultCounts = await dbContext.ReagentScanItems
            .GroupBy(x => x.ScanResult)
            .ToDictionaryAsync(x => x.Key, x => x.Count());

        Assert.Equal(1, resultCounts[ReagentScanResult.Empty]);
        Assert.Equal(1, resultCounts[ReagentScanResult.Valid]);
        Assert.Equal(1, resultCounts[ReagentScanResult.Invalid]);
    }

    [Fact]
    public async Task Reagent_bottle_placement_history_is_traceable()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        await new ReferenceDataSeeder(dbContext).SeedAsync();
        var repository = new EfReagentRepository(dbContext);

        var bottle = await CreateReagentBottleAsync(dbContext, "ABC05020260101001");
        var r1 = await dbContext.ReagentRackPositions.SingleAsync(x => x.Code == "R1");
        var r2 = await dbContext.ReagentRackPositions.SingleAsync(x => x.Code == "R2");

        dbContext.ReagentRackPlacements.Add(new ReagentRackPlacement
        {
            ReagentBottleId = bottle.Id,
            ReagentRackPositionId = r1.Id,
            PlacedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            RemovedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        dbContext.ReagentRackPlacements.Add(new ReagentRackPlacement
        {
            ReagentBottleId = bottle.Id,
            ReagentRackPositionId = r2.Id,
            PlacedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var history = await repository.GetBottlePlacementHistoryAsync(bottle.Id);

        Assert.Equal(2, history.Count);
        Assert.Equal("R1", history[0].ReagentRackPosition!.Code);
        Assert.NotNull(history[0].RemovedAtUtc);
        Assert.Equal("R2", history[1].ReagentRackPosition!.Code);
        Assert.Null(history[1].RemovedAtUtc);
    }

    [Fact]
    public async Task Foreign_keys_and_unique_constraints_are_enforced()
    {
        await using var dbContext = await CreateMigratedContextAsync();
        await new ReferenceDataSeeder(dbContext).SeedAsync();

        dbContext.ReagentBottles.Add(new ReagentBottle
        {
            ReagentDefinitionId = Guid.NewGuid().ToString(),
            FullBarcode = "ABC05020260101001",
            ReagentCode = "ABC",
            ProductionBatchNo = "20260101",
            SerialNo = "001",
            InitialVolumeUl = 5000,
            RemainingVolumeUl = 5000,
            ExpirationDate = new DateOnly(2027, 1, 1),
            Status = "Available"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        dbContext.ChangeTracker.Clear();

        _ = await CreateReagentBottleAsync(dbContext, "ABC05020260101001");
        _ = await CreateReagentBottleAsync(dbContext, "XYZ05020260101001");

        dbContext.ReagentBottles.Add(new ReagentBottle
        {
            ReagentDefinitionId = (await dbContext.ReagentDefinitions.SingleAsync(x => x.ReagentCode == "ABC")).Id,
            FullBarcode = "ABC05020260101001",
            ReagentCode = "ABC",
            ProductionBatchNo = "20260101",
            SerialNo = "001",
            InitialVolumeUl = 5000,
            RemainingVolumeUl = 5000,
            ExpirationDate = new DateOnly(2027, 1, 1),
            Status = "Available"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private static ReagentScanItem CreateScanItem(
        ReagentRackPosition position,
        string scanResult,
        string? rawBarcode,
        bool isValidationPassed,
        string validationMessage)
    {
        var parser = new ReagentBarcodeParser();
        var parsed = parser.Parse(rawBarcode);

        return new ReagentScanItem
        {
            ReagentRackPositionId = position.Id,
            ScannerChannelNo = position.ScannerChannelNo,
            ScannerChannelCode = position.ScannerChannelCode,
            LocatorCode = position.Code,
            ScanResult = scanResult,
            RawBarcode = rawBarcode,
            ParsedReagentCode = parsed.ReagentCode,
            ParsedQuantityUl = parsed.QuantityUl,
            ParsedBatchNo = parsed.ProductionBatchNo,
            ParsedSerialNo = parsed.SerialNo,
            IsValidationPassed = isValidationPassed,
            ValidationMessage = validationMessage,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task<WorkflowVersion> CreatePublishedWorkflowVersionAsync(StainerDbContext dbContext, string workflowCode, int versionNo)
    {
        var workflowDefinition = new WorkflowDefinition
        {
            Code = workflowCode,
            Name = $"{workflowCode} definition",
            WorkflowType = "IHC",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var workflowVersion = new WorkflowVersion
        {
            WorkflowDefinition = workflowDefinition,
            VersionNo = versionNo,
            Status = WorkflowVersionStatus.Published,
            ChangeNote = "Initial published version.",
            PublishedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        workflowVersion.Steps.Add(new WorkflowStep
        {
            StepNo = 1,
            MajorStepCode = "STEP",
            ActionType = "Incubate",
            VolumeUl = 100,
            DurationSeconds = 60,
            TargetTemperatureDeciC = 250,
            FailureStrategy = "Stop",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        dbContext.WorkflowVersions.Add(workflowVersion);
        await dbContext.SaveChangesAsync();
        return workflowVersion;
    }

    private static async Task<ReagentBottle> CreateReagentBottleAsync(StainerDbContext dbContext, string fullBarcode)
    {
        var parsed = new ReagentBarcodeParser().Parse(fullBarcode);
        Assert.True(parsed.IsValid);

        var reagentDefinition = await dbContext.ReagentDefinitions.SingleOrDefaultAsync(x => x.ReagentCode == parsed.ReagentCode);
        if (reagentDefinition is null)
        {
            reagentDefinition = new ReagentDefinition
            {
                ReagentCode = parsed.ReagentCode!,
                Name = $"Definition {parsed.ReagentCode}",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.ReagentDefinitions.Add(reagentDefinition);
            await dbContext.SaveChangesAsync();
        }

        var bottle = new ReagentBottle
        {
            ReagentDefinitionId = reagentDefinition.Id,
            FullBarcode = fullBarcode,
            ReagentCode = parsed.ReagentCode!,
            ProductionBatchNo = parsed.ProductionBatchNo!,
            SerialNo = parsed.SerialNo!,
            InitialVolumeUl = parsed.QuantityUl!.Value,
            RemainingVolumeUl = parsed.QuantityUl.Value,
            ExpirationDate = new DateOnly(2027, 1, 1),
            Status = "Available",
            FirstScannedAtUtc = DateTimeOffset.UtcNow,
            LastScannedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.ReagentBottles.Add(bottle);
        await dbContext.SaveChangesAsync();
        return bottle;
    }

    private static async Task<StainerDbContext> CreateMigratedContextAsync()
    {
        var databasePath = Path.Combine(TestPaths.TempRoot, "stainer-workflow-reagent-tests", Guid.NewGuid().ToString("N"), "stainer.db");
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
