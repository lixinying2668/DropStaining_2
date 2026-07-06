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

        services.AddPersistenceServices(connectionString);
        services.AddRepositoryServices();
        services.AddApplicationServices();
        services.AddDeviceServices(configuration, environment);
        services.AddRuntimeMessagingServices();
        services.AddHostedRuntimeServices();
        services.AddSignalR();

        return services;
    }

    private static IServiceCollection AddPersistenceServices(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(new DatabaseHealthChecker(connectionString));
        services.AddDbContext<StainerDbContext>(options =>
            options.UseSqlite(connectionString)
                .AddInterceptors(new SqlitePragmaConnectionInterceptor()));
        services.AddScoped<ReferenceDataSeeder>();

        return services;
    }

    private static IServiceCollection AddRepositoryServices(this IServiceCollection services)
    {
        services.AddScoped<IReferenceDataRepository, EfReferenceDataRepository>();
        services.AddScoped<IWorkflowRepository, EfWorkflowRepository>();
        services.AddScoped<IReagentRepository, EfReagentRepository>();
        services.AddScoped<IWorkflowReadRepository, EfWorkflowReadRepository>();
        services.AddScoped<IReagentReadRepository, EfReagentReadRepository>();
        services.AddScoped<IEngineeringReadRepository, EfEngineeringReadRepository>();
        services.AddScoped<IUserReadRepository, EfUserReadRepository>();

        return services;
    }

    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<SafetyLogWriter>();
        services.AddSingleton<MachineExecutorLeaseService>();
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
        services.AddScoped<EngineeringSessionService>();
        services.AddScoped<EngineeringDiagnosticService>();
        services.AddScoped<DeviceCommunicationPersistenceService>();
        services.AddScoped<EngineeringConfigService>();
        services.AddScoped<EngineeringWriteService>();
        services.AddScoped<LiquidClassSnapshotFactory>();
        services.AddScoped<PreflightValidationService>();
        services.AddScoped<MachineRunService>();
        services.AddScoped<MachineRunQueryService>();
        services.AddScoped<OperatorSnapshotQueryService>();
        services.AddScoped<TraceabilityQueryService>();
        services.AddScoped<DeviceModeService>();
        services.AddScoped<DeviceControlService>();
        services.AddScoped<ThermalControlService>();
        services.AddScoped<FluidicsControlService>();
        services.AddScoped<MotionControlService>();
        services.AddScoped<DeviceInitializationService>();
        services.AddSingleton<StartupDeviceInitializationRunner>();
        services.AddScoped<StartupRecoveryService>();
        services.AddScoped<DatabaseMaintenanceService>();
        services.AddScoped<PreHardwareReadinessService>();
        services.AddScoped<RunControlService>();
        services.AddScoped<RuntimePageBridgeService>();
        services.AddSingleton<IReagentBarcodeParser, ReagentBarcodeParser>();

        return services;
    }

    private static IServiceCollection AddDeviceServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<MockDeviceStateStore>();
        var requestedMode = DeviceModes.Normalize(configuration["Device:Mode"]);
        var debugMode = IsEnabled(configuration["Device:DebugMode"]) || environment.IsDevelopment();
        var hardwareAvailable = IsEnabled(configuration["Device:HardwareAvailable"]);
        var useMockFallback = !bool.TryParse(configuration["Device:UseMockWhenHardwareUnavailable"], out var configuredFallback)
            || configuredFallback;
        if (requestedMode != DeviceModes.Real || debugMode || (!hardwareAvailable && useMockFallback))
        {
            services.AddSingleton<IDeviceAdapter, MockDeviceOperations>();
        }
        else
        {
            services.AddSingleton<IDeviceAdapter, UnavailableRealDeviceAdapter>();
        }

        return services;
    }

    private static bool IsEnabled(string? value) =>
        bool.TryParse(value, out var enabled) && enabled;

    private static IServiceCollection AddRuntimeMessagingServices(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryRuntimeEventPublisher>();
        services.AddSingleton<IRuntimeEventPublisher>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeEventPublisher>());
        services.AddSingleton<MachineExecutor>();

        return services;
    }

    private static IServiceCollection AddHostedRuntimeServices(this IServiceCollection services)
    {
        services.AddHostedService<StartupDeviceInitializationHostedService>();
        services.AddHostedService<MachineExecutorHostedService>();
        services.AddHostedService<DabExpiryHostedService>();
        services.AddHostedService<MachineEventSignalRDispatcher>();

        return services;
    }
}
