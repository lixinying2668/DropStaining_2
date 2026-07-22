using Stainer.Web.Infrastructure;
using Stainer.Web.Application.Services;
using Stainer.Web.Application.Requests;
using Stainer.Web.Infrastructure.Data;
using Stainer.Web.Infrastructure.Health;
using Stainer.Web.Infrastructure.Web;
using Stainer.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    && string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5205");
}

builder.Services.AddStainerInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<MockRuntimeStore>();

var app = builder.Build();

if (args.Contains("--seed-reference-data", StringComparer.OrdinalIgnoreCase))
{
    using var seedScope = app.Services.CreateScope();
    var dbContext = seedScope.ServiceProvider.GetRequiredService<StainerDbContext>();
    await DatabaseInitializer.InitializeAsync(dbContext);
    await dbContext.Database.MigrateAsync();
    var seeder = seedScope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>();
    await seeder.SeedAsync();
    var summary = await seeder.GetManualAcceptanceSeedSummaryAsync();
    var appliedMigrations = (await dbContext.Database.GetAppliedMigrationsAsync()).ToList();
    var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();
    var defaults = await dbContext.WorkflowVersions
        .AsNoTracking()
        .Include(x => x.WorkflowDefinition)
        .Where(x => x.DefaultExperimentType != null)
        .OrderBy(x => x.DefaultExperimentType)
        .ToListAsync();
    var duplicateDefaults = defaults
        .GroupBy(x => x.DefaultExperimentType)
        .Where(x => x.Count() > 1)
        .Select(x => $"{x.Key}:{x.Count()}")
        .ToList();
    var duplicateMappings = await dbContext.PrimaryAntibodyWorkflowMappings
        .AsNoTracking()
        .GroupBy(x => new { x.PrimaryAntibodyCode, x.WorkflowVersionId })
        .Where(x => x.Count() > 1)
        .Select(x => $"{x.Key.PrimaryAntibodyCode}->{x.Key.WorkflowVersionId}:{x.Count()}")
        .ToListAsync();
    var defaultHe = defaults.SingleOrDefault(x => x.DefaultExperimentType == StainingTaskType.He);
    var defaultIhc = defaults.SingleOrDefault(x => x.DefaultExperimentType == StainingTaskType.Ihc);
    var mapping001Ok = defaultIhc is not null && await dbContext.PrimaryAntibodyWorkflowMappings
        .AsNoTracking()
        .AnyAsync(x => x.PrimaryAntibodyCode == ReferenceDataSeeder.ManualPrimaryAntibodyCode
            && x.IsEnabled
            && x.WorkflowVersionId == defaultIhc.Id);
    var verificationOk = pendingMigrations.Count == 0
        && duplicateDefaults.Count == 0
        && duplicateMappings.Count == 0
        && defaultHe?.Status == WorkflowVersionStatus.Published
        && defaultHe.WorkflowDefinition?.WorkflowType == StainingTaskType.He
        && defaultIhc?.Status == WorkflowVersionStatus.Published
        && defaultIhc.WorkflowDefinition?.WorkflowType == StainingTaskType.Ihc
        && mapping001Ok;

    Console.WriteLine($"Latest applied Migration: {appliedMigrations.LastOrDefault() ?? "none"}");
    Console.WriteLine($"Default HE: {FormatDefaultWorkflow(defaultHe)}");
    Console.WriteLine($"Default IHC: {FormatDefaultWorkflow(defaultIhc)}");
    Console.WriteLine($"Primary antibody 001 enabled mapping to default IHC: {mapping001Ok}");
    Console.WriteLine($"Duplicate defaults: {(duplicateDefaults.Count == 0 ? "none" : string.Join(", ", duplicateDefaults))}");
    Console.WriteLine($"Duplicate mappings: {(duplicateMappings.Count == 0 ? "none" : string.Join(", ", duplicateMappings))}");
    Console.WriteLine($"Pending migrations: {(pendingMigrations.Count == 0 ? "none" : string.Join(", ", pendingMigrations))}");
    Console.WriteLine($"Required reagent codes: {string.Join(", ", summary.RequiredReagentCodes)}");
    Console.WriteLine($"Migration + reference seed verification: {(verificationOk ? "PASS" : "FAIL")}");
    Environment.ExitCode = verificationOk ? 0 : 2;
    return;
}

if (args.Contains("--seed-mock-demo-data", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--reset-mock-demo-data", StringComparer.OrdinalIgnoreCase))
{
    using var seedScope = app.Services.CreateScope();
    var dbContext = seedScope.ServiceProvider.GetRequiredService<StainerDbContext>();
    await DatabaseInitializer.InitializeAsync(dbContext);
    await dbContext.Database.MigrateAsync();
    await seedScope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>().SeedAsync();
    var actor = new AuthenticatedUser(string.Empty, "system", "System", "admin", ["admin"]);
    if (args.Contains("--seed-mock-demo-data", StringComparer.OrdinalIgnoreCase))
    {
        var response = await seedScope.ServiceProvider.GetRequiredService<MockDemoDataSeeder>()
            .SeedAsync($"program-seed-mock-demo-{Guid.NewGuid():N}", actor);
        Console.WriteLine(response.Message);
        Console.WriteLine($"Created={response.CreatedCount}; Updated={response.UpdatedCount}; Skipped={response.SkippedCount}");
        Environment.ExitCode = response.Ok ? 0 : 2;
        return;
    }

    var confirmation = GetOption(args, "--confirm") ?? string.Empty;
    var reset = await seedScope.ServiceProvider.GetRequiredService<MockDemoDataSeeder>()
        .ResetAsync(new ResetMockDemoDataRequest($"program-reset-mock-demo-{Guid.NewGuid():N}", confirmation), actor);
    Console.WriteLine(reset.Message);
    Console.WriteLine($"Deleted={reset.DeletedCount}; Updated={reset.UpdatedCount}; Skipped={reset.SkippedCount}");
    Environment.ExitCode = reset.Ok ? 0 : 2;
    return;
}

app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { ok = true, app = "Stainer ASP.NET Core backend" }));
app.MapGet("/health/database", async (DatabaseMaintenanceService checker, CancellationToken cancellationToken) =>
{
    return Results.Ok(await checker.CheckAsync(cancellationToken));
});
app.MapStainerWebHostEndpoints();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<StainerDbContext>();
    await DatabaseInitializer.InitializeAsync(dbContext);
    await dbContext.Database.MigrateAsync();
    var backfill = scope.ServiceProvider.GetRequiredService<ChannelBatchWorkflowBackfillService>();
    await backfill.BackfillAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>();
    await seeder.SeedAsync();
    var recovery = scope.ServiceProvider.GetRequiredService<StartupRecoveryService>();
    await recovery.RecoverAsync();
    // 预 seed 温控点（drawers A-D × slots 1-4 = 16 点）与冷却单元，
    // 使 /api/twin/snapshot 在首次空库启动时也能稳定返回温控点，而非依赖 thermal API 被首次调用。
    var thermalControl = scope.ServiceProvider.GetRequiredService<ThermalControlService>();
    await thermalControl.EnsureSeededAsync(CancellationToken.None);
    // 预 seed 4 个供水孔（CH1..CH4），使 /api/water-supply/state 与 twin snapshot 首次空库即可稳定返回。
    await scope.ServiceProvider.GetRequiredService<WaterSupplyControlService>().EnsureSeededAsync(CancellationToken.None);
    // 预 seed 液路容器（SystemWater/PBS/Waste/ToxicWaste），避免空库首次打开供水孔时 SystemWater 容器缺失导致扣减丢失。
    await scope.ServiceProvider.GetRequiredService<FluidicsControlService>().EnsureSeededAsync(CancellationToken.None);
}

app.Services.GetRequiredService<StartupDeviceInitializationRunner>()
    .Start(app.Lifetime.ApplicationStopping);

app.Run();

static string FormatDefaultWorkflow(WorkflowVersion? version)
{
    return version is null
        ? "missing"
        : $"{version.WorkflowDefinition!.Name} ({version.WorkflowDefinition.Code}) v{version.VersionLabel}, Id={version.Id}, Status={version.Status}";
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

public partial class Program;
