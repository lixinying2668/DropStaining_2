using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Stainer.Web.Domain.Entities;

namespace Stainer.Web.Infrastructure.Data;

public sealed class StainerDbContext(DbContextOptions<StainerDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Drawer> Drawers => Set<Drawer>();
    public DbSet<PhysicalSlot> PhysicalSlots => Set<PhysicalSlot>();
    public DbSet<ReagentRackPosition> ReagentRackPositions => Set<ReagentRackPosition>();
    public DbSet<DabMixPosition> DabMixPositions => Set<DabMixPosition>();
    public DbSet<WashPosition> WashPositions => Set<WashPosition>();
    public DbSet<DeviceProfile> DeviceProfiles => Set<DeviceProfile>();
    public DbSet<CoordinateProfile> CoordinateProfiles => Set<CoordinateProfile>();
    public DbSet<CoordinateProfileVersion> CoordinateProfileVersions => Set<CoordinateProfileVersion>();
    public DbSet<CoordinatePoint> CoordinatePoints => Set<CoordinatePoint>();
    public DbSet<CoordinateCalibrationHistory> CoordinateCalibrationHistory => Set<CoordinateCalibrationHistory>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowReagentRequirement> WorkflowReagentRequirements => Set<WorkflowReagentRequirement>();
    public DbSet<PrimaryAntibodyWorkflowMapping> PrimaryAntibodyWorkflowMappings => Set<PrimaryAntibodyWorkflowMapping>();
    public DbSet<ReagentDefinition> ReagentDefinitions => Set<ReagentDefinition>();
    public DbSet<ReagentBottle> ReagentBottles => Set<ReagentBottle>();
    public DbSet<ReagentRackPlacement> ReagentRackPlacements => Set<ReagentRackPlacement>();
    public DbSet<ReagentScanSession> ReagentScanSessions => Set<ReagentScanSession>();
    public DbSet<ReagentScanItem> ReagentScanItems => Set<ReagentScanItem>();
    public DbSet<LiquidClassProfile> LiquidClassProfiles => Set<LiquidClassProfile>();
    public DbSet<LiquidClassVersion> LiquidClassVersions => Set<LiquidClassVersion>();
    public DbSet<LiquidClassVersionDifference> LiquidClassVersionDifferences => Set<LiquidClassVersionDifference>();
    public DbSet<LiquidClassValidationRecord> LiquidClassValidationRecords => Set<LiquidClassValidationRecord>();
    public DbSet<LegacyImportRun> LegacyImportRuns => Set<LegacyImportRun>();
    public DbSet<LegacyImportIssue> LegacyImportIssues => Set<LegacyImportIssue>();
    public DbSet<LegacyRuntimeSnapshot> LegacyRuntimeSnapshots => Set<LegacyRuntimeSnapshot>();
    public DbSet<CommandReceipt> CommandReceipts => Set<CommandReceipt>();
    public DbSet<StainingTask> StainingTasks => Set<StainingTask>();
    public DbSet<HospitalBarcodeMapping> HospitalBarcodeMappings => Set<HospitalBarcodeMapping>();
    public DbSet<SampleScanSession> SampleScanSessions => Set<SampleScanSession>();
    public DbSet<SampleScanItem> SampleScanItems => Set<SampleScanItem>();
    public DbSet<MockLisEntry> MockLisEntries => Set<MockLisEntry>();
    public DbSet<LisQueryLog> LisQueryLogs => Set<LisQueryLog>();
    public DbSet<MockDemoDataTag> MockDemoDataTags => Set<MockDemoDataTag>();
    public DbSet<ChannelBatch> ChannelBatches => Set<ChannelBatch>();
    public DbSet<WorkflowAssignmentHistory> WorkflowAssignmentHistory => Set<WorkflowAssignmentHistory>();
    public DbSet<SlideTask> SlideTasks => Set<SlideTask>();
    public DbSet<MachineRun> MachineRuns => Set<MachineRun>();
    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();
    public DbSet<WorkflowStepExecution> WorkflowStepExecutions => Set<WorkflowStepExecution>();
    public DbSet<DeviceCommandExecution> DeviceCommandExecutions => Set<DeviceCommandExecution>();
    public DbSet<DeviceInitializationRun> DeviceInitializationRuns => Set<DeviceInitializationRun>();
    public DbSet<DeviceInitializationCheck> DeviceInitializationChecks => Set<DeviceInitializationCheck>();
    public DbSet<ThermalPointState> ThermalPointStates => Set<ThermalPointState>();
    public DbSet<CoolingUnitState> CoolingUnitStates => Set<CoolingUnitState>();
    public DbSet<TemperatureTelemetry> TemperatureTelemetry => Set<TemperatureTelemetry>();
    public DbSet<PumpChannelState> PumpChannelStates => Set<PumpChannelState>();
    public DbSet<MixerChannelState> MixerChannelStates => Set<MixerChannelState>();
    public DbSet<LiquidContainerState> LiquidContainerStates => Set<LiquidContainerState>();
    public DbSet<FluidicsTelemetry> FluidicsTelemetry => Set<FluidicsTelemetry>();
    public DbSet<RobotArmState> RobotArmStates => Set<RobotArmState>();
    public DbSet<NeedleState> NeedleStates => Set<NeedleState>();
    public DbSet<PipettingOperation> PipettingOperations => Set<PipettingOperation>();
    public DbSet<MachineResourceLease> MachineResourceLeases => Set<MachineResourceLease>();
    public DbSet<ReagentReservation> ReagentReservations => Set<ReagentReservation>();
    public DbSet<ReagentConsumption> ReagentConsumptions => Set<ReagentConsumption>();
    public DbSet<SystemLiquidUsage> SystemLiquidUsages => Set<SystemLiquidUsage>();
    public DbSet<DispenseExecution> DispenseExecutions => Set<DispenseExecution>();
    public DbSet<DabBatch> DabBatches => Set<DabBatch>();
    public DbSet<DabBatchTask> DabBatchTasks => Set<DabBatchTask>();
    public DbSet<DabBatchUsage> DabBatchUsages => Set<DabBatchUsage>();
    public DbSet<DabRepreparationPlan> DabRepreparationPlans => Set<DabRepreparationPlan>();
    public DbSet<Alarm> Alarms => Set<Alarm>();
    public DbSet<AlarmAction> AlarmActions => Set<AlarmAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureRole(modelBuilder);
        ConfigureUser(modelBuilder);
        ConfigureUserRole(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ConfigureDrawer(modelBuilder);
        ConfigurePhysicalSlot(modelBuilder);
        ConfigureReagentRackPosition(modelBuilder);
        ConfigureDabMixPosition(modelBuilder);
        ConfigureWashPosition(modelBuilder);
        ConfigureDeviceProfile(modelBuilder);
        ConfigureCoordinateProfile(modelBuilder);
        ConfigureCoordinateProfileVersion(modelBuilder);
        ConfigureCoordinatePoint(modelBuilder);
        ConfigureCoordinateCalibrationHistory(modelBuilder);
        ConfigureWorkflowDefinition(modelBuilder);
        ConfigureWorkflowVersion(modelBuilder);
        ConfigureWorkflowStep(modelBuilder);
        ConfigureWorkflowReagentRequirement(modelBuilder);
        ConfigurePrimaryAntibodyWorkflowMapping(modelBuilder);
        ConfigureLiquidClassProfile(modelBuilder);
        ConfigureLiquidClassVersion(modelBuilder);
        ConfigureReagentDefinition(modelBuilder);
        ConfigureReagentBottle(modelBuilder);
        ConfigureReagentRackPlacement(modelBuilder);
        ConfigureReagentScanSession(modelBuilder);
        ConfigureReagentScanItem(modelBuilder);
        ConfigureLegacyImportRun(modelBuilder);
        ConfigureLegacyImportIssue(modelBuilder);
        ConfigureLegacyRuntimeSnapshot(modelBuilder);
        ConfigureCommandReceipt(modelBuilder);
        ConfigureStainingTask(modelBuilder);
        ConfigureHospitalBarcodeMapping(modelBuilder);
        ConfigureSampleScan(modelBuilder);
        ConfigureLis(modelBuilder);
        ConfigureMockDemoDataTag(modelBuilder);
        ConfigureRuntimeLedger(modelBuilder);
        ConfigureDeviceInitialization(modelBuilder);
        ConfigureThermalState(modelBuilder);
        ConfigureFluidicsState(modelBuilder);
        ConfigureMotionState(modelBuilder);
    }

    public override int SaveChanges()
    {
        ValidateWorkflowVersionChanges();
        ValidateCoordinateVersionChanges();
        ValidateLiquidClassVersionChanges();
        EnsureWorkflowAssignmentHistoryIsAppendOnly();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateWorkflowVersionChanges();
        ValidateCoordinateVersionChanges();
        ValidateLiquidClassVersionChanges();
        EnsureWorkflowAssignmentHistoryIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(true, cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await ValidateWorkflowVersionChangesAsync(cancellationToken);
        await ValidateCoordinateVersionChangesAsync(cancellationToken);
        ValidateLiquidClassVersionChanges();
        EnsureWorkflowAssignmentHistoryIsAppendOnly();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateLiquidClassVersionChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<LiquidClassVersion>())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var originalStatus = entry.Property(x => x.Status).OriginalValue;
            if (originalStatus == LiquidClassVersionStatus.Draft)
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Published or enabled Liquid Class versions cannot be deleted.");
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(LiquidClassVersion.Status),
                nameof(LiquidClassVersion.PublishedAtUtc),
                nameof(LiquidClassVersion.PublishedByUserId),
                nameof(LiquidClassVersion.EnabledAtUtc),
                nameof(LiquidClassVersion.EnabledByUserId)
            };
            if (entry.Properties.Any(x => x.IsModified && !allowed.Contains(x.Metadata.Name)))
            {
                throw new InvalidOperationException("Published or enabled Liquid Class parameters are immutable. Create a new draft version.");
            }

            var currentStatus = entry.Property(x => x.Status).CurrentValue;
            var validTransition = (originalStatus == LiquidClassVersionStatus.Published && currentStatus == LiquidClassVersionStatus.Enabled)
                || (originalStatus == LiquidClassVersionStatus.Enabled && currentStatus == LiquidClassVersionStatus.Published)
                || originalStatus == currentStatus;
            if (!validTransition)
            {
                throw new InvalidOperationException("Liquid Class lifecycle transition is invalid.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<LiquidClassVersionDifference>().Where(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Liquid Class version differences are append-only.");
        }
    }

    private void ValidateWorkflowVersionChanges()
    {
        ChangeTracker.DetectChanges();
        EnsurePublishedWorkflowVersionsAreImmutable();

        var changedChildVersionIds = GetChangedWorkflowChildVersionIds();
        var changedMappingVersionIds = GetChangedMappingVersionIds();
        var addedVersionIds = GetAddedWorkflowVersionIds();

        var childStatuses = LoadPersistedWorkflowVersionStatuses(changedChildVersionIds);
        EnsureWorkflowChildrenDoNotModifyPublishedVersions(changedChildVersionIds, addedVersionIds, childStatuses);

        var mappingStatuses = LoadCurrentWorkflowVersionStatuses(changedMappingVersionIds);
        EnsureMappingsPointToPublishedVersions(changedMappingVersionIds, mappingStatuses);
    }

    private void EnsureWorkflowAssignmentHistoryIsAppendOnly()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<WorkflowAssignmentHistory>())
        {
            if (entry.State == EntityState.Added
                && string.IsNullOrWhiteSpace(entry.Entity.OperatorUserId)
                && !string.IsNullOrWhiteSpace(entry.Entity.ActorUserId))
            {
                entry.Entity.OperatorUserId = entry.Entity.ActorUserId;
            }
        }

        if (ChangeTracker.Entries<WorkflowAssignmentHistory>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Workflow assignment history is append-only and cannot be modified or deleted.");
        }
    }

    private async Task ValidateWorkflowVersionChangesAsync(CancellationToken cancellationToken)
    {
        ChangeTracker.DetectChanges();
        EnsurePublishedWorkflowVersionsAreImmutable();

        var changedChildVersionIds = GetChangedWorkflowChildVersionIds();
        var changedMappingVersionIds = GetChangedMappingVersionIds();
        var addedVersionIds = GetAddedWorkflowVersionIds();

        var childStatuses = await LoadPersistedWorkflowVersionStatusesAsync(changedChildVersionIds, cancellationToken);
        EnsureWorkflowChildrenDoNotModifyPublishedVersions(changedChildVersionIds, addedVersionIds, childStatuses);

        var mappingStatuses = await LoadCurrentWorkflowVersionStatusesAsync(changedMappingVersionIds, cancellationToken);
        EnsureMappingsPointToPublishedVersions(changedMappingVersionIds, mappingStatuses);
    }

    private void EnsurePublishedWorkflowVersionsAreImmutable()
    {
        foreach (var entry in ChangeTracker.Entries<WorkflowVersion>())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var originalStatus = entry.Property(x => x.Status).OriginalValue;
            if (originalStatus == WorkflowVersionStatus.Published && !IsAllowedPublishedLifecycleChange(entry))
            {
                throw new InvalidOperationException("Published workflow versions cannot be modified in place. Create a new version for changes.");
            }
        }
    }

    private static bool IsAllowedPublishedLifecycleChange(EntityEntry<WorkflowVersion> entry)
    {
        if (entry.State == EntityState.Deleted)
        {
            return false;
        }

        var modifiedProperties = entry.Properties
            .Where(x => x.IsModified)
            .Select(x => x.Metadata.Name)
            .ToList();
        if (modifiedProperties.Count == 0)
        {
            return false;
        }

        if (entry.Property(x => x.Status).CurrentValue == WorkflowVersionStatus.Published)
        {
            var allowedDefaultProperties = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(WorkflowVersion.DefaultExperimentType),
                nameof(WorkflowVersion.UpdatedAtUtc)
            };
            return modifiedProperties.All(allowedDefaultProperties.Contains);
        }

        var allowedRetireProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(WorkflowVersion.Status),
            nameof(WorkflowVersion.RetiredAtUtc),
            nameof(WorkflowVersion.UpdatedAtUtc)
        };
        return modifiedProperties.All(allowedRetireProperties.Contains)
            && entry.Property(x => x.Status).CurrentValue == WorkflowVersionStatus.Retired
            && entry.Property(x => x.RetiredAtUtc).CurrentValue is not null;
    }

    private string[] GetChangedWorkflowChildVersionIds()
    {
        return ChangeTracker.Entries<WorkflowStep>()
            .Where(IsChanged)
            .Select(x => x.Entity.WorkflowVersionId)
            .Concat(ChangeTracker.Entries<WorkflowReagentRequirement>().Where(IsChanged).Select(x => x.Entity.WorkflowVersionId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
    }

    private string[] GetChangedMappingVersionIds()
    {
        return ChangeTracker.Entries<PrimaryAntibodyWorkflowMapping>()
            .Where(IsChanged)
            .Select(x => x.Entity.WorkflowVersionId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
    }

    private string[] GetAddedWorkflowVersionIds()
    {
        return ChangeTracker.Entries<WorkflowVersion>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity.Id)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
    }

    private Dictionary<string, string> LoadPersistedWorkflowVersionStatuses(string[] workflowVersionIds)
    {
        if (workflowVersionIds.Length == 0)
        {
            return [];
        }

        return WorkflowVersions
            .AsNoTracking()
            .Where(x => workflowVersionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Status })
            .ToDictionary(x => x.Id, x => x.Status);
    }

    private async Task<Dictionary<string, string>> LoadPersistedWorkflowVersionStatusesAsync(string[] workflowVersionIds, CancellationToken cancellationToken)
    {
        if (workflowVersionIds.Length == 0)
        {
            return [];
        }

        return await WorkflowVersions
            .AsNoTracking()
            .Where(x => workflowVersionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Status })
            .ToDictionaryAsync(x => x.Id, x => x.Status, cancellationToken);
    }

    private Dictionary<string, string> LoadCurrentWorkflowVersionStatuses(string[] workflowVersionIds)
    {
        var statuses = GetTrackedCurrentWorkflowVersionStatuses(workflowVersionIds);
        var missingIds = workflowVersionIds.Where(x => !statuses.ContainsKey(x)).ToArray();
        foreach (var item in LoadPersistedWorkflowVersionStatuses(missingIds))
        {
            statuses[item.Key] = item.Value;
        }

        return statuses;
    }

    private async Task<Dictionary<string, string>> LoadCurrentWorkflowVersionStatusesAsync(string[] workflowVersionIds, CancellationToken cancellationToken)
    {
        var statuses = GetTrackedCurrentWorkflowVersionStatuses(workflowVersionIds);
        var missingIds = workflowVersionIds.Where(x => !statuses.ContainsKey(x)).ToArray();
        foreach (var item in await LoadPersistedWorkflowVersionStatusesAsync(missingIds, cancellationToken))
        {
            statuses[item.Key] = item.Value;
        }

        return statuses;
    }

    private Dictionary<string, string> GetTrackedCurrentWorkflowVersionStatuses(string[] workflowVersionIds)
    {
        var idSet = workflowVersionIds.ToHashSet(StringComparer.Ordinal);
        return ChangeTracker.Entries<WorkflowVersion>()
            .Where(x => x.State != EntityState.Deleted && idSet.Contains(x.Entity.Id))
            .GroupBy(x => x.Entity.Id)
            .ToDictionary(x => x.Key, x => x.First().Entity.Status);
    }

    private static void EnsureWorkflowChildrenDoNotModifyPublishedVersions(
        string[] changedChildVersionIds,
        string[] addedVersionIds,
        IReadOnlyDictionary<string, string> persistedStatuses)
    {
        var addedVersionIdSet = addedVersionIds.ToHashSet(StringComparer.Ordinal);
        foreach (var workflowVersionId in changedChildVersionIds)
        {
            if (addedVersionIdSet.Contains(workflowVersionId))
            {
                continue;
            }

            if (persistedStatuses.TryGetValue(workflowVersionId, out var status) && status == WorkflowVersionStatus.Published)
            {
                throw new InvalidOperationException("Published workflow version steps and reagent requirements cannot be modified in place.");
            }
        }
    }

    private static void EnsureMappingsPointToPublishedVersions(
        string[] changedMappingVersionIds,
        IReadOnlyDictionary<string, string> currentStatuses)
    {
        foreach (var workflowVersionId in changedMappingVersionIds)
        {
            if (!currentStatuses.TryGetValue(workflowVersionId, out var status) || status != WorkflowVersionStatus.Published)
            {
                throw new InvalidOperationException("Primary antibody workflow mappings must target published workflow versions.");
            }
        }
    }

    private void ValidateCoordinateVersionChanges()
    {
        ChangeTracker.DetectChanges();
        EnsureCoordinateVersionLifecycleIsControlled();

        var changedPointVersionIds = GetChangedCoordinatePointVersionIds();
        var addedVersionIds = GetAddedCoordinateVersionIds();
        var protectedStatuses = LoadPersistedCoordinateVersionStatuses(changedPointVersionIds);
        var referencedVersionIds = LoadReferencedCoordinateVersionIds(changedPointVersionIds);
        EnsureCoordinatePointsDoNotModifyProtectedVersions(changedPointVersionIds, addedVersionIds, protectedStatuses, referencedVersionIds);
    }

    private async Task ValidateCoordinateVersionChangesAsync(CancellationToken cancellationToken)
    {
        ChangeTracker.DetectChanges();
        EnsureCoordinateVersionLifecycleIsControlled();

        var changedPointVersionIds = GetChangedCoordinatePointVersionIds();
        var addedVersionIds = GetAddedCoordinateVersionIds();
        var protectedStatuses = await LoadPersistedCoordinateVersionStatusesAsync(changedPointVersionIds, cancellationToken);
        var referencedVersionIds = await LoadReferencedCoordinateVersionIdsAsync(changedPointVersionIds, cancellationToken);
        EnsureCoordinatePointsDoNotModifyProtectedVersions(changedPointVersionIds, addedVersionIds, protectedStatuses, referencedVersionIds);
    }

    private void EnsureCoordinateVersionLifecycleIsControlled()
    {
        foreach (var entry in ChangeTracker.Entries<CoordinateProfileVersion>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Coordinate profile versions cannot be deleted.");
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var originalStatus = entry.Property(x => x.Status).OriginalValue;
            if (originalStatus is CoordinateProfileVersionStatus.Published or CoordinateProfileVersionStatus.Active or CoordinateProfileVersionStatus.Retired
                && !IsAllowedCoordinateLifecycleChange(entry))
            {
                throw new InvalidOperationException("Published or active coordinate versions cannot be modified in place. Create a new version for changes.");
            }
        }
    }

    private static bool IsAllowedCoordinateLifecycleChange(EntityEntry<CoordinateProfileVersion> entry)
    {
        var modifiedProperties = entry.Properties
            .Where(x => x.IsModified)
            .Select(x => x.Metadata.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (modifiedProperties.Count == 0)
        {
            return false;
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(CoordinateProfileVersion.Status),
            nameof(CoordinateProfileVersion.IsActive),
            nameof(CoordinateProfileVersion.PublishedByUserId),
            nameof(CoordinateProfileVersion.ActivatedByUserId),
            nameof(CoordinateProfileVersion.PublishedAtUtc),
            nameof(CoordinateProfileVersion.ActivatedAtUtc),
            nameof(CoordinateProfileVersion.RetiredAtUtc),
            nameof(CoordinateProfileVersion.ValidationResultJson)
        };
        return modifiedProperties.All(allowed.Contains);
    }

    private string[] GetChangedCoordinatePointVersionIds()
    {
        return ChangeTracker.Entries<CoordinatePoint>()
            .Where(IsChanged)
            .Select(x => x.Entity.CoordinateProfileVersionId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
    }

    private string[] GetAddedCoordinateVersionIds()
    {
        return ChangeTracker.Entries<CoordinateProfileVersion>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity.Id)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
    }

    private Dictionary<string, string> LoadPersistedCoordinateVersionStatuses(string[] versionIds)
    {
        if (versionIds.Length == 0)
        {
            return [];
        }

        return CoordinateProfileVersions
            .AsNoTracking()
            .Where(x => versionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Status })
            .ToDictionary(x => x.Id, x => x.Status);
    }

    private async Task<Dictionary<string, string>> LoadPersistedCoordinateVersionStatusesAsync(string[] versionIds, CancellationToken cancellationToken)
    {
        if (versionIds.Length == 0)
        {
            return [];
        }

        return await CoordinateProfileVersions
            .AsNoTracking()
            .Where(x => versionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Status })
            .ToDictionaryAsync(x => x.Id, x => x.Status, cancellationToken);
    }

    private HashSet<string> LoadReferencedCoordinateVersionIds(string[] versionIds)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (versionIds.Length == 0)
        {
            return result;
        }

        foreach (var id in ChannelBatches.AsNoTracking().Where(x => x.CoordinateProfileVersionId != null && versionIds.Contains(x.CoordinateProfileVersionId)).Select(x => x.CoordinateProfileVersionId!))
        {
            result.Add(id);
        }

        foreach (var id in MachineRuns.AsNoTracking().Where(x => x.CoordinateProfileVersionId != null && versionIds.Contains(x.CoordinateProfileVersionId)).Select(x => x.CoordinateProfileVersionId!))
        {
            result.Add(id);
        }

        return result;
    }

    private async Task<HashSet<string>> LoadReferencedCoordinateVersionIdsAsync(string[] versionIds, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (versionIds.Length == 0)
        {
            return result;
        }

        var batchIds = await ChannelBatches.AsNoTracking()
            .Where(x => x.CoordinateProfileVersionId != null && versionIds.Contains(x.CoordinateProfileVersionId))
            .Select(x => x.CoordinateProfileVersionId!)
            .ToListAsync(cancellationToken);
        foreach (var id in batchIds)
        {
            result.Add(id);
        }

        var runIds = await MachineRuns.AsNoTracking()
            .Where(x => x.CoordinateProfileVersionId != null && versionIds.Contains(x.CoordinateProfileVersionId))
            .Select(x => x.CoordinateProfileVersionId!)
            .ToListAsync(cancellationToken);
        foreach (var id in runIds)
        {
            result.Add(id);
        }

        return result;
    }

    private static void EnsureCoordinatePointsDoNotModifyProtectedVersions(
        string[] changedPointVersionIds,
        string[] addedVersionIds,
        IReadOnlyDictionary<string, string> persistedStatuses,
        IReadOnlySet<string> referencedVersionIds)
    {
        var addedVersionIdSet = addedVersionIds.ToHashSet(StringComparer.Ordinal);
        foreach (var versionId in changedPointVersionIds)
        {
            if (addedVersionIdSet.Contains(versionId))
            {
                continue;
            }

            if (referencedVersionIds.Contains(versionId)
                || (persistedStatuses.TryGetValue(versionId, out var status)
                    && status is CoordinateProfileVersionStatus.Published or CoordinateProfileVersionStatus.Active or CoordinateProfileVersionStatus.Retired))
            {
                throw new InvalidOperationException("Coordinate target points cannot be modified in place after publish, activation, or task/run reference.");
            }
        }
    }

    private static bool IsChanged(EntityEntry entry)
    {
        return entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;
    }

    private static void ConfigureRole(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Role>();
        entity.ToTable("roles");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<User>();
        entity.ToTable("users");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(128).IsRequired();
        entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        entity.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(512);
        entity.Property(x => x.PasswordHashAlgorithm).HasColumnName("password_hash_algorithm").HasMaxLength(128);
        entity.Property(x => x.PasswordUpdatedAtUtc).HasColumnName("password_updated_at_utc");
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Username).IsUnique();
    }

    private static void ConfigureUserRole(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserRole>();
        entity.ToTable("user_roles");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.RoleId).HasColumnName("role_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();
        entity.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Role).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AuditLog>();
        entity.ToTable("audit_logs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.ActorUserId).HasColumnName("actor_user_id").HasMaxLength(36);
        entity.Property(x => x.Action).HasColumnName("action").HasMaxLength(128).IsRequired();
        entity.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(128).IsRequired();
        entity.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(128);
        entity.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000).IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasOne(x => x.ActorUser).WithMany(x => x.AuditLogs).HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
        entity.HasIndex(x => x.CreatedAtUtc);
    }

    private static void ConfigureDrawer(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Drawer>();
        entity.ToTable("drawers");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(8).IsRequired();
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        entity.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired();
        entity.Property(x => x.HeatBoardId).HasColumnName("heat_board_id").IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.HeatBoardId).IsUnique();
    }

    private static void ConfigurePhysicalSlot(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PhysicalSlot>();
        entity.ToTable("physical_slots");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.DrawerId).HasColumnName("drawer_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(16).IsRequired();
        entity.Property(x => x.SlotNo).HasColumnName("slot_no").IsRequired();
        entity.Property(x => x.VerticalOrderFromBottom).HasColumnName("vertical_order_from_bottom").IsRequired();
        entity.Property(x => x.HeatPointId).HasColumnName("heat_point_id").IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => new { x.DrawerId, x.SlotNo }).IsUnique();
        entity.HasIndex(x => new { x.DrawerId, x.HeatPointId }).IsUnique();
        entity.HasOne(x => x.Drawer).WithMany(x => x.PhysicalSlots).HasForeignKey(x => x.DrawerId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureReagentRackPosition(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReagentRackPosition>();
        entity.ToTable("reagent_rack_positions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(8).IsRequired();
        entity.Property(x => x.PositionNo).HasColumnName("position_no").IsRequired();
        entity.Property(x => x.ColumnNo).HasColumnName("column_no").IsRequired();
        entity.Property(x => x.RowNo).HasColumnName("row_no").IsRequired();
        entity.Property(x => x.ScannerChannelNo).HasColumnName("scanner_channel_no").IsRequired();
        entity.Property(x => x.ScannerChannelCode).HasColumnName("scanner_channel_code").HasMaxLength(8).IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.PositionNo).IsUnique();
        entity.HasIndex(x => new { x.ColumnNo, x.RowNo }).IsUnique();
    }

    private static void ConfigureDabMixPosition(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DabMixPosition>();
        entity.ToTable("dab_mix_positions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(8).IsRequired();
        entity.Property(x => x.PositionNo).HasColumnName("position_no").IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasDefaultValue(DabMixPositionStatus.Available).IsRequired();
        entity.Property(x => x.ActiveDabBatchId).HasColumnName("active_dab_batch_id").HasMaxLength(36);
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.PositionNo).IsUnique();
        entity.HasIndex(x => x.ActiveDabBatchId).IsUnique();
    }

    private static void ConfigureWashPosition(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WashPosition>();
        entity.ToTable("wash_positions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(x => x.WashType).HasColumnName("wash_type").HasMaxLength(32).IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
    }

    private static void ConfigureDeviceProfile(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DeviceProfile>();
        entity.ToTable("device_profiles");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        entity.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
    }

    private static void ConfigureCoordinateProfile(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CoordinateProfile>();
        entity.ToTable("coordinate_profiles");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        entity.Property(x => x.OriginDefinition).HasColumnName("origin_definition").HasMaxLength(512).IsRequired();
        entity.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        entity.Property(x => x.ActiveVersionId).HasColumnName("active_version_id").HasMaxLength(36);
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasOne(x => x.ActiveVersion).WithMany().HasForeignKey(x => x.ActiveVersionId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureCoordinateProfileVersion(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CoordinateProfileVersion>();
        entity.ToTable("coordinate_profile_versions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.CoordinateProfileId).HasColumnName("coordinate_profile_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.VersionNo).HasColumnName("version_no").IsRequired();
        entity.Property(x => x.VersionLabel).HasColumnName("version_label").HasMaxLength(64).IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        entity.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        entity.Property(x => x.UsageScope).HasColumnName("usage_scope").HasMaxLength(32).HasDefaultValue(CoordinateVersionUsageScope.MockOnly).IsRequired();
        entity.Property(x => x.VerificationStatus).HasColumnName("verification_status").HasMaxLength(32).HasDefaultValue(CoordinateVersionVerificationStatus.Unverified).IsRequired();
        entity.Property(x => x.SourceVersionId).HasColumnName("source_version_id").HasMaxLength(36);
        entity.Property(x => x.ChangeReason).HasColumnName("change_reason").HasMaxLength(2000).IsRequired();
        entity.Property(x => x.ChangeSummaryJson).HasColumnName("change_summary_json").HasMaxLength(40000).IsRequired();
        entity.Property(x => x.ValidationResultJson).HasColumnName("validation_result_json").HasMaxLength(40000).IsRequired();
        entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(36);
        entity.Property(x => x.PublishedByUserId).HasColumnName("published_by_user_id").HasMaxLength(36);
        entity.Property(x => x.ActivatedByUserId).HasColumnName("activated_by_user_id").HasMaxLength(36);
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        entity.Property(x => x.ActivatedAtUtc).HasColumnName("activated_at_utc");
        entity.Property(x => x.RetiredAtUtc).HasColumnName("retired_at_utc");
        entity.HasIndex(x => new { x.CoordinateProfileId, x.VersionNo }).IsUnique();
        entity.HasIndex(x => new { x.CoordinateProfileId, x.VersionLabel }).IsUnique();
        entity.HasIndex(x => x.CoordinateProfileId)
            .IsUnique()
            .HasFilter("is_active = 1")
            .HasDatabaseName("UX_coordinate_profile_versions_profile_active");
        entity.HasOne(x => x.CoordinateProfile).WithMany(x => x.Versions).HasForeignKey(x => x.CoordinateProfileId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.SourceVersion).WithMany(x => x.DerivedVersions).HasForeignKey(x => x.SourceVersionId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.PublishedByUser).WithMany().HasForeignKey(x => x.PublishedByUserId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.ActivatedByUser).WithMany().HasForeignKey(x => x.ActivatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureCoordinatePoint(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CoordinatePoint>();
        entity.ToTable("coordinate_points");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.CoordinateProfileId).HasColumnName("coordinate_profile_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.CoordinateProfileVersionId).HasColumnName("coordinate_profile_version_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.PointCode).HasColumnName("point_code").HasMaxLength(128).IsRequired();
        entity.Property(x => x.PointType).HasColumnName("point_type").HasMaxLength(64).IsRequired();
        entity.Property(x => x.PresetXUm).HasColumnName("preset_x_um");
        entity.Property(x => x.PresetYUm).HasColumnName("preset_y_um");
        entity.Property(x => x.CalibratedXUm).HasColumnName("calibrated_x_um");
        entity.Property(x => x.CalibratedYUm).HasColumnName("calibrated_y_um");
        entity.Property(x => x.CalibratedZUm).HasColumnName("calibrated_z_um");
        entity.Property(x => x.SafeZUm).HasColumnName("safe_z_um");
        entity.Property(x => x.LiquidDetectZUm).HasColumnName("liquid_detect_z_um");
        entity.Property(x => x.DispenseZUm).HasColumnName("dispense_z_um");
        entity.Property(x => x.ActionOffsetXUm).HasColumnName("action_offset_x_um");
        entity.Property(x => x.ActionOffsetYUm).HasColumnName("action_offset_y_um");
        entity.Property(x => x.ActionOffsetZUm).HasColumnName("action_offset_z_um");
        entity.Property(x => x.RequiresCalibration).HasColumnName("requires_calibration").IsRequired();
        entity.Property(x => x.ValidationStatus).HasColumnName("validation_status").HasMaxLength(64).HasDefaultValue(CoordinateTargetPointValidationStatus.Unverified).IsRequired();
        entity.Property(x => x.ValidationMessage).HasColumnName("validation_message").HasMaxLength(2000).HasDefaultValue(string.Empty).IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.Ignore(x => x.AspirateZUm);
        entity.HasIndex(x => new { x.CoordinateProfileId, x.PointCode });
        entity.HasIndex(x => new { x.CoordinateProfileVersionId, x.PointCode }).IsUnique();
        entity.HasOne(x => x.CoordinateProfile).WithMany(x => x.CoordinatePoints).HasForeignKey(x => x.CoordinateProfileId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.CoordinateProfileVersion).WithMany(x => x.TargetPoints).HasForeignKey(x => x.CoordinateProfileVersionId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureCoordinateCalibrationHistory(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CoordinateCalibrationHistory>();
        entity.ToTable("coordinate_calibration_history");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.CoordinatePointId).HasColumnName("coordinate_point_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.CoordinateProfileVersionId).HasColumnName("coordinate_profile_version_id").HasMaxLength(36);
        entity.Property(x => x.SourceCoordinateProfileVersionId).HasColumnName("source_coordinate_profile_version_id").HasMaxLength(36);
        entity.Property(x => x.PreviousXUm).HasColumnName("previous_x_um");
        entity.Property(x => x.PreviousYUm).HasColumnName("previous_y_um");
        entity.Property(x => x.NewXUm).HasColumnName("new_x_um");
        entity.Property(x => x.NewYUm).HasColumnName("new_y_um");
        entity.Property(x => x.NewZUm).HasColumnName("new_z_um");
        entity.Property(x => x.SafeZUm).HasColumnName("safe_z_um");
        entity.Property(x => x.LiquidDetectZUm).HasColumnName("liquid_detect_z_um");
        entity.Property(x => x.DispenseZUm).HasColumnName("dispense_z_um");
        entity.Property(x => x.ActionOffsetXUm).HasColumnName("action_offset_x_um");
        entity.Property(x => x.ActionOffsetYUm).HasColumnName("action_offset_y_um");
        entity.Property(x => x.ActionOffsetZUm).HasColumnName("action_offset_z_um");
        entity.Property(x => x.ChangeSummaryJson).HasColumnName("change_summary_json").HasMaxLength(40000).IsRequired();
        entity.Property(x => x.ValidationResultJson).HasColumnName("validation_result_json").HasMaxLength(40000).IsRequired();
        entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        entity.Property(x => x.CalibratedByUserId).HasColumnName("calibrated_by_user_id").HasMaxLength(36);
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Ignore(x => x.AspirateZUm);
        entity.HasOne(x => x.CoordinatePoint).WithMany(x => x.CalibrationHistory).HasForeignKey(x => x.CoordinatePointId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.CoordinateProfileVersion).WithMany().HasForeignKey(x => x.CoordinateProfileVersionId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.SourceCoordinateProfileVersion).WithMany().HasForeignKey(x => x.SourceCoordinateProfileVersionId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.CalibratedByUser).WithMany().HasForeignKey(x => x.CalibratedByUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureWorkflowDefinition(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkflowDefinition>();
        entity.ToTable("workflow_definitions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        entity.Property(x => x.WorkflowType).HasColumnName("workflow_type").HasMaxLength(64).IsRequired();
        entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000).IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.WorkflowType);
    }

    private static void ConfigureWorkflowVersion(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkflowVersion>();
        entity.ToTable("workflow_versions", table =>
        {
            table.HasCheckConstraint(
                "ck_workflow_versions_status",
                $"status in ('{WorkflowVersionStatus.Draft}', '{WorkflowVersionStatus.Published}', '{WorkflowVersionStatus.Retired}')");
            table.HasCheckConstraint(
                "ck_workflow_versions_default_experiment_type",
                $"default_experiment_type is null or (default_experiment_type in ('{StainingTaskType.He}', '{StainingTaskType.Ihc}') and status = '{WorkflowVersionStatus.Published}')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.WorkflowDefinitionId).HasColumnName("workflow_definition_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.VersionNo).HasColumnName("version_no").IsRequired();
        entity.Property(x => x.VersionLabel).HasColumnName("version_label").HasMaxLength(64).IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(x => x.ChangeNote).HasColumnName("change_note").HasMaxLength(2000).IsRequired();
        entity.Property(x => x.DefaultExperimentType).HasColumnName("default_experiment_type").HasMaxLength(8);
        entity.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        entity.Property(x => x.RetiredAtUtc).HasColumnName("retired_at_utc");
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => new { x.WorkflowDefinitionId, x.VersionNo }).IsUnique();
        entity.HasIndex(x => new { x.WorkflowDefinitionId, x.VersionLabel }).IsUnique();
        entity.HasIndex(x => x.Status);
        entity.HasIndex(x => x.DefaultExperimentType)
            .IsUnique()
            .HasFilter("default_experiment_type IS NOT NULL");
        entity.HasOne(x => x.WorkflowDefinition).WithMany(x => x.Versions).HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureWorkflowStep(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkflowStep>();
        entity.ToTable("workflow_steps");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.WorkflowVersionId).HasColumnName("workflow_version_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.StepNo).HasColumnName("step_no").IsRequired();
        entity.Property(x => x.MajorStepCode).HasColumnName("major_step_code").HasMaxLength(128).IsRequired();
        entity.Property(x => x.StepName).HasColumnName("step_name").HasMaxLength(512).IsRequired();
        entity.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(128).IsRequired();
        entity.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64);
        entity.Property(x => x.VolumeUl).HasColumnName("volume_ul");
        entity.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
        entity.Property(x => x.TargetTemperatureDeciC).HasColumnName("target_temperature_deci_c");
        entity.Property(x => x.MixParametersJson).HasColumnName("mix_parameters_json").HasMaxLength(4000).IsRequired();
        entity.Property(x => x.WashParametersJson).HasColumnName("wash_parameters_json").HasMaxLength(4000).IsRequired();
        entity.Property(x => x.LegacyParametersJson).HasColumnName("legacy_parameters_json").HasMaxLength(8000).IsRequired();
        entity.Property(x => x.FailureStrategy).HasColumnName("failure_strategy").HasMaxLength(128).IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => new { x.WorkflowVersionId, x.StepNo }).IsUnique();
        entity.HasIndex(x => x.ReagentCode);
        entity.HasOne(x => x.WorkflowVersion).WithMany(x => x.Steps).HasForeignKey(x => x.WorkflowVersionId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureWorkflowReagentRequirement(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkflowReagentRequirement>();
        entity.ToTable("workflow_reagent_requirements");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.WorkflowVersionId).HasColumnName("workflow_version_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64).IsRequired();
        entity.Property(x => x.RequiredVolumeUl).HasColumnName("required_volume_ul");
        entity.Property(x => x.IsRequired).HasColumnName("is_required").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => new { x.WorkflowVersionId, x.ReagentCode }).IsUnique();
        entity.HasIndex(x => x.ReagentCode);
        entity.HasOne(x => x.WorkflowVersion).WithMany(x => x.ReagentRequirements).HasForeignKey(x => x.WorkflowVersionId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigurePrimaryAntibodyWorkflowMapping(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PrimaryAntibodyWorkflowMapping>();
        entity.ToTable("primary_antibody_workflow_mappings");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.PrimaryAntibodyCode).HasColumnName("primary_antibody_code").HasMaxLength(64).IsRequired();
        entity.Property(x => x.WorkflowVersionId).HasColumnName("workflow_version_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => new { x.PrimaryAntibodyCode, x.WorkflowVersionId }).IsUnique();
        entity.HasIndex(x => x.PrimaryAntibodyCode);
        entity.HasIndex(x => x.WorkflowVersionId);
        entity.HasOne(x => x.WorkflowVersion).WithMany(x => x.PrimaryAntibodyMappings).HasForeignKey(x => x.WorkflowVersionId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLiquidClassProfile(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LiquidClassProfile>();
        entity.ToTable("liquid_class_profiles");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        entity.Property(x => x.EnabledVersionId).HasColumnName("enabled_version_id").HasMaxLength(36);
        entity.Property(x => x.AspirateSpeedUlPerSecond).HasColumnName("aspirate_speed_ul_per_second");
        entity.Property(x => x.DispenseSpeedUlPerSecond).HasColumnName("dispense_speed_ul_per_second");
        entity.Property(x => x.LeadingAirGapUl).HasColumnName("leading_air_gap_ul");
        entity.Property(x => x.TrailingAirGapUl).HasColumnName("trailing_air_gap_ul");
        entity.Property(x => x.ExcessVolumeUl).HasColumnName("excess_volume_ul");
        entity.Property(x => x.PreWetCycles).HasColumnName("pre_wet_cycles");
        entity.Property(x => x.MixCycles).HasColumnName("mix_cycles");
        entity.Property(x => x.LegacyParametersJson).HasColumnName("legacy_parameters_json").HasMaxLength(4000).IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.EnabledVersionId).IsUnique();
        entity.HasOne(x => x.EnabledVersion).WithMany().HasForeignKey(x => x.EnabledVersionId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureLiquidClassVersion(ModelBuilder modelBuilder)
    {
        var version = modelBuilder.Entity<LiquidClassVersion>();
        version.ToTable("liquid_class_versions", table => table.HasCheckConstraint(
            "ck_liquid_class_versions_status",
            $"status in ('{LiquidClassVersionStatus.Draft}', '{LiquidClassVersionStatus.Published}', '{LiquidClassVersionStatus.Enabled}')"));
        version.HasKey(x => x.Id);
        version.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        version.Property(x => x.LiquidClassProfileId).HasColumnName("liquid_class_profile_id").HasMaxLength(36).IsRequired();
        version.Property(x => x.VersionNo).HasColumnName("version_no").IsRequired();
        version.Property(x => x.VersionLabel).HasColumnName("version_label").HasMaxLength(64).IsRequired();
        version.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        version.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        version.Property(x => x.SourceVersionId).HasColumnName("source_version_id").HasMaxLength(36);
        version.Property(x => x.ChangeReason).HasColumnName("change_reason").HasMaxLength(2000).IsRequired();
        version.Property(x => x.ChangeSummaryJson).HasColumnName("change_summary_json").HasMaxLength(16000).IsRequired();
        version.Property(x => x.LiquidDetectionEnabled).HasColumnName("liquid_detection_enabled").IsRequired();
        version.Property(x => x.LiquidDetectionSensitivityPercent).HasColumnName("liquid_detection_sensitivity_percent").IsRequired();
        version.Property(x => x.LiquidDetectionSpeedUmPerSecond).HasColumnName("liquid_detection_speed_um_per_second").IsRequired();
        version.Property(x => x.AspirateSpeedUlPerSecond).HasColumnName("aspirate_speed_ul_per_second").IsRequired();
        version.Property(x => x.AspirateDelayMs).HasColumnName("aspirate_delay_ms").IsRequired();
        version.Property(x => x.DispenseSpeedUlPerSecond).HasColumnName("dispense_speed_ul_per_second").IsRequired();
        version.Property(x => x.DispenseDelayMs).HasColumnName("dispense_delay_ms").IsRequired();
        version.Property(x => x.LeadingAirGapUl).HasColumnName("leading_air_gap_ul").IsRequired();
        version.Property(x => x.TrailingAirGapUl).HasColumnName("trailing_air_gap_ul").IsRequired();
        version.Property(x => x.BlowoutVolumeUl).HasColumnName("blowout_volume_ul").IsRequired();
        version.Property(x => x.BlowoutDelayMs).HasColumnName("blowout_delay_ms").IsRequired();
        version.Property(x => x.VolumeAdjustmentUl).HasColumnName("volume_adjustment_ul").IsRequired();
        version.Property(x => x.PreWetCycles).HasColumnName("pre_wet_cycles").IsRequired();
        version.Property(x => x.MixCycles).HasColumnName("mix_cycles").IsRequired();
        version.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(36);
        version.Property(x => x.PublishedByUserId).HasColumnName("published_by_user_id").HasMaxLength(36);
        version.Property(x => x.EnabledByUserId).HasColumnName("enabled_by_user_id").HasMaxLength(36);
        version.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        version.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        version.Property(x => x.EnabledAtUtc).HasColumnName("enabled_at_utc");
        version.HasIndex(x => new { x.LiquidClassProfileId, x.VersionNo }).IsUnique();
        version.HasIndex(x => x.LiquidClassProfileId).IsUnique().HasFilter($"status = '{LiquidClassVersionStatus.Enabled}'");
        version.HasIndex(x => x.SourceVersionId);
        version.HasOne(x => x.LiquidClassProfile).WithMany(x => x.Versions).HasForeignKey(x => x.LiquidClassProfileId).OnDelete(DeleteBehavior.Restrict);
        version.HasOne(x => x.SourceVersion).WithMany(x => x.DerivedVersions).HasForeignKey(x => x.SourceVersionId).OnDelete(DeleteBehavior.Restrict);
        version.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        version.HasOne(x => x.PublishedByUser).WithMany().HasForeignKey(x => x.PublishedByUserId).OnDelete(DeleteBehavior.SetNull);
        version.HasOne(x => x.EnabledByUser).WithMany().HasForeignKey(x => x.EnabledByUserId).OnDelete(DeleteBehavior.SetNull);

        var difference = modelBuilder.Entity<LiquidClassVersionDifference>();
        difference.ToTable("liquid_class_version_differences");
        difference.HasKey(x => x.Id);
        difference.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        difference.Property(x => x.LiquidClassVersionId).HasColumnName("liquid_class_version_id").HasMaxLength(36).IsRequired();
        difference.Property(x => x.ParameterName).HasColumnName("parameter_name").HasMaxLength(128).IsRequired();
        difference.Property(x => x.PreviousValue).HasColumnName("previous_value").HasMaxLength(512);
        difference.Property(x => x.NewValue).HasColumnName("new_value").HasMaxLength(512);
        difference.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(32).IsRequired();
        difference.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        difference.HasIndex(x => new { x.LiquidClassVersionId, x.ParameterName }).IsUnique();
        difference.HasOne(x => x.LiquidClassVersion).WithMany(x => x.Differences).HasForeignKey(x => x.LiquidClassVersionId).OnDelete(DeleteBehavior.Cascade);

        var validation = modelBuilder.Entity<LiquidClassValidationRecord>();
        validation.ToTable("liquid_class_validation_records");
        validation.HasKey(x => x.Id);
        validation.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        validation.Property(x => x.LiquidClassVersionId).HasColumnName("liquid_class_version_id").HasMaxLength(36).IsRequired();
        validation.Property(x => x.Stage).HasColumnName("stage").HasMaxLength(32).IsRequired();
        validation.Property(x => x.IsValid).HasColumnName("is_valid").IsRequired();
        validation.Property(x => x.ResultJson).HasColumnName("result_json").HasMaxLength(16000).IsRequired();
        validation.Property(x => x.ValidatedByUserId).HasColumnName("validated_by_user_id").HasMaxLength(36);
        validation.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        validation.HasIndex(x => new { x.LiquidClassVersionId, x.CreatedAtUtc });
        validation.HasOne(x => x.LiquidClassVersion).WithMany(x => x.ValidationRecords).HasForeignKey(x => x.LiquidClassVersionId).OnDelete(DeleteBehavior.Cascade);
        validation.HasOne(x => x.ValidatedByUser).WithMany().HasForeignKey(x => x.ValidatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureReagentDefinition(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReagentDefinition>();
        entity.ToTable("reagent_definitions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64).IsRequired();
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        entity.Property(x => x.ReagentType).HasColumnName("reagent_type").HasMaxLength(64).IsRequired();
        entity.Property(x => x.LiquidClassProfileId).HasColumnName("liquid_class_profile_id").HasMaxLength(36);
        entity.Property(x => x.MinimumAlarmVolumeUl).HasColumnName("minimum_alarm_volume_ul");
        entity.Property(x => x.LegacyMetadataJson).HasColumnName("legacy_metadata_json").HasMaxLength(4000).IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => x.ReagentCode).IsUnique();
        entity.HasOne(x => x.LiquidClassProfile).WithMany(x => x.ReagentDefinitions).HasForeignKey(x => x.LiquidClassProfileId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureReagentBottle(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReagentBottle>();
        entity.ToTable("reagent_bottles");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.ReagentDefinitionId).HasColumnName("reagent_definition_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.FullBarcode).HasColumnName("full_barcode").HasMaxLength(64).IsRequired();
        entity.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64).IsRequired();
        entity.Property(x => x.ProductionBatchNo).HasColumnName("production_batch_no").HasMaxLength(32).IsRequired();
        entity.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(32).IsRequired();
        entity.Property(x => x.InitialVolumeUl).HasColumnName("initial_volume_ul").IsRequired();
        entity.Property(x => x.RemainingVolumeUl).HasColumnName("remaining_volume_ul").IsRequired();
        entity.Property(x => x.ExpirationDate).HasColumnName("expiration_date").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        entity.Property(x => x.FirstScannedAtUtc).HasColumnName("first_scanned_at_utc");
        entity.Property(x => x.LastScannedAtUtc).HasColumnName("last_scanned_at_utc");
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => x.FullBarcode).IsUnique();
        entity.HasIndex(x => new { x.ReagentCode, x.ProductionBatchNo, x.SerialNo }).IsUnique();
        entity.HasIndex(x => x.ReagentDefinitionId);
        entity.HasOne(x => x.ReagentDefinition).WithMany(x => x.Bottles).HasForeignKey(x => x.ReagentDefinitionId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureReagentRackPlacement(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReagentRackPlacement>();
        entity.ToTable("reagent_rack_placements");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.ReagentBottleId).HasColumnName("reagent_bottle_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.ReagentRackPositionId).HasColumnName("reagent_rack_position_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.ReagentScanSessionId).HasColumnName("reagent_scan_session_id").HasMaxLength(36);
        entity.Property(x => x.PlacedAtUtc).HasColumnName("placed_at_utc").IsRequired();
        entity.Property(x => x.RemovedAtUtc).HasColumnName("removed_at_utc");
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.ReagentBottleId).IsUnique().HasFilter("removed_at_utc IS NULL");
        entity.HasIndex(x => x.ReagentRackPositionId).IsUnique().HasFilter("removed_at_utc IS NULL");
        entity.HasIndex(x => new { x.ReagentBottleId, x.PlacedAtUtc });
        entity.HasOne(x => x.ReagentBottle).WithMany(x => x.Placements).HasForeignKey(x => x.ReagentBottleId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.ReagentRackPosition).WithMany().HasForeignKey(x => x.ReagentRackPositionId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.ReagentScanSession).WithMany(x => x.Placements).HasForeignKey(x => x.ReagentScanSessionId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureReagentScanSession(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReagentScanSession>();
        entity.ToTable("reagent_scan_sessions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.SessionCode).HasColumnName("session_code").HasMaxLength(128).IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        entity.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        entity.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(36);
        entity.HasIndex(x => x.SessionCode).IsUnique();
        entity.HasIndex(x => x.StartedAtUtc);
        entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureReagentScanItem(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReagentScanItem>();
        entity.ToTable("reagent_scan_items", table =>
        {
            table.HasCheckConstraint(
                "ck_reagent_scan_items_scan_result",
                $"scan_result in ('{ReagentScanResult.Empty}', '{ReagentScanResult.Valid}', '{ReagentScanResult.Invalid}')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.ReagentScanSessionId).HasColumnName("reagent_scan_session_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.ReagentRackPositionId).HasColumnName("reagent_rack_position_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.ScannerChannelNo).HasColumnName("scanner_channel_no").IsRequired();
        entity.Property(x => x.ScannerChannelCode).HasColumnName("scanner_channel_code").HasMaxLength(16).IsRequired();
        entity.Property(x => x.LocatorCode).HasColumnName("locator_code").HasMaxLength(64);
        entity.Property(x => x.ScanResult).HasColumnName("scan_result").HasMaxLength(16).IsRequired();
        entity.Property(x => x.RawBarcode).HasColumnName("raw_barcode").HasMaxLength(128);
        entity.Property(x => x.ParsedReagentCode).HasColumnName("parsed_reagent_code").HasMaxLength(64);
        entity.Property(x => x.ParsedQuantityUl).HasColumnName("parsed_quantity_ul");
        entity.Property(x => x.ParsedBatchNo).HasColumnName("parsed_batch_no").HasMaxLength(32);
        entity.Property(x => x.ParsedSerialNo).HasColumnName("parsed_serial_no").HasMaxLength(32);
        entity.Property(x => x.IsValidationPassed).HasColumnName("is_validation_passed").IsRequired();
        entity.Property(x => x.ValidationMessage).HasColumnName("validation_message").HasMaxLength(2000).IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => new { x.ReagentScanSessionId, x.ReagentRackPositionId }).IsUnique();
        entity.HasIndex(x => x.ScanResult);
        entity.HasIndex(x => x.RawBarcode);
        entity.HasIndex(x => x.ParsedReagentCode);
        entity.HasOne(x => x.ReagentScanSession).WithMany(x => x.Items).HasForeignKey(x => x.ReagentScanSessionId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.ReagentRackPosition).WithMany().HasForeignKey(x => x.ReagentRackPositionId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureLegacyImportRun(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LegacyImportRun>();
        entity.ToTable("legacy_import_runs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.ImportedAtUtc).HasColumnName("imported_at_utc").IsRequired();
        entity.Property(x => x.SourcePath).HasColumnName("source_path").HasMaxLength(1024).IsRequired();
        entity.Property(x => x.SourceHashJson).HasColumnName("source_hash_json").HasMaxLength(8000).IsRequired();
        entity.Property(x => x.IsDryRun).HasColumnName("is_dry_run").IsRequired();
        entity.Property(x => x.Result).HasColumnName("result").HasMaxLength(64).IsRequired();
        entity.Property(x => x.StatisticsJson).HasColumnName("statistics_json").HasMaxLength(8000).IsRequired();
        entity.Property(x => x.ReportPath).HasColumnName("report_path").HasMaxLength(1024);
        entity.HasIndex(x => x.ImportedAtUtc);
        entity.HasIndex(x => new { x.SourcePath, x.ImportedAtUtc });
    }

    private static void ConfigureLegacyImportIssue(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LegacyImportIssue>();
        entity.ToTable("legacy_import_issues");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.LegacyImportRunId).HasColumnName("legacy_import_run_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.FilePath).HasColumnName("file_path").HasMaxLength(1024).IsRequired();
        entity.Property(x => x.RecordIdentifier).HasColumnName("record_identifier").HasMaxLength(256);
        entity.Property(x => x.FieldName).HasColumnName("field_name").HasMaxLength(256);
        entity.Property(x => x.IssueType).HasColumnName("issue_type").HasMaxLength(128).IsRequired();
        entity.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000).IsRequired();
        entity.Property(x => x.RawFragment).HasColumnName("raw_fragment").HasMaxLength(8000);
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.LegacyImportRunId);
        entity.HasIndex(x => x.IssueType);
        entity.HasOne(x => x.LegacyImportRun).WithMany(x => x.Issues).HasForeignKey(x => x.LegacyImportRunId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLegacyRuntimeSnapshot(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LegacyRuntimeSnapshot>();
        entity.ToTable("legacy_runtime_snapshots");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.LegacyImportRunId).HasColumnName("legacy_import_run_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.SourceFilePath).HasColumnName("source_file_path").HasMaxLength(1024).IsRequired();
        entity.Property(x => x.SourceFileHash).HasColumnName("source_file_hash").HasMaxLength(128).IsRequired();
        entity.Property(x => x.RunId).HasColumnName("run_id").HasMaxLength(128);
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(64);
        entity.Property(x => x.CapturedAtUtc).HasColumnName("captured_at_utc").IsRequired();
        entity.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasMaxLength(40000).IsRequired();
        entity.HasIndex(x => x.SourceFileHash).IsUnique();
        entity.HasOne(x => x.LegacyImportRun).WithMany(x => x.RuntimeSnapshots).HasForeignKey(x => x.LegacyImportRunId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureCommandReceipt(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CommandReceipt>();
        entity.ToTable("command_receipts");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.CommandId).HasColumnName("command_id").HasMaxLength(128).IsRequired();
        entity.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(128).IsRequired();
        entity.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(128).IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        entity.Property(x => x.ResponseJson).HasColumnName("response_json").HasMaxLength(40000).IsRequired();
        entity.Property(x => x.ActorUserId).HasColumnName("actor_user_id").HasMaxLength(36);
        entity.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(128);
        entity.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(128);
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        entity.HasIndex(x => x.CommandId).IsUnique();
        entity.HasIndex(x => new { x.Operation, x.CreatedAtUtc });
        entity.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureStainingTask(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<StainingTask>();
        entity.ToTable("staining_tasks", table =>
        {
            table.HasCheckConstraint(
                "ck_staining_tasks_task_type",
                $"task_type in ('{StainingTaskType.He}', '{StainingTaskType.Ihc}')");
            table.HasCheckConstraint(
                "ck_staining_tasks_status",
                $"status in ('{StainingTaskStatus.Confirmed}', '{StainingTaskStatus.Cancelled}', '{StainingTaskStatus.Completed}')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.TaskCode).HasColumnName("task_code").HasMaxLength(64).IsRequired();
        entity.Property(x => x.TaskType).HasColumnName("task_type").HasMaxLength(16).IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(x => x.PhysicalSlotId).HasColumnName("physical_slot_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.WorkflowDefinitionId).HasColumnName("workflow_definition_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.WorkflowVersionId).HasColumnName("workflow_version_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.WorkflowSnapshotJson).HasColumnName("workflow_snapshot_json").HasMaxLength(40000).IsRequired();
        entity.Property(x => x.InputMode).HasColumnName("input_mode").HasMaxLength(64);
        entity.Property(x => x.RawCode).HasColumnName("raw_code").HasMaxLength(512);
        entity.Property(x => x.NormalizedCode).HasColumnName("normalized_code").HasMaxLength(512);
        entity.Property(x => x.PrimaryAntibodyCode).HasColumnName("primary_antibody_code").HasMaxLength(64);
        entity.Property(x => x.CandidateResultsJson).HasColumnName("candidate_results_json").HasMaxLength(40000).IsRequired();
        entity.Property(x => x.RawSampleCode).HasColumnName("raw_sample_code").HasMaxLength(512);
        entity.Property(x => x.NormalizedSampleCode).HasColumnName("normalized_sample_code").HasMaxLength(512);
        entity.Property(x => x.LisQueryLogId).HasColumnName("lis_query_log_id").HasMaxLength(128);
        entity.Property(x => x.LisCandidatePrimaryAntibodyCodesJson).HasColumnName("lis_candidate_primary_antibody_codes_json").HasMaxLength(40000);
        entity.Property(x => x.ConfirmedPrimaryAntibodyCode).HasColumnName("confirmed_primary_antibody_code").HasMaxLength(64);
        entity.Property(x => x.CompatibilityValidationStatus).HasColumnName("compatibility_validation_status").HasMaxLength(32);
        entity.Property(x => x.CompatibilityValidationMessage).HasColumnName("compatibility_validation_message").HasMaxLength(2000);
        entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(36);
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => x.TaskCode).IsUnique();
        entity.HasIndex(x => new { x.PhysicalSlotId, x.Status });
        entity.HasIndex(x => x.WorkflowVersionId);
        entity.HasIndex(x => x.PrimaryAntibodyCode);
        entity.HasIndex(x => x.ConfirmedPrimaryAntibodyCode);
        entity.HasIndex(x => x.CompatibilityValidationStatus);
        entity.HasOne(x => x.PhysicalSlot).WithMany().HasForeignKey(x => x.PhysicalSlotId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.WorkflowDefinition).WithMany().HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.WorkflowVersion).WithMany().HasForeignKey(x => x.WorkflowVersionId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureHospitalBarcodeMapping(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HospitalBarcodeMapping>();
        entity.ToTable("hospital_barcode_mappings");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.HospitalCode).HasColumnName("hospital_code").HasMaxLength(512).IsRequired();
        entity.Property(x => x.PrimaryAntibodyCode).HasColumnName("primary_antibody_code").HasMaxLength(64).IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => new { x.HospitalCode, x.PrimaryAntibodyCode }).IsUnique();
        entity.HasIndex(x => x.PrimaryAntibodyCode);
    }

    private static void ConfigureSampleScan(ModelBuilder modelBuilder)
    {
        var sessions = modelBuilder.Entity<SampleScanSession>();
        sessions.ToTable("sample_scan_sessions");
        sessions.HasKey(x => x.Id);
        sessions.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        sessions.Property(x => x.SessionCode).HasColumnName("session_code").HasMaxLength(128).IsRequired();
        sessions.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        sessions.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        sessions.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        sessions.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(36);
        sessions.HasIndex(x => x.SessionCode).IsUnique();
        sessions.HasIndex(x => x.StartedAtUtc);
        sessions.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);

        var items = modelBuilder.Entity<SampleScanItem>();
        items.ToTable("sample_scan_items");
        items.HasKey(x => x.Id);
        items.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        items.Property(x => x.SampleScanSessionId).HasColumnName("sample_scan_session_id").HasMaxLength(36).IsRequired();
        items.Property(x => x.SlotCode).HasColumnName("slot_code").HasMaxLength(32);
        items.Property(x => x.ScanKind).HasColumnName("scan_kind").HasMaxLength(64).IsRequired();
        items.Property(x => x.ScanStatus).HasColumnName("scan_status").HasMaxLength(64).IsRequired();
        items.Property(x => x.RawCode).HasColumnName("raw_code").HasMaxLength(512);
        items.Property(x => x.NormalizedCode).HasColumnName("normalized_code").HasMaxLength(512);
        items.Property(x => x.PrimaryAntibodyCode).HasColumnName("primary_antibody_code").HasMaxLength(64);
        items.Property(x => x.ErrorReason).HasColumnName("error_reason").HasMaxLength(2000);
        items.Property(x => x.DeviceStatus).HasColumnName("device_status").HasMaxLength(64).IsRequired();
        items.Property(x => x.ScannedAtUtc).HasColumnName("scanned_at_utc").IsRequired();
        items.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        items.HasIndex(x => x.SampleScanSessionId);
        items.HasIndex(x => x.ScanStatus);
        items.HasIndex(x => x.NormalizedCode);
        items.HasOne(x => x.SampleScanSession).WithMany(x => x.Items).HasForeignKey(x => x.SampleScanSessionId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLis(ModelBuilder modelBuilder)
    {
        var entries = modelBuilder.Entity<MockLisEntry>();
        entries.ToTable("mock_lis_entries");
        entries.HasKey(x => x.Id);
        entries.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entries.Property(x => x.NormalizedCode).HasColumnName("normalized_code").HasMaxLength(512).IsRequired();
        entries.Property(x => x.PrimaryAntibodyCode).HasColumnName("primary_antibody_code").HasMaxLength(64);
        entries.Property(x => x.Scenario).HasColumnName("scenario").HasMaxLength(64).IsRequired();
        entries.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entries.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasMaxLength(4000).IsRequired();
        entries.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entries.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entries.HasIndex(x => new { x.NormalizedCode, x.PrimaryAntibodyCode, x.Scenario }).IsUnique();
        entries.HasIndex(x => x.Scenario);

        var logs = modelBuilder.Entity<LisQueryLog>();
        logs.ToTable("lis_query_logs");
        logs.HasKey(x => x.Id);
        logs.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        logs.Property(x => x.Source).HasColumnName("source").HasMaxLength(64).IsRequired();
        logs.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        logs.Property(x => x.RawCode).HasColumnName("raw_code").HasMaxLength(512).IsRequired();
        logs.Property(x => x.NormalizedCode).HasColumnName("normalized_code").HasMaxLength(512).IsRequired();
        logs.Property(x => x.CandidatePrimaryAntibodyCodesJson).HasColumnName("candidate_primary_antibody_codes_json").HasMaxLength(40000).IsRequired();
        logs.Property(x => x.SelectedPrimaryAntibodyCode).HasColumnName("selected_primary_antibody_code").HasMaxLength(64);
        logs.Property(x => x.SelectedAtUtc).HasColumnName("selected_at_utc");
        logs.Property(x => x.SelectedByUserId).HasColumnName("selected_by_user_id").HasMaxLength(36);
        logs.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        logs.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        logs.Property(x => x.ExceptionJson).HasColumnName("exception_json").HasMaxLength(8000).IsRequired();
        logs.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        logs.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        logs.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        logs.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        logs.HasIndex(x => x.NormalizedCode);
        logs.HasIndex(x => x.Status);
        logs.HasIndex(x => x.StartedAtUtc);
        logs.HasOne(x => x.SelectedByUser).WithMany().HasForeignKey(x => x.SelectedByUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureMockDemoDataTag(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MockDemoDataTag>();
        entity.ToTable("mock_demo_data_tags");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(128).IsRequired();
        entity.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(128).IsRequired();
        entity.Property(x => x.DemoKey).HasColumnName("demo_key").HasMaxLength(128).IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => new { x.EntityType, x.EntityId }).IsUnique();
        entity.HasIndex(x => x.DemoKey);
    }

    private static void ConfigureRuntimeLedger(ModelBuilder modelBuilder)
    {
        var runs = modelBuilder.Entity<MachineRun>();
        runs.ToTable("machine_runs");
        runs.HasKey(x => x.Id);
        runs.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        runs.Property(x => x.RunCode).HasColumnName("run_code").HasMaxLength(64).IsRequired();
        runs.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        runs.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").HasMaxLength(36);
        runs.Property(x => x.PauseRequested).HasColumnName("pause_requested").IsRequired();
        runs.Property(x => x.StopRequested).HasColumnName("stop_requested").IsRequired();
        runs.Property(x => x.FaultMessage).HasColumnName("fault_message").HasMaxLength(2000);
        runs.Property(x => x.CurrentMajorStepCode).HasColumnName("current_major_step_code").HasMaxLength(128);
        runs.Property(x => x.CoordinateProfileVersionId).HasColumnName("coordinate_profile_version_id").HasMaxLength(36);
        runs.Property(x => x.CoordinateSnapshotJson).HasColumnName("coordinate_snapshot_json").HasMaxLength(40000).HasDefaultValue("{}").IsRequired();
        runs.Property(x => x.LiquidClassSnapshotJson).HasColumnName("liquid_class_snapshot_json").HasMaxLength(80000).HasDefaultValue("{}").IsRequired();
        runs.Property(x => x.LiquidClassSelectionStatus).HasColumnName("liquid_class_selection_status").HasMaxLength(32).HasDefaultValue(LiquidClassSelectionStatus.Unselected).IsRequired();
        runs.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        runs.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        runs.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        runs.HasIndex(x => x.RunCode).IsUnique();
        runs.HasIndex(x => x.Status);
        runs.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
        runs.HasOne(x => x.CoordinateProfileVersion).WithMany().HasForeignKey(x => x.CoordinateProfileVersionId).OnDelete(DeleteBehavior.Restrict);

        var batches = modelBuilder.Entity<ChannelBatch>();
        batches.ToTable("channel_batches", table =>
        {
            table.HasCheckConstraint(
                "ck_channel_batches_workflow_selection_status",
                $"workflow_selection_status in ('{WorkflowSelectionStatus.Unselected}', '{WorkflowSelectionStatus.Selected}', '{WorkflowSelectionStatus.Locked}', '{WorkflowSelectionStatus.NeedsManualResolution}')");
        });
        batches.HasKey(x => x.Id);
        batches.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        batches.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        batches.Property(x => x.DrawerId).HasColumnName("drawer_id").HasMaxLength(36).IsRequired();
        batches.Property(x => x.DrawerCode).HasColumnName("drawer_code").HasMaxLength(8).IsRequired();
        batches.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        batches.Property(x => x.ExperimentType).HasColumnName("experiment_type").HasMaxLength(16);
        batches.Property(x => x.SelectedWorkflowVersionId).HasColumnName("selected_workflow_version_id").HasMaxLength(36);
        batches.Property(x => x.WorkflowSnapshotJson).HasColumnName("workflow_snapshot_json").HasMaxLength(40000).IsRequired();
        batches.Property(x => x.CoordinateProfileVersionId).HasColumnName("coordinate_profile_version_id").HasMaxLength(36);
        batches.Property(x => x.CoordinateSnapshotJson).HasColumnName("coordinate_snapshot_json").HasMaxLength(40000).HasDefaultValue("{}").IsRequired();
        batches.Property(x => x.CoordinateSelectionStatus).HasColumnName("coordinate_selection_status").HasMaxLength(32).HasDefaultValue(CoordinateSelectionStatus.Unselected).IsRequired();
        batches.Property(x => x.LiquidClassSnapshotJson).HasColumnName("liquid_class_snapshot_json").HasMaxLength(80000).HasDefaultValue("{}").IsRequired();
        batches.Property(x => x.LiquidClassSelectionStatus).HasColumnName("liquid_class_selection_status").HasMaxLength(32).HasDefaultValue(LiquidClassSelectionStatus.Unselected).IsRequired();
        batches.Property(x => x.WorkflowSelectionStatus).HasColumnName("workflow_selection_status").HasMaxLength(32).IsRequired();
        batches.Property(x => x.NeedsManualResolution).HasColumnName("needs_manual_resolution").IsRequired();
        batches.Property(x => x.ManualResolutionReason).HasColumnName("manual_resolution_reason").HasMaxLength(2000).IsRequired();
        batches.Property(x => x.WorkflowSelectedAtUtc).HasColumnName("workflow_selected_at_utc");
        batches.Property(x => x.WorkflowSelectedByUserId).HasColumnName("workflow_selected_by_user_id").HasMaxLength(36);
        batches.Property(x => x.WorkflowLockedAtUtc).HasColumnName("workflow_locked_at_utc");
        batches.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        batches.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        batches.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        batches.HasIndex(x => new { x.MachineRunId, x.DrawerCode }).IsUnique();
        batches.HasIndex(x => x.SelectedWorkflowVersionId);
        batches.HasIndex(x => x.CoordinateProfileVersionId);
        batches.HasIndex(x => x.DrawerId)
            .IsUnique()
            .HasFilter($"status in ('{RuntimeLedgerStatus.Pending}', '{RuntimeLedgerStatus.Running}', '{RuntimeLedgerStatus.Paused}', '{RuntimeLedgerStatus.Faulted}')");
        batches.HasOne(x => x.MachineRun).WithMany(x => x.ChannelBatches).HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.SetNull);
        batches.HasOne(x => x.Drawer).WithMany().HasForeignKey(x => x.DrawerId).OnDelete(DeleteBehavior.Restrict);
        batches.HasOne(x => x.SelectedWorkflowVersion).WithMany().HasForeignKey(x => x.SelectedWorkflowVersionId).OnDelete(DeleteBehavior.Restrict);
        batches.HasOne(x => x.CoordinateProfileVersion).WithMany().HasForeignKey(x => x.CoordinateProfileVersionId).OnDelete(DeleteBehavior.Restrict);
        batches.HasOne(x => x.WorkflowSelectedByUser).WithMany().HasForeignKey(x => x.WorkflowSelectedByUserId).OnDelete(DeleteBehavior.SetNull);

        var histories = modelBuilder.Entity<WorkflowAssignmentHistory>();
        histories.ToTable("workflow_assignment_history");
        histories.HasKey(x => x.Id);
        histories.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        histories.Property(x => x.ChannelBatchId).HasColumnName("channel_batch_id").HasMaxLength(36).IsRequired();
        histories.Property(x => x.OldExperimentType).HasColumnName("old_experiment_type").HasMaxLength(16);
        histories.Property(x => x.OldWorkflowVersionId).HasColumnName("old_workflow_version_id").HasMaxLength(36);
        histories.Property(x => x.OldWorkflowSnapshotJson).HasColumnName("old_workflow_snapshot_json").HasMaxLength(40000);
        histories.Property(x => x.NewExperimentType).HasColumnName("new_experiment_type").HasMaxLength(16);
        histories.Property(x => x.NewWorkflowVersionId).HasColumnName("new_workflow_version_id").HasMaxLength(36);
        histories.Property(x => x.NewWorkflowSnapshotJson).HasColumnName("new_workflow_snapshot_json").HasMaxLength(40000);
        histories.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(64).IsRequired();
        histories.Property(x => x.ActorUserId).HasColumnName("actor_user_id").HasMaxLength(36);
        histories.Property(x => x.OperatorUserId).HasColumnName("operator_user_id").HasMaxLength(36);
        histories.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        histories.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2000).IsRequired();
        histories.Property(x => x.CommandId).HasColumnName("command_id").HasMaxLength(128);
        histories.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        histories.HasIndex(x => new { x.ChannelBatchId, x.CreatedAtUtc });
        histories.HasIndex(x => new { x.ChannelBatchId, x.ActionType, x.CreatedAtUtc });
        histories.HasIndex(x => x.ActionType);
        histories.HasIndex(x => x.CreatedAtUtc);
        histories.HasIndex(x => x.CommandId);
        histories.HasIndex(x => x.CorrelationId);
        histories.HasOne(x => x.ChannelBatch).WithMany(x => x.WorkflowAssignmentHistory).HasForeignKey(x => x.ChannelBatchId).OnDelete(DeleteBehavior.Restrict);
        histories.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
        histories.HasOne(x => x.OperatorUser).WithMany().HasForeignKey(x => x.OperatorUserId).OnDelete(DeleteBehavior.SetNull);

        var slides = modelBuilder.Entity<SlideTask>();
        slides.ToTable("slide_tasks");
        slides.HasKey(x => x.Id);
        slides.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        slides.Property(x => x.ChannelBatchId).HasColumnName("channel_batch_id").HasMaxLength(36).IsRequired();
        slides.Property(x => x.StainingTaskId).HasColumnName("staining_task_id").HasMaxLength(36).IsRequired();
        slides.Property(x => x.PhysicalSlotId).HasColumnName("physical_slot_id").HasMaxLength(36).IsRequired();
        slides.Property(x => x.SlotCode).HasColumnName("slot_code").HasMaxLength(16).IsRequired();
        slides.Property(x => x.TaskType).HasColumnName("task_type").HasMaxLength(16).IsRequired();
        slides.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        slides.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        slides.HasIndex(x => x.StainingTaskId).IsUnique();
        slides.HasIndex(x => x.PhysicalSlotId)
            .IsUnique()
            .HasFilter($"status in ('{RuntimeLedgerStatus.Pending}', '{RuntimeLedgerStatus.Running}', '{RuntimeLedgerStatus.Paused}', '{RuntimeLedgerStatus.Faulted}', '{RuntimeLedgerStatus.WaitingUnload}')");
        slides.HasOne(x => x.ChannelBatch).WithMany(x => x.SlideTasks).HasForeignKey(x => x.ChannelBatchId).OnDelete(DeleteBehavior.Cascade);
        slides.HasOne(x => x.StainingTask).WithMany().HasForeignKey(x => x.StainingTaskId).OnDelete(DeleteBehavior.Restrict);
        slides.HasOne(x => x.PhysicalSlot).WithMany().HasForeignKey(x => x.PhysicalSlotId).OnDelete(DeleteBehavior.Restrict);

        var executions = modelBuilder.Entity<WorkflowExecution>();
        executions.ToTable("workflow_executions");
        executions.HasKey(x => x.Id);
        executions.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        executions.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36).IsRequired();
        executions.Property(x => x.SlideTaskId).HasColumnName("slide_task_id").HasMaxLength(36).IsRequired();
        executions.Property(x => x.WorkflowVersionId).HasColumnName("workflow_version_id").HasMaxLength(36).IsRequired();
        executions.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        executions.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        executions.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        executions.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        executions.HasIndex(x => x.MachineRunId);
        executions.HasOne(x => x.MachineRun).WithMany(x => x.WorkflowExecutions).HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        executions.HasOne(x => x.SlideTask).WithMany(x => x.WorkflowExecutions).HasForeignKey(x => x.SlideTaskId).OnDelete(DeleteBehavior.Cascade);
        executions.HasOne(x => x.WorkflowVersion).WithMany().HasForeignKey(x => x.WorkflowVersionId).OnDelete(DeleteBehavior.Restrict);

        var steps = modelBuilder.Entity<WorkflowStepExecution>();
        steps.ToTable("workflow_step_executions");
        steps.HasKey(x => x.Id);
        steps.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        steps.Property(x => x.WorkflowExecutionId).HasColumnName("workflow_execution_id").HasMaxLength(36).IsRequired();
        steps.Property(x => x.StepNo).HasColumnName("step_no").IsRequired();
        steps.Property(x => x.MajorStepCode).HasColumnName("major_step_code").HasMaxLength(128).IsRequired();
        steps.Property(x => x.StepName).HasColumnName("step_name").HasMaxLength(512).IsRequired();
        steps.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(128).IsRequired();
        steps.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64);
        steps.Property(x => x.VolumeUl).HasColumnName("volume_ul");
        steps.Property(x => x.TargetTemperatureDeciC).HasColumnName("target_temperature_deci_c");
        steps.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        steps.Property(x => x.RedoCount).HasColumnName("redo_count").IsRequired();
        steps.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        steps.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        steps.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        steps.HasIndex(x => new { x.WorkflowExecutionId, x.StepNo });
        steps.HasOne(x => x.WorkflowExecution).WithMany(x => x.StepExecutions).HasForeignKey(x => x.WorkflowExecutionId).OnDelete(DeleteBehavior.Cascade);

        var deviceCommands = modelBuilder.Entity<DeviceCommandExecution>();
        deviceCommands.ToTable("device_command_executions");
        deviceCommands.HasKey(x => x.Id);
        deviceCommands.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        deviceCommands.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36).IsRequired();
        deviceCommands.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36);
        deviceCommands.Property(x => x.CommandType).HasColumnName("command_type").HasMaxLength(128).IsRequired();
        deviceCommands.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        deviceCommands.Property(x => x.PayloadJson).HasColumnName("payload_json").HasMaxLength(8000).IsRequired();
        deviceCommands.Property(x => x.LiquidClassVersionId).HasColumnName("liquid_class_version_id").HasMaxLength(36);
        deviceCommands.Property(x => x.LiquidClassVersionNo).HasColumnName("liquid_class_version_no");
        deviceCommands.Property(x => x.LiquidClassParametersJson).HasColumnName("liquid_class_parameters_json").HasMaxLength(16000).HasDefaultValue("{}").IsRequired();
        deviceCommands.Property(x => x.LiquidClassSelectionStatus).HasColumnName("liquid_class_selection_status").HasMaxLength(32).HasDefaultValue(LiquidClassSelectionStatus.NotApplicable).IsRequired();
        deviceCommands.Property(x => x.ResultJson).HasColumnName("result_json").HasMaxLength(8000).IsRequired();
        deviceCommands.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        deviceCommands.Property(x => x.CommandSentAtUtc).HasColumnName("command_sent_at_utc");
        deviceCommands.Property(x => x.AcknowledgedAtUtc).HasColumnName("acknowledged_at_utc");
        deviceCommands.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        deviceCommands.HasIndex(x => x.MachineRunId);
        deviceCommands.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        deviceCommands.HasOne(x => x.WorkflowStepExecution).WithMany().HasForeignKey(x => x.WorkflowStepExecutionId).OnDelete(DeleteBehavior.SetNull);
        deviceCommands.HasOne(x => x.LiquidClassVersion).WithMany().HasForeignKey(x => x.LiquidClassVersionId).OnDelete(DeleteBehavior.Restrict);

        ConfigureRuntimeConsumables(modelBuilder);
        ConfigureRuntimeAlarms(modelBuilder);
    }

    private static void ConfigureRuntimeConsumables(ModelBuilder modelBuilder)
    {
        var reservations = modelBuilder.Entity<ReagentReservation>();
        reservations.ToTable("reagent_reservations");
        reservations.HasKey(x => x.Id);
        reservations.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        reservations.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        reservations.Property(x => x.DabBatchId).HasColumnName("dab_batch_id").HasMaxLength(36);
        reservations.Property(x => x.ReagentBottleId).HasColumnName("reagent_bottle_id").HasMaxLength(36);
        reservations.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64).IsRequired();
        reservations.Property(x => x.ReservationKind).HasColumnName("reservation_kind").HasMaxLength(32).HasDefaultValue(ReagentReservationKind.MachineRun).IsRequired();
        reservations.Property(x => x.SourceRole).HasColumnName("source_role").HasMaxLength(32).HasDefaultValue(string.Empty).IsRequired();
        reservations.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasDefaultValue(ReagentReservationStatus.Reserved).IsRequired();
        reservations.Property(x => x.CommandId).HasColumnName("command_id").HasMaxLength(128);
        reservations.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(36);
        reservations.Property(x => x.RequiredVolumeUl).HasColumnName("required_volume_ul").IsRequired();
        reservations.Property(x => x.ReservedVolumeUl).HasColumnName("reserved_volume_ul").IsRequired();
        reservations.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        reservations.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        reservations.HasIndex(x => new { x.MachineRunId, x.ReagentCode });
        reservations.HasIndex(x => new { x.DabBatchId, x.SourceRole, x.Status });
        reservations.HasIndex(x => new { x.ReagentBottleId, x.Status });
        reservations.HasIndex(x => x.CommandId);
        reservations.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        reservations.HasOne(x => x.DabBatch).WithMany(x => x.ReagentReservations).HasForeignKey(x => x.DabBatchId).OnDelete(DeleteBehavior.Cascade);
        reservations.HasOne(x => x.ReagentBottle).WithMany().HasForeignKey(x => x.ReagentBottleId).OnDelete(DeleteBehavior.Restrict);
        reservations.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);

        var consumptions = modelBuilder.Entity<ReagentConsumption>();
        consumptions.ToTable("reagent_consumptions");
        consumptions.HasKey(x => x.Id);
        consumptions.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        consumptions.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36).IsRequired();
        consumptions.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36).IsRequired();
        consumptions.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36);
        consumptions.Property(x => x.DabBatchId).HasColumnName("dab_batch_id").HasMaxLength(36);
        consumptions.Property(x => x.ReagentBottleId).HasColumnName("reagent_bottle_id").HasMaxLength(36).IsRequired();
        consumptions.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64).IsRequired();
        consumptions.Property(x => x.SourceRole).HasColumnName("source_role").HasMaxLength(32).HasDefaultValue(string.Empty).IsRequired();
        consumptions.Property(x => x.VolumeUl).HasColumnName("volume_ul").IsRequired();
        consumptions.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        consumptions.HasIndex(x => new { x.DabBatchId, x.DeviceCommandExecutionId });
        consumptions.HasIndex(x => x.DeviceCommandExecutionId);
        consumptions.HasIndex(x => new { x.DeviceCommandExecutionId, x.ReagentBottleId }).IsUnique().HasFilter("device_command_execution_id IS NOT NULL");
        consumptions.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        consumptions.HasOne(x => x.WorkflowStepExecution).WithMany().HasForeignKey(x => x.WorkflowStepExecutionId).OnDelete(DeleteBehavior.Cascade);
        consumptions.HasOne(x => x.DeviceCommandExecution).WithMany().HasForeignKey(x => x.DeviceCommandExecutionId).OnDelete(DeleteBehavior.SetNull);
        consumptions.HasOne(x => x.DabBatch).WithMany().HasForeignKey(x => x.DabBatchId).OnDelete(DeleteBehavior.SetNull);
        consumptions.HasOne(x => x.ReagentBottle).WithMany().HasForeignKey(x => x.ReagentBottleId).OnDelete(DeleteBehavior.Restrict);

        var systemLiquidUsages = modelBuilder.Entity<SystemLiquidUsage>();
        systemLiquidUsages.ToTable("system_liquid_usages");
        systemLiquidUsages.HasKey(x => x.Id);
        systemLiquidUsages.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        systemLiquidUsages.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36).IsRequired();
        systemLiquidUsages.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36).IsRequired();
        systemLiquidUsages.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36).IsRequired();
        systemLiquidUsages.Property(x => x.DabBatchId).HasColumnName("dab_batch_id").HasMaxLength(36).IsRequired();
        systemLiquidUsages.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).HasDefaultValue(SystemLiquidSourceTypes.SystemWater).IsRequired();
        systemLiquidUsages.Property(x => x.VolumeUl).HasColumnName("volume_ul").IsRequired();
        systemLiquidUsages.Property(x => x.LevelSnapshotJson).HasColumnName("level_snapshot_json").HasMaxLength(4000).IsRequired();
        systemLiquidUsages.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        systemLiquidUsages.HasIndex(x => new { x.DabBatchId, x.DeviceCommandExecutionId });
        systemLiquidUsages.HasIndex(x => new { x.MachineRunId, x.SourceType });
        systemLiquidUsages.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        systemLiquidUsages.HasOne(x => x.WorkflowStepExecution).WithMany().HasForeignKey(x => x.WorkflowStepExecutionId).OnDelete(DeleteBehavior.Cascade);
        systemLiquidUsages.HasOne(x => x.DeviceCommandExecution).WithMany().HasForeignKey(x => x.DeviceCommandExecutionId).OnDelete(DeleteBehavior.Cascade);
        systemLiquidUsages.HasOne(x => x.DabBatch).WithMany().HasForeignKey(x => x.DabBatchId).OnDelete(DeleteBehavior.Cascade);

        var dispenses = modelBuilder.Entity<DispenseExecution>();
        dispenses.ToTable("dispense_executions");
        dispenses.HasKey(x => x.Id);
        dispenses.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        dispenses.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36).IsRequired();
        dispenses.Property(x => x.ReagentBottleId).HasColumnName("reagent_bottle_id").HasMaxLength(36);
        dispenses.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64).IsRequired();
        dispenses.Property(x => x.VolumeUl).HasColumnName("volume_ul").IsRequired();
        dispenses.Property(x => x.SourcePositionCode).HasColumnName("source_position_code").HasMaxLength(64);
        dispenses.Property(x => x.TargetSlotCode).HasColumnName("target_slot_code").HasMaxLength(64);
        dispenses.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        dispenses.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        dispenses.HasIndex(x => new { x.DeviceCommandExecutionId, x.ReagentBottleId }).IsUnique().HasFilter("reagent_bottle_id IS NOT NULL");
        dispenses.HasOne(x => x.DeviceCommandExecution).WithMany().HasForeignKey(x => x.DeviceCommandExecutionId).OnDelete(DeleteBehavior.Cascade);
        dispenses.HasOne(x => x.ReagentBottle).WithMany().HasForeignKey(x => x.ReagentBottleId).OnDelete(DeleteBehavior.SetNull);

        var dabBatches = modelBuilder.Entity<DabBatch>();
        dabBatches.ToTable("dab_batches");
        dabBatches.HasKey(x => x.Id);
        dabBatches.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        dabBatches.Property(x => x.DabMixPositionId).HasColumnName("dab_mix_position_id").HasMaxLength(36).IsRequired();
        dabBatches.Property(x => x.PositionCode).HasColumnName("position_code").HasMaxLength(16).IsRequired();
        dabBatches.Property(x => x.DabAReagentBottleId).HasColumnName("dab_a_reagent_bottle_id").HasMaxLength(36);
        dabBatches.Property(x => x.DabBReagentBottleId).HasColumnName("dab_b_reagent_bottle_id").HasMaxLength(36);
        dabBatches.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(36);
        dabBatches.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        dabBatches.Property(x => x.CleaningStatus).HasColumnName("cleaning_status").HasMaxLength(32).HasDefaultValue(DabCleaningStatus.NotRequired).IsRequired();
        dabBatches.Property(x => x.SlideCount).HasColumnName("slide_count").IsRequired();
        dabBatches.Property(x => x.VolumePerSlideUl).HasColumnName("volume_per_slide_ul").HasDefaultValue(DabFormula.VolumePerSlideUl).IsRequired();
        dabBatches.Property(x => x.LineReserveVolumeUl).HasColumnName("line_reserve_volume_ul").HasDefaultValue(DabFormula.LineReserveVolumeUl).IsRequired();
        dabBatches.Property(x => x.DabARatioParts).HasColumnName("dab_a_ratio_parts").HasDefaultValue(DabFormula.DabARatioParts).IsRequired();
        dabBatches.Property(x => x.DabBRatioParts).HasColumnName("dab_b_ratio_parts").HasDefaultValue(DabFormula.DabBRatioParts).IsRequired();
        dabBatches.Property(x => x.WaterRatioParts).HasColumnName("water_ratio_parts").HasDefaultValue(DabFormula.WaterRatioParts).IsRequired();
        dabBatches.Property(x => x.TotalRequiredVolumeUl).HasColumnName("total_required_volume_ul").IsRequired();
        dabBatches.Property(x => x.ActualPreparedVolumeUl).HasColumnName("actual_prepared_volume_ul").IsRequired();
        dabBatches.Property(x => x.DabAVolumeUl).HasColumnName("dab_a_volume_ul").IsRequired();
        dabBatches.Property(x => x.DabBVolumeUl).HasColumnName("dab_b_volume_ul").IsRequired();
        dabBatches.Property(x => x.WaterVolumeUl).HasColumnName("water_volume_ul").IsRequired();
        dabBatches.Property(x => x.UsedVolumeUl).HasColumnName("used_volume_ul").IsRequired();
        dabBatches.Property(x => x.RemainingVolumeUl).HasColumnName("remaining_volume_ul").IsRequired();
        dabBatches.Property(x => x.PreparedAtUtc).HasColumnName("prepared_at_utc");
        dabBatches.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        dabBatches.Property(x => x.CleaningConfirmedAtUtc).HasColumnName("cleaning_confirmed_at_utc");
        dabBatches.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        dabBatches.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        dabBatches.HasIndex(x => new { x.PositionCode, x.Status });
        dabBatches.HasIndex(x => x.DabMixPositionId);
        dabBatches.HasOne(x => x.DabMixPosition).WithMany().HasForeignKey(x => x.DabMixPositionId).OnDelete(DeleteBehavior.Restrict);
        dabBatches.HasOne(x => x.DabAReagentBottle).WithMany().HasForeignKey(x => x.DabAReagentBottleId).OnDelete(DeleteBehavior.Restrict);
        dabBatches.HasOne(x => x.DabBReagentBottle).WithMany().HasForeignKey(x => x.DabBReagentBottleId).OnDelete(DeleteBehavior.Restrict);
        dabBatches.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);

        var dabTasks = modelBuilder.Entity<DabBatchTask>();
        dabTasks.ToTable("dab_batch_tasks");
        dabTasks.HasKey(x => x.Id);
        dabTasks.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        dabTasks.Property(x => x.DabBatchId).HasColumnName("dab_batch_id").HasMaxLength(36).IsRequired();
        dabTasks.Property(x => x.StainingTaskId).HasColumnName("staining_task_id").HasMaxLength(36).IsRequired();
        dabTasks.Property(x => x.RequiredVolumeUl).HasColumnName("required_volume_ul").IsRequired();
        dabTasks.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        dabTasks.HasIndex(x => new { x.DabBatchId, x.StainingTaskId }).IsUnique();
        dabTasks.HasIndex(x => x.StainingTaskId);
        dabTasks.HasOne(x => x.DabBatch).WithMany(x => x.Tasks).HasForeignKey(x => x.DabBatchId).OnDelete(DeleteBehavior.Cascade);
        dabTasks.HasOne(x => x.StainingTask).WithMany().HasForeignKey(x => x.StainingTaskId).OnDelete(DeleteBehavior.Restrict);

        var repreparationPlans = modelBuilder.Entity<DabRepreparationPlan>();
        repreparationPlans.ToTable("dab_repreparation_plans");
        repreparationPlans.HasKey(x => x.Id);
        repreparationPlans.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        repreparationPlans.Property(x => x.ExpiredDabBatchId).HasColumnName("expired_dab_batch_id").HasMaxLength(36).IsRequired();
        repreparationPlans.Property(x => x.ReplacementDabBatchId).HasColumnName("replacement_dab_batch_id").HasMaxLength(36);
        repreparationPlans.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36).IsRequired();
        repreparationPlans.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        repreparationPlans.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        repreparationPlans.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        repreparationPlans.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        repreparationPlans.HasIndex(x => new { x.ExpiredDabBatchId, x.MachineRunId }).IsUnique();
        repreparationPlans.HasIndex(x => x.ReplacementDabBatchId).IsUnique();
        repreparationPlans.HasOne(x => x.ExpiredDabBatch).WithMany(x => x.ExpiryRepreparationPlans).HasForeignKey(x => x.ExpiredDabBatchId).OnDelete(DeleteBehavior.Restrict);
        repreparationPlans.HasOne(x => x.ReplacementDabBatch).WithMany(x => x.ReplacementForPlans).HasForeignKey(x => x.ReplacementDabBatchId).OnDelete(DeleteBehavior.Restrict);
        repreparationPlans.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Restrict);

        var dabUsages = modelBuilder.Entity<DabBatchUsage>();
        dabUsages.ToTable("dab_batch_usages");
        dabUsages.HasKey(x => x.Id);
        dabUsages.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        dabUsages.Property(x => x.DabBatchId).HasColumnName("dab_batch_id").HasMaxLength(36).IsRequired();
        dabUsages.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        dabUsages.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36);
        dabUsages.Property(x => x.StainingTaskId).HasColumnName("staining_task_id").HasMaxLength(36);
        dabUsages.Property(x => x.CommandId).HasColumnName("command_id").HasMaxLength(128);
        dabUsages.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(36);
        dabUsages.Property(x => x.VolumeUl).HasColumnName("volume_ul").IsRequired();
        dabUsages.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        dabUsages.HasIndex(x => x.CommandId).IsUnique();
        dabUsages.HasOne(x => x.DabBatch).WithMany(x => x.Usages).HasForeignKey(x => x.DabBatchId).OnDelete(DeleteBehavior.Cascade);
        dabUsages.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        dabUsages.HasOne(x => x.WorkflowStepExecution).WithMany().HasForeignKey(x => x.WorkflowStepExecutionId).OnDelete(DeleteBehavior.Cascade);
        dabUsages.HasOne(x => x.StainingTask).WithMany().HasForeignKey(x => x.StainingTaskId).OnDelete(DeleteBehavior.Restrict);
        dabUsages.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureRuntimeAlarms(ModelBuilder modelBuilder)
    {
        var alarms = modelBuilder.Entity<Alarm>();
        alarms.ToTable("alarms");
        alarms.HasKey(x => x.Id);
        alarms.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        alarms.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        alarms.Property(x => x.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
        alarms.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(32).IsRequired();
        alarms.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000).IsRequired();
        alarms.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        alarms.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        alarms.Property(x => x.ClearedAtUtc).HasColumnName("cleared_at_utc");
        alarms.HasIndex(x => new { x.MachineRunId, x.Code, x.Status });
        alarms.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);

        var actions = modelBuilder.Entity<AlarmAction>();
        actions.ToTable("alarm_actions");
        actions.HasKey(x => x.Id);
        actions.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        actions.Property(x => x.AlarmId).HasColumnName("alarm_id").HasMaxLength(36).IsRequired();
        actions.Property(x => x.ActorUserId).HasColumnName("actor_user_id").HasMaxLength(36);
        actions.Property(x => x.Action).HasColumnName("action").HasMaxLength(128).IsRequired();
        actions.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000).IsRequired();
        actions.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        actions.HasOne(x => x.Alarm).WithMany(x => x.Actions).HasForeignKey(x => x.AlarmId).OnDelete(DeleteBehavior.Cascade);
        actions.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureDeviceInitialization(ModelBuilder modelBuilder)
    {
        var runs = modelBuilder.Entity<DeviceInitializationRun>();
        runs.ToTable("device_initialization_runs", table =>
        {
            table.HasCheckConstraint(
                "ck_device_initialization_runs_status",
                $"status in ('{DeviceInitializationStatus.Running}', '{DeviceInitializationStatus.Ready}', '{DeviceInitializationStatus.Failed}')");
        });
        runs.HasKey(x => x.Id);
        runs.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        runs.Property(x => x.CommandId).HasColumnName("command_id").HasMaxLength(128).IsRequired();
        runs.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        runs.Property(x => x.DeviceMode).HasColumnName("device_mode").HasMaxLength(16).IsRequired();
        runs.Property(x => x.AdapterName).HasColumnName("adapter_name").HasMaxLength(128).IsRequired();
        runs.Property(x => x.AttemptNo).HasColumnName("attempt_no").IsRequired();
        runs.Property(x => x.RetryOfRunId).HasColumnName("retry_of_run_id").HasMaxLength(36);
        runs.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").HasMaxLength(36);
        runs.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(128);
        runs.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000);
        runs.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        runs.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        runs.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        runs.HasIndex(x => x.CommandId).IsUnique();
        runs.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        runs.HasIndex(x => x.RetryOfRunId);
        runs.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
        runs.HasOne(x => x.RetryOfRun).WithMany(x => x.RetryRuns).HasForeignKey(x => x.RetryOfRunId).OnDelete(DeleteBehavior.Restrict);

        var checks = modelBuilder.Entity<DeviceInitializationCheck>();
        checks.ToTable("device_initialization_checks", table =>
        {
            table.HasCheckConstraint(
                "ck_device_initialization_checks_status",
                $"status in ('{DeviceInitializationCheckStatus.Pending}', '{DeviceInitializationCheckStatus.Running}', '{DeviceInitializationCheckStatus.Succeeded}', '{DeviceInitializationCheckStatus.Failed}', '{DeviceInitializationCheckStatus.TimedOut}', '{DeviceInitializationCheckStatus.Unknown}')");
        });
        checks.HasKey(x => x.Id);
        checks.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        checks.Property(x => x.DeviceInitializationRunId).HasColumnName("device_initialization_run_id").HasMaxLength(36).IsRequired();
        checks.Property(x => x.StepNo).HasColumnName("step_no").IsRequired();
        checks.Property(x => x.ModuleCode).HasColumnName("module_code").HasMaxLength(128).IsRequired();
        checks.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        checks.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        checks.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000).IsRequired();
        checks.Property(x => x.ResultJson).HasColumnName("result_json").HasMaxLength(8000).IsRequired();
        checks.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        checks.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        checks.HasIndex(x => new { x.DeviceInitializationRunId, x.StepNo }).IsUnique();
        checks.HasIndex(x => new { x.ModuleCode, x.Status });
        checks.HasOne(x => x.DeviceInitializationRun).WithMany(x => x.Checks).HasForeignKey(x => x.DeviceInitializationRunId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureThermalState(ModelBuilder modelBuilder)
    {
        var points = modelBuilder.Entity<ThermalPointState>();
        points.ToTable("thermal_point_states");
        points.HasKey(x => x.Id);
        points.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        points.Property(x => x.DrawerCode).HasColumnName("drawer_code").HasMaxLength(1).IsRequired();
        points.Property(x => x.BoardNo).HasColumnName("board_no").IsRequired();
        points.Property(x => x.SlotNo).HasColumnName("slot_no").IsRequired();
        points.Property(x => x.PointNo).HasColumnName("point_no").IsRequired();
        points.Property(x => x.CurrentTemperatureDeciC).HasColumnName("current_temperature_deci_c").IsRequired();
        points.Property(x => x.TargetTemperatureDeciC).HasColumnName("target_temperature_deci_c").IsRequired();
        points.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        points.Property(x => x.IsConnected).HasColumnName("is_connected").IsRequired();
        points.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        points.Property(x => x.FaultCode).HasColumnName("fault_code").HasMaxLength(128);
        points.Property(x => x.FaultMessage).HasColumnName("fault_message").HasMaxLength(2000);
        points.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        points.HasIndex(x => new { x.DrawerCode, x.SlotNo }).IsUnique();
        points.HasIndex(x => new { x.BoardNo, x.PointNo }).IsUnique();

        var cooling = modelBuilder.Entity<CoolingUnitState>();
        cooling.ToTable("cooling_unit_states");
        cooling.HasKey(x => x.Id);
        cooling.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        cooling.Property(x => x.CurrentTemperatureDeciC).HasColumnName("current_temperature_deci_c").IsRequired();
        cooling.Property(x => x.TargetTemperatureDeciC).HasColumnName("target_temperature_deci_c").IsRequired();
        cooling.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        cooling.Property(x => x.IsConnected).HasColumnName("is_connected").IsRequired();
        cooling.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        cooling.Property(x => x.FaultCode).HasColumnName("fault_code").HasMaxLength(128);
        cooling.Property(x => x.FaultMessage).HasColumnName("fault_message").HasMaxLength(2000);
        cooling.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        var telemetry = modelBuilder.Entity<TemperatureTelemetry>();
        telemetry.ToTable("temperature_telemetry");
        telemetry.HasKey(x => x.Id);
        telemetry.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        telemetry.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired();
        telemetry.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(36).IsRequired();
        telemetry.Property(x => x.DrawerCode).HasColumnName("drawer_code").HasMaxLength(1);
        telemetry.Property(x => x.BoardNo).HasColumnName("board_no");
        telemetry.Property(x => x.SlotNo).HasColumnName("slot_no");
        telemetry.Property(x => x.PointNo).HasColumnName("point_no");
        telemetry.Property(x => x.CurrentTemperatureDeciC).HasColumnName("current_temperature_deci_c").IsRequired();
        telemetry.Property(x => x.TargetTemperatureDeciC).HasColumnName("target_temperature_deci_c").IsRequired();
        telemetry.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        telemetry.Property(x => x.IsConnected).HasColumnName("is_connected").IsRequired();
        telemetry.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        telemetry.Property(x => x.FaultCode).HasColumnName("fault_code").HasMaxLength(128);
        telemetry.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired();
        telemetry.HasIndex(x => new { x.SourceType, x.SourceId, x.RecordedAtUtc });
        telemetry.HasIndex(x => x.RecordedAtUtc);
    }

    private static void ConfigureFluidicsState(ModelBuilder modelBuilder)
    {
        var pumps = modelBuilder.Entity<PumpChannelState>();
        pumps.ToTable("pump_channel_states");
        pumps.HasKey(x => x.Id);
        pumps.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        pumps.Property(x => x.PwmChannelCode).HasColumnName("pwm_channel_code").HasMaxLength(16).IsRequired();
        pumps.Property(x => x.PwmChannelNo).HasColumnName("pwm_channel_no").IsRequired();
        pumps.Property(x => x.DrawerCode).HasColumnName("drawer_code").HasMaxLength(1).IsRequired();
        pumps.Property(x => x.SpeedPercent).HasColumnName("speed_percent").IsRequired();
        pumps.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(32).IsRequired();
        pumps.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        pumps.Property(x => x.IsConnected).HasColumnName("is_connected").IsRequired();
        pumps.Property(x => x.TargetPointCode).HasColumnName("target_point_code").HasMaxLength(128);
        pumps.Property(x => x.DurationMs).HasColumnName("duration_ms");
        pumps.Property(x => x.CurrentCommandId).HasColumnName("current_command_id").HasMaxLength(128);
        pumps.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        pumps.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36);
        pumps.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36);
        pumps.Property(x => x.FaultCode).HasColumnName("fault_code").HasMaxLength(128);
        pumps.Property(x => x.FaultMessage).HasColumnName("fault_message").HasMaxLength(2000);
        pumps.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        pumps.HasIndex(x => x.PwmChannelCode).IsUnique();
        pumps.HasIndex(x => x.PwmChannelNo).IsUnique();
        pumps.HasIndex(x => x.DrawerCode).IsUnique();
        pumps.HasIndex(x => new { x.MachineRunId, x.WorkflowStepExecutionId });

        var mixers = modelBuilder.Entity<MixerChannelState>();
        mixers.ToTable("mixer_channel_states");
        mixers.HasKey(x => x.Id);
        mixers.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        mixers.Property(x => x.DrawerCode).HasColumnName("drawer_code").HasMaxLength(1).IsRequired();
        mixers.Property(x => x.ChannelNo).HasColumnName("channel_no").IsRequired();
        mixers.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        mixers.Property(x => x.IsConnected).HasColumnName("is_connected").IsRequired();
        mixers.Property(x => x.CurrentRoundKey).HasColumnName("current_round_key").HasMaxLength(256);
        mixers.Property(x => x.CurrentCommandId).HasColumnName("current_command_id").HasMaxLength(128);
        mixers.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        mixers.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36);
        mixers.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36);
        mixers.Property(x => x.FaultCode).HasColumnName("fault_code").HasMaxLength(128);
        mixers.Property(x => x.FaultMessage).HasColumnName("fault_message").HasMaxLength(2000);
        mixers.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        mixers.HasIndex(x => x.DrawerCode).IsUnique();
        mixers.HasIndex(x => x.ChannelNo).IsUnique();
        mixers.HasIndex(x => new { x.MachineRunId, x.WorkflowStepExecutionId });

        var liquids = modelBuilder.Entity<LiquidContainerState>();
        liquids.ToTable("liquid_container_states");
        liquids.HasKey(x => x.Id);
        liquids.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        liquids.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired();
        liquids.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        liquids.Property(x => x.IsWaste).HasColumnName("is_waste").IsRequired();
        liquids.Property(x => x.CapacityUl).HasColumnName("capacity_ul").IsRequired();
        liquids.Property(x => x.CurrentVolumeUl).HasColumnName("current_volume_ul").IsRequired();
        liquids.Property(x => x.LowThresholdUl).HasColumnName("low_threshold_ul").IsRequired();
        liquids.Property(x => x.FullThresholdUl).HasColumnName("full_threshold_ul").IsRequired();
        liquids.Property(x => x.LevelStatus).HasColumnName("level_status").HasMaxLength(32).IsRequired();
        liquids.Property(x => x.IsConnected).HasColumnName("is_connected").IsRequired();
        liquids.Property(x => x.FaultCode).HasColumnName("fault_code").HasMaxLength(128);
        liquids.Property(x => x.FaultMessage).HasColumnName("fault_message").HasMaxLength(2000);
        liquids.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        liquids.HasIndex(x => x.SourceType).IsUnique();

        var telemetry = modelBuilder.Entity<FluidicsTelemetry>();
        telemetry.ToTable("fluidics_telemetry");
        telemetry.HasKey(x => x.Id);
        telemetry.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        telemetry.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired();
        telemetry.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(36).IsRequired();
        telemetry.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        telemetry.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        telemetry.Property(x => x.PwmChannelCode).HasColumnName("pwm_channel_code").HasMaxLength(16);
        telemetry.Property(x => x.DrawerCode).HasColumnName("drawer_code").HasMaxLength(1);
        telemetry.Property(x => x.LiquidSourceType).HasColumnName("liquid_source_type").HasMaxLength(64);
        telemetry.Property(x => x.SpeedPercent).HasColumnName("speed_percent");
        telemetry.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(32);
        telemetry.Property(x => x.CurrentVolumeUl).HasColumnName("current_volume_ul");
        telemetry.Property(x => x.CapacityUl).HasColumnName("capacity_ul");
        telemetry.Property(x => x.TargetPointCode).HasColumnName("target_point_code").HasMaxLength(128);
        telemetry.Property(x => x.CommandId).HasColumnName("command_id").HasMaxLength(128);
        telemetry.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        telemetry.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36);
        telemetry.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36);
        telemetry.Property(x => x.FaultCode).HasColumnName("fault_code").HasMaxLength(128);
        telemetry.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired();
        telemetry.HasIndex(x => new { x.SourceType, x.SourceId, x.RecordedAtUtc });
        telemetry.HasIndex(x => x.RecordedAtUtc);
        telemetry.HasIndex(x => new { x.MachineRunId, x.WorkflowStepExecutionId });
    }

    private static void ConfigureMotionState(ModelBuilder modelBuilder)
    {
        var arm = modelBuilder.Entity<RobotArmState>();
        arm.ToTable("robot_arm_states");
        arm.HasKey(x => x.Id);
        arm.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        arm.Property(x => x.IsHomed).HasColumnName("is_homed").IsRequired();
        arm.Property(x => x.IsConnected).HasColumnName("is_connected").IsRequired();
        arm.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        arm.Property(x => x.CurrentTargetPointCode).HasColumnName("current_target_point_code").HasMaxLength(128);
        arm.Property(x => x.CurrentXUm).HasColumnName("current_x_um");
        arm.Property(x => x.CurrentYUm).HasColumnName("current_y_um");
        arm.Property(x => x.CurrentZUm).HasColumnName("current_z_um");
        arm.Property(x => x.CoordinateProfileVersionId).HasColumnName("coordinate_profile_version_id").HasMaxLength(36);
        arm.Property(x => x.CurrentCommandId).HasColumnName("current_command_id").HasMaxLength(128);
        arm.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        arm.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36);
        arm.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36);
        arm.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(128);
        arm.Property(x => x.LastErrorMessage).HasColumnName("last_error_message").HasMaxLength(2000);
        arm.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        var needles = modelBuilder.Entity<NeedleState>();
        needles.ToTable("needle_states");
        needles.HasKey(x => x.Id);
        needles.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        needles.Property(x => x.NeedleCode).HasColumnName("needle_code").HasMaxLength(16).IsRequired();
        needles.Property(x => x.NeedleNo).HasColumnName("needle_no").IsRequired();
        needles.Property(x => x.IsConnected).HasColumnName("is_connected").IsRequired();
        needles.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        needles.Property(x => x.LoadedSourceType).HasColumnName("loaded_source_type").HasMaxLength(32).IsRequired();
        needles.Property(x => x.LoadedReagentCode).HasColumnName("loaded_reagent_code").HasMaxLength(64);
        needles.Property(x => x.SourceBottleId).HasColumnName("source_bottle_id").HasMaxLength(36);
        needles.Property(x => x.DabBatchId).HasColumnName("dab_batch_id").HasMaxLength(36);
        needles.Property(x => x.SystemLiquidSourceType).HasColumnName("system_liquid_source_type").HasMaxLength(64);
        needles.Property(x => x.SourcePositionCode).HasColumnName("source_position_code").HasMaxLength(64);
        needles.Property(x => x.VolumeUl).HasColumnName("volume_ul").IsRequired();
        needles.Property(x => x.LiquidClassVersionId).HasColumnName("liquid_class_version_id").HasMaxLength(36);
        needles.Property(x => x.LiquidClassVersionNo).HasColumnName("liquid_class_version_no");
        needles.Property(x => x.LiquidClassParametersJson).HasColumnName("liquid_class_parameters_json").HasMaxLength(16000).HasDefaultValue("{}").IsRequired();
        needles.Property(x => x.NeedsWash).HasColumnName("needs_wash").IsRequired();
        needles.Property(x => x.CurrentCommandId).HasColumnName("current_command_id").HasMaxLength(128);
        needles.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        needles.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36);
        needles.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36);
        needles.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(128);
        needles.Property(x => x.LastErrorMessage).HasColumnName("last_error_message").HasMaxLength(2000);
        needles.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        needles.HasIndex(x => x.NeedleCode).IsUnique();
        needles.HasIndex(x => x.NeedleNo).IsUnique();

        var ops = modelBuilder.Entity<PipettingOperation>();
        ops.ToTable("pipetting_operations");
        ops.HasKey(x => x.Id);
        ops.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        ops.Property(x => x.OperationType).HasColumnName("operation_type").HasMaxLength(64).IsRequired();
        ops.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        ops.Property(x => x.NeedleCode).HasColumnName("needle_code").HasMaxLength(16);
        ops.Property(x => x.ExecutionMode).HasColumnName("execution_mode").HasMaxLength(32).IsRequired();
        ops.Property(x => x.TargetPointCode).HasColumnName("target_point_code").HasMaxLength(128);
        ops.Property(x => x.SecondaryTargetPointCode).HasColumnName("secondary_target_point_code").HasMaxLength(128);
        ops.Property(x => x.CoordinateProfileVersionId).HasColumnName("coordinate_profile_version_id").HasMaxLength(36);
        ops.Property(x => x.LiquidClassVersionId).HasColumnName("liquid_class_version_id").HasMaxLength(36);
        ops.Property(x => x.LiquidClassVersionNo).HasColumnName("liquid_class_version_no");
        ops.Property(x => x.LiquidClassParametersJson).HasColumnName("liquid_class_parameters_json").HasMaxLength(16000).HasDefaultValue("{}").IsRequired();
        ops.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired();
        ops.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64);
        ops.Property(x => x.ReagentBottleId).HasColumnName("reagent_bottle_id").HasMaxLength(36);
        ops.Property(x => x.DabBatchId).HasColumnName("dab_batch_id").HasMaxLength(36);
        ops.Property(x => x.SystemLiquidSourceType).HasColumnName("system_liquid_source_type").HasMaxLength(64);
        ops.Property(x => x.SourcePositionCode).HasColumnName("source_position_code").HasMaxLength(64);
        ops.Property(x => x.VolumeUl).HasColumnName("volume_ul").IsRequired();
        ops.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        ops.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36);
        ops.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36);
        ops.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        ops.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        ops.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        ops.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        ops.HasIndex(x => new { x.MachineRunId, x.WorkflowStepExecutionId });
        ops.HasIndex(x => x.DeviceCommandExecutionId);

        var leases = modelBuilder.Entity<MachineResourceLease>();
        leases.ToTable("machine_resource_leases");
        leases.HasKey(x => x.Id);
        leases.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        leases.Property(x => x.ResourceCode).HasColumnName("resource_code").HasMaxLength(128).IsRequired();
        leases.Property(x => x.ResourceType).HasColumnName("resource_type").HasMaxLength(32).IsRequired();
        leases.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        leases.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36);
        leases.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36);
        leases.Property(x => x.DeviceCommandExecutionId).HasColumnName("device_command_execution_id").HasMaxLength(36);
        leases.Property(x => x.CommandType).HasColumnName("command_type").HasMaxLength(128);
        leases.Property(x => x.WaitReason).HasColumnName("wait_reason").HasMaxLength(2000);
        leases.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        leases.Property(x => x.AcquiredAtUtc).HasColumnName("acquired_at_utc");
        leases.Property(x => x.ReleasedAtUtc).HasColumnName("released_at_utc");
        leases.HasIndex(x => x.ResourceCode).IsUnique().HasFilter("status = 'Acquired'");
        leases.HasIndex(x => new { x.MachineRunId, x.WorkflowStepExecutionId });
        leases.HasIndex(x => x.DeviceCommandExecutionId);
    }
}
