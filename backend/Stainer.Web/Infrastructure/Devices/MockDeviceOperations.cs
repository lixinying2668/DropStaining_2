using Microsoft.Extensions.DependencyInjection;

namespace Stainer.Web.Infrastructure.Devices;

public sealed class MockDeviceOperations(
    MockDeviceStateStore stateStore,
    IServiceScopeFactory? scopeFactory = null) : MockDeviceAdapter(stateStore, scopeFactory)
{
    public override string Name => nameof(MockDeviceOperations);
}
