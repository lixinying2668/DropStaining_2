using Microsoft.AspNetCore.SignalR;
using Stainer.Web.Application.Services;

namespace Stainer.Web.Infrastructure.Web;

public sealed class MachineEventSignalRDispatcher(
    InMemoryRuntimeEventPublisher publisher,
    IHubContext<MachineHub> hubContext,
    ILogger<MachineEventSignalRDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in publisher.ReadAllAsync(stoppingToken))
            {
                var clients = string.Equals(message.RequiredRole, "engineer", StringComparison.OrdinalIgnoreCase)
                    ? hubContext.Clients.Group(MachineHub.EngineerGroup)
                    : hubContext.Clients.Group(MachineHub.AuthorizedGroup);
                await clients.SendAsync("machineEvent", message, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Machine SignalR dispatcher stopped unexpectedly.");
            throw;
        }
    }
}
