using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Repositories;
using Stainer.Web.Application.Services;
using Stainer.Web.Infrastructure.Data;
using Stainer.Web.Application.Devices;
using Stainer.Web.Infrastructure.Devices;
using Stainer.Web.Infrastructure.Health;
using Stainer.Web.Infrastructure.Repositories;
using Stainer.Web.Infrastructure.Web;

namespace Stainer.Web.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStainerInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = DatabasePathResolver.ResolveConnectionString(configuration, environment);
        DatabaseInitializer.EnsureDatabaseDirectory(connectionString);

        services.AddSingleton(new DatabaseHealthChecker(connectionString));
        services.AddSingleton<SafetyLogWriter>();
        services.AddSingleton<MachineExecutorLeaseService>();
        services.AddDbContext<StainerDbContext>(options =>
            options.UseSqlite(connectionString)
                .AddInterceptors(new SqlitePragmaConnectionInterceptor()));
        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<IReferenceDataRepository, EfReferenceDataRepository>();
        services.AddScoped<IWorkflowRepository, EfWorkflowRepository>();
        services.AddScoped<IReagentRepository, EfReagentRepository>();
        services.AddScoped<IWorkflowReadRepository, EfWorkflowReadRepository>();
        services.AddScoped<IReagentReadRepository, EfReagentReadRepository>();
        services.AddScoped<IEngineeringReadRepository, EfEngineeringReadRepository>();
        services.AddScoped<IUserReadRepository, EfUserReadRepository>();
        services.AddScoped<WorkflowQueryService>();
        services.AddScoped<ReagentQueryService>();
        services.AddScoped<EngineeringQueryService>();
        services.AddScoped<UserQueryService>();
        services.AddScoped<PasswordHashService>();
        services.AddScoped<CommandIdempotencyService>();
        services.AddScoped<UserSessionService>();
        services.AddScoped<UserManagementService>();
        services.AddScoped<WorkflowWriteService>();
        services.AddScoped<WorkflowMaintenanceService>();
        services.AddScoped<ChannelBatchWorkflowService>();
        services.AddScoped<ChannelBatchWorkflowBackfillService>();
        services.AddScoped<TaskCreationService>();
        services.AddScoped<ReagentScanWriteService>();
        services.AddScoped<SampleScanWriteService>();
        services.AddScoped<ReagentScannerMockService>();
        services.AddScoped<HospitalBarcodeNormalizer>();
        services.AddScoped<IMockLisAdapter, MockLisAdapter>();
        services.AddScoped<MockLisQueryService>();
        services.AddScoped<MockDemoDataSeeder>();
        services.AddScoped<DabLifecycleService>();
        services.AddScoped<CoordinateProfileLifecycleService>();
        services.AddScoped<EngineeringWriteService>();
        services.AddScoped<LiquidClassSnapshotFactory>();
        services.AddScoped<PreflightValidationService>();
        services.AddScoped<MachineRunService>();
        services.AddScoped<MachineRunQueryService>();
        services.AddScoped<TraceabilityQueryService>();
        services.AddScoped<DeviceModeService>();
        services.AddScoped<DeviceControlService>();
        services.AddScoped<ThermalControlService>();
        services.AddScoped<FluidicsControlService>();
        services.AddScoped<MotionControlService>();
        services.AddScoped<DeviceInitializationService>();
        services.AddScoped<StartupRecoveryService>();
        services.AddScoped<DatabaseMaintenanceService>();
        services.AddScoped<PreHardwareReadinessService>();
        services.AddScoped<RunControlService>();
        services.AddScoped<RuntimePageBridgeService>();
        services.AddSingleton<IReagentBarcodeParser, ReagentBarcodeParser>();
        services.AddSingleton<MockDeviceStateStore>();
        if (DeviceModes.Normalize(configuration["Device:Mode"]) == DeviceModes.Real)
        {
            services.AddSingleton<IDeviceAdapter, UnavailableRealDeviceAdapter>();
        }
        else
        {
            services.AddSingleton<IDeviceAdapter, MockDeviceAdapter>();
        }
        services.AddSingleton<InMemoryRuntimeEventPublisher>();
        services.AddSingleton<IRuntimeEventPublisher>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeEventPublisher>());
        services.AddSingleton<MachineExecutor>();
        services.AddHostedService<MachineExecutorHostedService>();
        services.AddHostedService<DabExpiryHostedService>();
        services.AddHostedService<MachineEventSignalRDispatcher>();
        services.AddSignalR();

        return services;
    }
}
