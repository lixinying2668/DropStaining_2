using Stainer.Web.Application.Devices;

namespace Stainer.Web.Infrastructure.Devices;

// 组合 Transport：按 endpoint 路由到具体的串口 Transport（Main Controller / DCR55 / …）。
//
// 设计动机（任务 P1-03-01）：
// - 现有架构中 Application 层（UnavailableRealDeviceAdapter 等）只持有单个
//   IDeviceByteTransport，并通过 endpoint 区分目标设备。
// - 现在需要同时接入 MainController 与 DCR55 两个真实串口，它们使用不同的 COM 口、
//   不同的串口参数、不同的协议语义；直接把两套配置塞进同一个 Transport 会破坏
//   “单一职责”和“协议无交叉”的边界。
// - 因此在 Infrastructure 层新增一个轻量路由器：Application 层依赖不发生任何变化，
//   具体 SerialPort 代码仍然只存在于各自的 Transport 实现里，本类不含任何业务逻辑，
//   也不直接访问 System.IO.Ports。
//
// 路由规则：
// - MainController endpoint  -> MainControllerSerialTransport
// - Dcr55 endpoint           -> Dcr55SerialTransport
// - 其他 endpoint             -> Failed(endpoint_not_configured)
//
// IsConfigured 语义：只要任意一个被路由的 Transport 已配置即为 true，
// 这样 UnavailableRealDeviceAdapter 的 TransportUnavailable 判定不会因为其中一个
// Transport 未配置而整体短路；具体 Transport 内部仍会按自身配置闭合。
public sealed class CompositeDeviceByteTransport : IDeviceByteTransport
{
    private readonly IDeviceByteTransport? mainController;
    private readonly IDeviceByteTransport? dcr55;

    public CompositeDeviceByteTransport(
        IDeviceByteTransport? mainController = null,
        IDeviceByteTransport? dcr55 = null)
    {
        this.mainController = mainController;
        this.dcr55 = dcr55;
    }

    public string Name => "composite-serial";

    public bool IsConfigured =>
        (mainController?.IsConfigured ?? false) || (dcr55?.IsConfigured ?? false);

    public Task<DeviceByteTransportResult> ExchangeAsync(
        DeviceByteTransportRequest request,
        CancellationToken cancellationToken = default)
    {
        var transport = Route(request.Endpoint);
        return transport is null
            ? Task.FromResult(EndpointNotSupported(request.Endpoint))
            : transport.ExchangeAsync(request, cancellationToken);
    }

    public Task<DeviceByteTransportResult> ReceiveAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var transport = Route(endpoint);
        return transport is null
            ? Task.FromResult(EndpointNotSupported(endpoint))
            : transport.ReceiveAsync(endpoint, cancellationToken);
    }

    private IDeviceByteTransport? Route(string endpoint)
    {
        if (string.Equals(endpoint, DeviceByteTransportEndpoints.MainController, StringComparison.Ordinal))
        {
            return mainController;
        }

        if (string.Equals(endpoint, DeviceByteTransportEndpoints.Dcr55, StringComparison.Ordinal))
        {
            return dcr55;
        }

        return null;
    }

    private static DeviceByteTransportResult EndpointNotSupported(string endpoint) =>
        new(
            DeviceByteTransportStatuses.Failed,
            [],
            "composite_endpoint_not_configured",
            $"No serial transport is registered for endpoint '{endpoint}'.");
}