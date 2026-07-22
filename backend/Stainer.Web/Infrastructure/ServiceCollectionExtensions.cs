using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Repositories;
using Stainer.Web.Application.Services;
using Stainer.Web.Infrastructure.Data;
using Stainer.Web.Application.Devices;
using Stainer.Web.Infrastructure.Devices;
using Stainer.Web.Infrastructure.Health;
using Stainer.Web.Infrastructure.Repositories;
using Stainer.Web.Infrastructure.Twin;
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
        services.AddScoped<WorkflowPrimaryAntibodyResolver>();
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
        services.AddScoped<MockRuntimeResetService>();
        services.AddScoped<DabLifecycleService>();
        services.AddScoped<CoordinateProfileLifecycleService>();
        services.AddScoped<DigitalTwinCoordinateImportService>();
        services.AddScoped<EngineeringSessionService>();
        services.AddScoped<EngineeringDiagnosticService>();
        services.AddScoped<EngineeringPipettingService>();
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
        services.AddScoped<ReagentQrScannerDeviceOperationService>();
        services.AddScoped<IReagentHardwareSink, ReagentHardwareSink>();
        services.AddScoped<ScannerConfigurationService>();
        services.AddScoped<SerialConnectionConfigService>();
        services.AddScoped<PrecisionCalibrationConfigService>();
        services.AddScoped<MixerParameterConfigService>();
        services.AddScoped<WashValveConfigService>();
        services.AddScoped<AppSettingsConfigService>();
        services.AddScoped<SerialConnectionConfigService>();
        services.AddScoped<ReagentCoordinateAnchorService>();
        services.AddScoped<ReagentCoordinateGenerationService>();
        services.AddScoped<ScannerControlService>();
        services.AddScoped<ThermalControlService>();
        services.AddScoped<FluidicsControlService>();
        services.AddScoped<MotionControlService>();
        services.AddScoped<DeviceInitializationService>();
        services.AddScoped<DevicePrecheckService>();
        services.AddSingleton<StartupDeviceInitializationRunner>();
        services.AddScoped<StartupRecoveryService>();
        services.AddScoped<DatabaseMaintenanceService>();
        services.AddScoped<PreHardwareReadinessService>();
        services.AddScoped<RunControlService>();
        services.AddScoped<RuntimePageBridgeService>();
        services.AddSingleton<IReagentBarcodeParser, ReagentBarcodeParser>();
        services.AddSingleton<TwinSnapshotService>();

        return services;
    }

    private static IServiceCollection AddDeviceServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<MockDeviceStateStore>();
        services.AddSingleton<MockDeviceOperations>();
        var requestedMode = DeviceModes.Normalize(configuration["Device:Mode"]);
        var dcr55Configuration = configuration
                .GetSection("Device:Dcr55")
                .Get<Dcr55ConnectionOptions>() ?? new Dcr55ConnectionOptions();
        services.AddSingleton(dcr55Configuration);

            // P1-03-01：主控（Main Controller）串口连接配置。
            // 串口参数全部来自配置（appsettings 的 Device:MainController 节），
            // 不硬编码任何 COM 口，不自动扫描 / 枚举 USB 设备。
        var mainControllerConfiguration = configuration
                .GetSection("Device:MainController")
                .Get<MainControllerConnectionOptions>() ?? new MainControllerConnectionOptions();
        services.AddSingleton(mainControllerConfiguration);

            // DCR55-02 / P1-03-01：在 Real 模式下注册真实串口 Transport。
            // SerialPort 仅存在于 Dcr55SerialTransport / MainControllerSerialTransport（Transport 层），
            // Application 层通过 IDeviceByteTransport 获取结果，永不接触串口。
            // 当连接配置未配置 Port / PortName 时，各 Transport 仍被构造，
            // 但 ReceiveAsync / ExchangeAsync 会以 NotConnected 失败闭合，不会尝试打开任何 COM 口。
            //
            // 单个 IDeviceByteTransport 使用 CompositeDeviceByteTransport 按 endpoint 路由：
            //   - main-controller-v1.0.4 -> MainControllerSerialTransport
            //   - dcr55-sample-scanner    -> Dcr55SerialTransport
            // 这样 Application 层依赖保持不变，而两个真实串口各自独立配置与协议。
        var mainControllerTransport = new MainControllerSerialTransport(mainControllerConfiguration);
        var dcr55Transport = new Dcr55SerialTransport(dcr55Configuration);
        services.AddSingleton(mainControllerTransport);
        services.AddSingleton(dcr55Transport);
        services.AddSingleton<IDeviceByteTransport>(new CompositeDeviceByteTransport(mainControllerTransport, dcr55Transport));
        services.AddSingleton<IDcr55Adapter>(serviceProvider =>
            new Dcr55RealAdapter(
                serviceProvider.GetRequiredService<IDeviceByteTransport>(),
                serviceProvider.GetRequiredService<Dcr55ConnectionOptions>()));
        services.AddSingleton<UnavailableRealDeviceAdapter>(serviceProvider =>
            new UnavailableRealDeviceAdapter(serviceProvider.GetRequiredService<IDeviceByteTransport>()));
        services.AddSingleton<IRealDeviceReadAdapter>(serviceProvider =>
            serviceProvider.GetRequiredService<UnavailableRealDeviceAdapter>());

        // The configured global adapter is fail-closed: Real never falls back to Mock.
        // DevicePrecheckService resolves both concrete adapters and selects by requested runtime mode.
        services.AddSingleton<IDeviceAdapter>(serviceProvider =>
            requestedMode == DeviceModes.Real
                ? serviceProvider.GetRequiredService<UnavailableRealDeviceAdapter>()
                : serviceProvider.GetRequiredService<MockDeviceOperations>());

        return services;
    }

    private static bool IsEnabled(string? value) =>
        bool.TryParse(value, out var enabled) && enabled;

    private static IServiceCollection AddRuntimeMessagingServices(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryRuntimeEventPublisher>();
        // 试剂区硬件通信旁挂：用装饰器替代裸 InMemoryRuntimeEventPublisher 作为 IRuntimeEventPublisher 的解析目标。
        // MachineEventSignalRDispatcher 注入的是具体类 InMemoryRuntimeEventPublisher（inner），仍读 inner 的 channel，零感知、零改动。
        // ReagentScanWriteService 注入 IRuntimeEventPublisher，运行时自动拿到装饰器（业务零改）。
        services.AddSingleton<ReagentHardwareEventDecorator>(serviceProvider => new ReagentHardwareEventDecorator(
            serviceProvider.GetRequiredService<InMemoryRuntimeEventPublisher>(),
            serviceProvider.GetRequiredService<IConfiguration>()));
        services.AddSingleton<IRuntimeEventPublisher>(serviceProvider => serviceProvider.GetRequiredService<ReagentHardwareEventDecorator>());
        services.AddSingleton<MachineExecutor>();

        return services;
    }

    private static IServiceCollection AddHostedRuntimeServices(this IServiceCollection services)
    {
        services.AddHostedService<StartupDeviceInitializationHostedService>();
        services.AddHostedService<MachineExecutorHostedService>();
        services.AddHostedService<DabExpiryHostedService>();
        services.AddHostedService<MachineEventSignalRDispatcher>();
        services.AddHostedService<ReagentHardwareDispatcher>();

        return services;
    }
}
