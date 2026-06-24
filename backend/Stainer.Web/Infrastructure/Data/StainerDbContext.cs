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
    public DbSet<LegacyImportRun> LegacyImportRuns => Set<LegacyImportRun>();
    public DbSet<LegacyImportIssue> LegacyImportIssues => Set<LegacyImportIssue>();
    public DbSet<LegacyRuntimeSnapshot> LegacyRuntimeSnapshots => Set<LegacyRuntimeSnapshot>();
    public DbSet<CommandReceipt> CommandReceipts => Set<CommandReceipt>();
    public DbSet<StainingTask> StainingTasks => Set<StainingTask>();
    public DbSet<HospitalBarcodeMapping> HospitalBarcodeMappings => Set<HospitalBarcodeMapping>();
    public DbSet<ChannelBatch> ChannelBatches => Set<ChannelBatch>();
    public DbSet<SlideTask> SlideTasks => Set<SlideTask>();
    public DbSet<MachineRun> MachineRuns => Set<MachineRun>();
    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();
    public DbSet<WorkflowStepExecution> WorkflowStepExecutions => Set<WorkflowStepExecution>();
    public DbSet<DeviceCommandExecution> DeviceCommandExecutions => Set<DeviceCommandExecution>();
    public DbSet<ReagentReservation> ReagentReservations => Set<ReagentReservation>();
    public DbSet<ReagentConsumption> ReagentConsumptions => Set<ReagentConsumption>();
    public DbSet<DispenseExecution> DispenseExecutions => Set<DispenseExecution>();
    public DbSet<DabBatch> DabBatches => Set<DabBatch>();
    public DbSet<DabBatchUsage> DabBatchUsages => Set<DabBatchUsage>();
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
        ConfigureCoordinatePoint(modelBuilder);
        ConfigureCoordinateCalibrationHistory(modelBuilder);
        ConfigureWorkflowDefinition(modelBuilder);
        ConfigureWorkflowVersion(modelBuilder);
        ConfigureWorkflowStep(modelBuilder);
        ConfigureWorkflowReagentRequirement(modelBuilder);
        ConfigurePrimaryAntibodyWorkflowMapping(modelBuilder);
        ConfigureLiquidClassProfile(modelBuilder);
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
        ConfigureRuntimeLedger(modelBuilder);
    }

    public override int SaveChanges()
    {
        ValidateWorkflowVersionChanges();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateWorkflowVersionChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(true, cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await ValidateWorkflowVersionChangesAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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
            if (originalStatus == WorkflowVersionStatus.Published)
            {
                throw new InvalidOperationException("Published workflow versions cannot be modified in place. Create a new version for changes.");
            }
        }
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
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.PositionNo).IsUnique();
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
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
    }

    private static void ConfigureCoordinatePoint(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CoordinatePoint>();
        entity.ToTable("coordinate_points");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.CoordinateProfileId).HasColumnName("coordinate_profile_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.PointCode).HasColumnName("point_code").HasMaxLength(128).IsRequired();
        entity.Property(x => x.PointType).HasColumnName("point_type").HasMaxLength(64).IsRequired();
        entity.Property(x => x.PresetXUm).HasColumnName("preset_x_um");
        entity.Property(x => x.PresetYUm).HasColumnName("preset_y_um");
        entity.Property(x => x.CalibratedXUm).HasColumnName("calibrated_x_um");
        entity.Property(x => x.CalibratedYUm).HasColumnName("calibrated_y_um");
        entity.Property(x => x.SafeZUm).HasColumnName("safe_z_um");
        entity.Property(x => x.AspirateZUm).HasColumnName("aspirate_z_um");
        entity.Property(x => x.DispenseZUm).HasColumnName("dispense_z_um");
        entity.Property(x => x.RequiresCalibration).HasColumnName("requires_calibration").IsRequired();
        entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => new { x.CoordinateProfileId, x.PointCode }).IsUnique();
        entity.HasOne(x => x.CoordinateProfile).WithMany(x => x.CoordinatePoints).HasForeignKey(x => x.CoordinateProfileId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureCoordinateCalibrationHistory(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CoordinateCalibrationHistory>();
        entity.ToTable("coordinate_calibration_history");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.CoordinatePointId).HasColumnName("coordinate_point_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.PreviousXUm).HasColumnName("previous_x_um");
        entity.Property(x => x.PreviousYUm).HasColumnName("previous_y_um");
        entity.Property(x => x.NewXUm).HasColumnName("new_x_um");
        entity.Property(x => x.NewYUm).HasColumnName("new_y_um");
        entity.Property(x => x.SafeZUm).HasColumnName("safe_z_um");
        entity.Property(x => x.AspirateZUm).HasColumnName("aspirate_z_um");
        entity.Property(x => x.DispenseZUm).HasColumnName("dispense_z_um");
        entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        entity.Property(x => x.CalibratedByUserId).HasColumnName("calibrated_by_user_id").HasMaxLength(36);
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasOne(x => x.CoordinatePoint).WithMany(x => x.CalibrationHistory).HasForeignKey(x => x.CoordinatePointId).OnDelete(DeleteBehavior.Cascade);
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
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        entity.Property(x => x.WorkflowDefinitionId).HasColumnName("workflow_definition_id").HasMaxLength(36).IsRequired();
        entity.Property(x => x.VersionNo).HasColumnName("version_no").IsRequired();
        entity.Property(x => x.VersionLabel).HasColumnName("version_label").HasMaxLength(64).IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(x => x.ChangeNote).HasColumnName("change_note").HasMaxLength(2000).IsRequired();
        entity.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        entity.Property(x => x.RetiredAtUtc).HasColumnName("retired_at_utc");
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => new { x.WorkflowDefinitionId, x.VersionNo }).IsUnique();
        entity.HasIndex(x => new { x.WorkflowDefinitionId, x.VersionLabel }).IsUnique();
        entity.HasIndex(x => x.Status);
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
        entity.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(36);
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => x.TaskCode).IsUnique();
        entity.HasIndex(x => new { x.PhysicalSlotId, x.Status });
        entity.HasIndex(x => x.WorkflowVersionId);
        entity.HasIndex(x => x.PrimaryAntibodyCode);
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
        runs.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        runs.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        runs.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        runs.HasIndex(x => x.RunCode).IsUnique();
        runs.HasIndex(x => x.Status);
        runs.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);

        var batches = modelBuilder.Entity<ChannelBatch>();
        batches.ToTable("channel_batches");
        batches.HasKey(x => x.Id);
        batches.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        batches.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36).IsRequired();
        batches.Property(x => x.DrawerId).HasColumnName("drawer_id").HasMaxLength(36).IsRequired();
        batches.Property(x => x.DrawerCode).HasColumnName("drawer_code").HasMaxLength(8).IsRequired();
        batches.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        batches.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        batches.HasIndex(x => new { x.MachineRunId, x.DrawerCode }).IsUnique();
        batches.HasOne(x => x.MachineRun).WithMany(x => x.ChannelBatches).HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        batches.HasOne(x => x.Drawer).WithMany().HasForeignKey(x => x.DrawerId).OnDelete(DeleteBehavior.Restrict);

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
        deviceCommands.Property(x => x.ResultJson).HasColumnName("result_json").HasMaxLength(8000).IsRequired();
        deviceCommands.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        deviceCommands.Property(x => x.CommandSentAtUtc).HasColumnName("command_sent_at_utc");
        deviceCommands.Property(x => x.AcknowledgedAtUtc).HasColumnName("acknowledged_at_utc");
        deviceCommands.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        deviceCommands.HasIndex(x => x.MachineRunId);
        deviceCommands.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        deviceCommands.HasOne(x => x.WorkflowStepExecution).WithMany().HasForeignKey(x => x.WorkflowStepExecutionId).OnDelete(DeleteBehavior.SetNull);

        ConfigureRuntimeConsumables(modelBuilder);
        ConfigureRuntimeAlarms(modelBuilder);
    }

    private static void ConfigureRuntimeConsumables(ModelBuilder modelBuilder)
    {
        var reservations = modelBuilder.Entity<ReagentReservation>();
        reservations.ToTable("reagent_reservations");
        reservations.HasKey(x => x.Id);
        reservations.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        reservations.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36).IsRequired();
        reservations.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64).IsRequired();
        reservations.Property(x => x.RequiredVolumeUl).HasColumnName("required_volume_ul").IsRequired();
        reservations.Property(x => x.ReservedVolumeUl).HasColumnName("reserved_volume_ul").IsRequired();
        reservations.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        reservations.HasIndex(x => new { x.MachineRunId, x.ReagentCode });
        reservations.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);

        var consumptions = modelBuilder.Entity<ReagentConsumption>();
        consumptions.ToTable("reagent_consumptions");
        consumptions.HasKey(x => x.Id);
        consumptions.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        consumptions.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36).IsRequired();
        consumptions.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36).IsRequired();
        consumptions.Property(x => x.ReagentBottleId).HasColumnName("reagent_bottle_id").HasMaxLength(36).IsRequired();
        consumptions.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64).IsRequired();
        consumptions.Property(x => x.VolumeUl).HasColumnName("volume_ul").IsRequired();
        consumptions.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        consumptions.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        consumptions.HasOne(x => x.WorkflowStepExecution).WithMany().HasForeignKey(x => x.WorkflowStepExecutionId).OnDelete(DeleteBehavior.Cascade);
        consumptions.HasOne(x => x.ReagentBottle).WithMany().HasForeignKey(x => x.ReagentBottleId).OnDelete(DeleteBehavior.Restrict);

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
        dispenses.HasOne(x => x.DeviceCommandExecution).WithMany().HasForeignKey(x => x.DeviceCommandExecutionId).OnDelete(DeleteBehavior.Cascade);
        dispenses.HasOne(x => x.ReagentBottle).WithMany().HasForeignKey(x => x.ReagentBottleId).OnDelete(DeleteBehavior.SetNull);

        var dabBatches = modelBuilder.Entity<DabBatch>();
        dabBatches.ToTable("dab_batches");
        dabBatches.HasKey(x => x.Id);
        dabBatches.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        dabBatches.Property(x => x.DabMixPositionId).HasColumnName("dab_mix_position_id").HasMaxLength(36).IsRequired();
        dabBatches.Property(x => x.PositionCode).HasColumnName("position_code").HasMaxLength(16).IsRequired();
        dabBatches.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        dabBatches.Property(x => x.RemainingVolumeUl).HasColumnName("remaining_volume_ul").IsRequired();
        dabBatches.Property(x => x.PreparedAtUtc).HasColumnName("prepared_at_utc").IsRequired();
        dabBatches.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        dabBatches.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        dabBatches.HasIndex(x => new { x.PositionCode, x.Status });
        dabBatches.HasOne(x => x.DabMixPosition).WithMany().HasForeignKey(x => x.DabMixPositionId).OnDelete(DeleteBehavior.Restrict);

        var dabUsages = modelBuilder.Entity<DabBatchUsage>();
        dabUsages.ToTable("dab_batch_usages");
        dabUsages.HasKey(x => x.Id);
        dabUsages.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
        dabUsages.Property(x => x.DabBatchId).HasColumnName("dab_batch_id").HasMaxLength(36).IsRequired();
        dabUsages.Property(x => x.MachineRunId).HasColumnName("machine_run_id").HasMaxLength(36).IsRequired();
        dabUsages.Property(x => x.WorkflowStepExecutionId).HasColumnName("workflow_step_execution_id").HasMaxLength(36).IsRequired();
        dabUsages.Property(x => x.VolumeUl).HasColumnName("volume_ul").IsRequired();
        dabUsages.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        dabUsages.HasOne(x => x.DabBatch).WithMany().HasForeignKey(x => x.DabBatchId).OnDelete(DeleteBehavior.Cascade);
        dabUsages.HasOne(x => x.MachineRun).WithMany().HasForeignKey(x => x.MachineRunId).OnDelete(DeleteBehavior.Cascade);
        dabUsages.HasOne(x => x.WorkflowStepExecution).WithMany().HasForeignKey(x => x.WorkflowStepExecutionId).OnDelete(DeleteBehavior.Cascade);
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
}
