using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stainer.Web.Application.Requests;

namespace Stainer.Web.Application.Services;

public sealed class StartupDeviceInitializationHostedService(StartupDeviceInitializationRunner runner) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        runner.Start(stoppingToken);
        return Task.CompletedTask;
    }
}

public sealed class StartupDeviceInitializationRunner(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<StartupDeviceInitializationRunner> logger)
{
    private static readonly AuthenticatedUser SystemActor = new(
        string.Empty,
        "system",
        "System",
        "system",
        ["system", "admin", "engineer"]);
    private int started;

    public void Start(CancellationToken stoppingToken)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () => await RunAsync(stoppingToken), CancellationToken.None);
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!StartupInitializationEnabled(configuration))
        {
            return;
        }

        await Task.Yield();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<DeviceInitializationService>();
                var commandId = $"startup-device-initialization-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
                await service.InitializeAsync(new StartDeviceInitializationRequest(commandId), SystemActor, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (attempt < 20)
            {
                logger.LogWarning(ex, "Startup device initialization attempt {Attempt} failed; retrying.", attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Startup device initialization failed before a normal result could be persisted.");
            }
        }
    }

    private static bool StartupInitializationEnabled(IConfiguration configuration)
    {
        var configured = configuration["Device:StartupInitialization:Enabled"];
        return !bool.TryParse(configured, out var enabled) || enabled;
    }
}
