using System.Threading.Channels;
using Microsoft.Extensions.Configuration;

namespace Stainer.Web.Application.Services;

// 装饰 IRuntimeEventPublisher，为试剂区硬件通信旁挂一条独立事件通道。
//
// 为什么用装饰器 + 独立 channel，而不是直接订阅 InMemoryRuntimeEventPublisher：
//   System.Threading.Channels 是竞争消费（每条消息只交付给一个 reader），不是广播。
//   若新 reader 直接读 InMemoryRuntimeEventPublisher 的 channel，会与现有 MachineEventSignalRDispatcher
//   瓜分消息，导致 SignalR 丢消息。故本装饰器：
//     1) 同步把所有事件转发到 inner（InMemoryRuntimeEventPublisher），SignalR 链路零感知、零改动；
//     2) 仅当 Device:ReagentHardware:Enabled=true 且事件类型属于试剂硬件白名单时，
//        额外镜像一份到自有的 hardwareChannel，供 ReagentHardwareDispatcher 单读。
//   inner channel 的 SingleReader=true 契约不破坏（本类只调 inner.Publish，永不读 inner）。
//
// 边界：本类是进程内事件基础设施的扩展，既不属于"前端→后端接口"，也不属于"后端→数据库逻辑"。
public sealed class ReagentHardwareEventDecorator : IRuntimeEventPublisher
{
    private static readonly IReadOnlySet<string> ReagentHardwareEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        MachineEventTypes.ReagentChanged,
        MachineEventTypes.ReagentBottleChanged,
        MachineEventTypes.ReagentBottleDepleted
    };

    private readonly InMemoryRuntimeEventPublisher inner;
    private readonly bool enabled;
    private readonly Channel<MachineEventMessage> hardwareChannel =
        Channel.CreateUnbounded<MachineEventMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,  // 仅 ReagentHardwareDispatcher 读
            SingleWriter = false  // 任意发布线程可写
        });

    public ReagentHardwareEventDecorator(InMemoryRuntimeEventPublisher inner, IConfiguration configuration)
    {
        this.inner = inner;
        // 开关放装饰器读：Enabled=false 时完全不写 hardwareChannel，
        // 避免无人消费的 channel 无界增长。
        enabled = bool.TryParse(configuration["Device:ReagentHardware:Enabled"], out var flag) && flag;
    }

    // 字符串重载只映射为 MachineStateChanged/WorkflowStepCompleted（见 InMemoryRuntimeEventPublisher.Publish(string)），
    // 永不产生试剂事件，直接转调 inner 即可。
    public void Publish(string runId, string eventType, string message)
        => inner.Publish(runId, eventType, message);

    public void Publish(MachineEventMessage message)
    {
        inner.Publish(message);
        if (enabled && ReagentHardwareEventTypes.Contains(message.Type))
        {
            hardwareChannel.Writer.TryWrite(message);
        }
    }

    // 仅供 ReagentHardwareDispatcher 调用。
    // 注意：ScanSessionChanged 被刻意排除（session start/complete/confirm 都发，会与 ReagentChanged 重复触发硬件）。
    public IAsyncEnumerable<MachineEventMessage> ReadAllReagentHardwareAsync(CancellationToken cancellationToken)
        => hardwareChannel.Reader.ReadAllAsync(cancellationToken);
}
