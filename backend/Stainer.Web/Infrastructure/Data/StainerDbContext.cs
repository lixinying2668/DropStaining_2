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
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(x => x.ChangeNote).HasColumnName("change_note").HasMaxLength(2000).IsRequired();
        entity.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        entity.Property(x => x.RetiredAtUtc).HasColumnName("retired_at_utc");
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.HasIndex(x => new { x.WorkflowDefinitionId, x.VersionNo }).IsUnique();
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
        entity.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(128).IsRequired();
        entity.Property(x => x.ReagentCode).HasColumnName("reagent_code").HasMaxLength(64);
        entity.Property(x => x.VolumeUl).HasColumnName("volume_ul");
        entity.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
        entity.Property(x => x.TargetTemperatureDeciC).HasColumnName("target_temperature_deci_c");
        entity.Property(x => x.MixParametersJson).HasColumnName("mix_parameters_json").HasMaxLength(4000).IsRequired();
        entity.Property(x => x.WashParametersJson).HasColumnName("wash_parameters_json").HasMaxLength(4000).IsRequired();
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
        entity.Property(x => x.PreWetCycles).HasColumnName("pre_wet_cycles");
        entity.Property(x => x.MixCycles).HasColumnName("mix_cycles");
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
        entity.Property(x => x.LiquidClassProfileId).HasColumnName("liquid_class_profile_id").HasMaxLength(36);
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
}
