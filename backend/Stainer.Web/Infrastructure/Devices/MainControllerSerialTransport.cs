using System.Buffers.Binary;
using System.IO.Ports;
using Stainer.Web.Application.Devices;

namespace Stainer.Web.Infrastructure.Devices;

// 主控（Main Controller）真实串口 Transport。任务 P1-03-01。
//
// 设计约束（必须遵守）：
// - SerialPort（System.IO.Ports）仅存在于本 Transport 层；Application 层
//   （UnavailableRealDeviceAdapter 等）只通过 IDeviceByteTransport 的
//   ExchangeAsync / ReceiveAsync 获取结果，永远不会直接接触串口。
// - Transport 在发送前解码请求帧并校验“已审核主控命令白名单”（IsApprovedMainControllerCommand）：
//     * 只读：0x01/0x08、0x01/0x09、0x04/0x09~0x0B、0x03/0x01~0x03、0x03/0x05。
//     * 写入：仅制冷 0x03/0x04（目标温度）、0x03/0x06（开关），payload 须为 2 字节 UINT16 LE 且量程合法。
//     * 其余命令（RESET、加热写、PWM、混匀、IO、扫码启动等）一律 NotSupported，不发出字节。
// - COM 口由 MainControllerConnectionOptions 配置提供，绝不硬编码、绝不自动扫描。
// - 不自动 Fallback 到 Mock；失败即返回对应状态码闭合。
public sealed class MainControllerSerialTransport : IDeviceByteTransport
{
    private const int FixedReadChunkBytes = 64;

    private readonly MainControllerConnectionOptions configuration;
    private readonly Func<ISerialPort> portFactory;
    private readonly TimeSpan readTimeout;
    private readonly TimeSpan writeTimeout;
    private readonly Action<MainControllerTransportDiagnostic>? recordDiagnostic;

    public string Name => "main-controller-serial";

    public bool IsConfigured => configuration.IsConfigured;

    // 生产构造：使用真实 System.IO.Ports.SerialPort。
    public MainControllerSerialTransport(
        MainControllerConnectionOptions configuration,
        Action<MainControllerTransportDiagnostic>? recordDiagnostic = null)
        : this(configuration, CreateDefaultPort, recordDiagnostic)
    {
    }

    // 测试 / 受控验证构造：可注入 ISerialPort 实现。
    internal MainControllerSerialTransport(
        MainControllerConnectionOptions configuration,
        Func<ISerialPort> portFactory,
        Action<MainControllerTransportDiagnostic>? recordDiagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(portFactory);
        this.configuration = configuration;
        this.portFactory = portFactory;
        this.recordDiagnostic = recordDiagnostic;
        this.readTimeout = configuration.ReadTimeoutMilliseconds > 0
            ? TimeSpan.FromMilliseconds(configuration.ReadTimeoutMilliseconds)
            : TimeSpan.FromSeconds(2);
        this.writeTimeout = configuration.WriteTimeoutMilliseconds > 0
            ? TimeSpan.FromMilliseconds(configuration.WriteTimeoutMilliseconds)
            : TimeSpan.FromSeconds(2);
    }

    public async Task<DeviceByteTransportResult> ExchangeAsync(
        DeviceByteTransportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Endpoint, DeviceByteTransportEndpoints.MainController, StringComparison.Ordinal))
        {
            return Failure(
                DeviceByteTransportStatuses.Failed,
                "main_controller_endpoint_not_supported",
                $"Main-controller serial transport does not serve endpoint '{request.Endpoint}'.");
        }

        if (!configuration.IsConfigured)
        {
            return Failure(
                DeviceByteTransportStatuses.NotConnected,
                "main_controller_not_configured",
                "Main-controller serial port is not configured (PortName is missing).");
        }

        // 只读边界：解码请求帧并校验白名单。
        // 协议中主机查询与控制命令都使用 RequestType（0x01）；因此不能按 MessageType
        // 区分“读/写”，只能按 (ParentClass, SubClass) 白名单判定本阶段允许的命令。
        IceImmunoFrame requestFrame;
        try
        {
            requestFrame = IceImmunoSerialProtocol.DecodeFrame(request.RequestBytes);
        }
        catch (IceImmunoProtocolException exception)
        {
            Diagnostic(MainControllerTransportDirection.None, request.RequestBytes, null, "invalid_request_frame", exception.Message);
            return Failure(
                DeviceByteTransportStatuses.InvalidFrame,
                $"main_controller_{exception.Error}",
                exception.Message);
        }

        if (requestFrame.MessageType != IceImmunoSerialProtocol.RequestType)
        {
            // 主机只会发送 RequestType（0x01）帧；ResponseType（0x02）是设备返回。
            Diagnostic(MainControllerTransportDirection.None, request.RequestBytes, null, "invalid_request_type", null);
            return Failure(
                DeviceByteTransportStatuses.InvalidFrame,
                "main_controller_invalid_request_type",
                $"Request message type must be 0x01; received 0x{requestFrame.MessageType:X2}.");
        }

        if (!IsApprovedMainControllerCommand(requestFrame))
        {
            // 白名单之外的命令（其他读命令、未开放的写/控制命令、非法 payload）一律拒绝，不发出字节。
            // 当前白名单：SystemClass 0x01/0x08、0x01/0x09；HeatingClass 0x04/0x09~0x0B；CoolingClass 0x03/0x01~0x06。
            // 写操作（RESET、加热写、PWM、混匀、IO、扫码启动）仍保持拒绝。
            Diagnostic(MainControllerTransportDirection.None, request.RequestBytes, null, "command_not_supported", null);
            return Failure(
                DeviceByteTransportStatuses.Failed,
                "main_controller_command_not_supported",
                $"Command 0x{requestFrame.ParentClass:X2}/0x{requestFrame.SubClass:X2} is not on the approved main-controller whitelist. " +
                "Allowed: TL_SYS_GET_WORK_STATUS (0x01/0x08), TL_SYS_GET_NODE_STATUS (0x01/0x09), " +
                "heating read commands 0x04/0x09, 0x04/0x0A, 0x04/0x0B with board id 0..3, " +
                "and cooling commands 0x03/0x01..0x06 (reads empty payload; 0x03/0x04 target UINT16 0..40, 0x03/0x06 switch UINT16 0/1).");
        }

        return await SendAndReceiveAsync(request.Operation, request.RequestBytes, requestFrame, cancellationToken);
    }

    public Task<DeviceByteTransportResult> ReceiveAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        // 被动接收（PUT 上报）只在端点匹配且已配置时监听字节流，不发送任何写命令。
        if (!string.Equals(endpoint, DeviceByteTransportEndpoints.MainController, StringComparison.Ordinal))
        {
            return Task.FromResult(Failure(
                DeviceByteTransportStatuses.Failed,
                "main_controller_endpoint_not_supported",
                $"Main-controller serial transport does not serve endpoint '{endpoint}'."));
        }

        if (!configuration.IsConfigured)
        {
            return Task.FromResult(Failure(
                DeviceByteTransportStatuses.NotConnected,
                "main_controller_not_configured",
                "Main-controller serial port is not configured (PortName is missing)."));
        }

        return ReceivePutReportAsync(cancellationToken);
    }

    private async Task<DeviceByteTransportResult> SendAndReceiveAsync(
        string operation,
        byte[] requestBytes,
        IceImmunoFrame requestFrame,
        CancellationToken cancellationToken)
    {
        ISerialPort port;
        try
        {
            port = portFactory();
            ConfigurePort(port);
        }
        catch (Exception ex)
        {
            Diagnostic(MainControllerTransportDirection.None, requestBytes, null, "port_factory_failed", ex.Message);
            return Failure(
                DeviceByteTransportStatuses.CommunicationError,
                "main_controller_port_factory_failed",
                "Failed to create serial port: " + ex.Message);
        }

        try
        {
            await OpenAsync(port, cancellationToken);
        }
        catch (Exception)
        {
            Diagnostic(MainControllerTransportDirection.None, requestBytes, null, "open_failed", null);
            port.Dispose();
            return Failure(
                DeviceByteTransportStatuses.CommunicationError,
                "main_controller_open_failed",
                $"Failed to open COM port '{configuration.PortName}'. The port may not exist or is in use.");
        }

        try
        {
            port.DiscardInBuffer();

            Diagnostic(MainControllerTransportDirection.Tx, requestBytes, requestFrame, operation + "_tx", null);

            port.Write(requestBytes, 0, requestBytes.Length);

            var chunks = await ReadFramesAsync(port, requestBytes, operation, cancellationToken);
            var status = chunks.Status;
            if (status == DeviceByteTransportStatuses.Succeeded)
            {
                Diagnostic(MainControllerTransportDirection.Rx, chunks.Flattened, null, operation + "_rx_ok", chunks.CrcResult);
                return new DeviceByteTransportResult(DeviceByteTransportStatuses.Succeeded, chunks.Chunks);
            }

            Diagnostic(MainControllerTransportDirection.Rx, chunks.Flattened, null, operation + "_" + status, chunks.CrcResult);
            return new DeviceByteTransportResult(status, chunks.Chunks, chunks.ErrorCode, chunks.Message);
        }
        catch (OperationCanceledException)
        {
            return Failure(DeviceByteTransportStatuses.TimedOut, "main_controller_canceled", "Main-controller exchange was canceled.");
        }
        catch (TimeoutException)
        {
            return Failure(DeviceByteTransportStatuses.TimedOut, "main_controller_read_timeout", "Main-controller did not respond within the read timeout.");
        }
        catch (InvalidOperationException ex)
        {
            return Failure(DeviceByteTransportStatuses.Disconnected, "main_controller_disconnected", "Main-controller serial port disconnected during I/O: " + ex.Message);
        }
        finally
        {
            try
            {
                port.Close();
                Diagnostic(MainControllerTransportDirection.None, requestBytes, null, "port_closed", null);
            }
            catch
            {
                Diagnostic(MainControllerTransportDirection.None, requestBytes, null, "port_close_failed", null);
            }
            port.Dispose();
        }
    }

    private async Task<DeviceByteTransportResult> ReceivePutReportAsync(CancellationToken cancellationToken)
    {
        ISerialPort port;
        try
        {
            port = portFactory();
            ConfigurePort(port);
        }
        catch (Exception ex)
        {
            Diagnostic(MainControllerTransportDirection.None, null, null, "port_factory_failed", ex.Message);
            return Failure(DeviceByteTransportStatuses.CommunicationError, "main_controller_port_factory_failed", "Failed to create serial port: " + ex.Message);
        }

        try
        {
            await OpenAsync(port, cancellationToken);
        }
        catch (Exception)
        {
            Diagnostic(MainControllerTransportDirection.None, null, null, "open_failed", null);
            port.Dispose();
            return Failure(DeviceByteTransportStatuses.CommunicationError, "main_controller_open_failed", $"Failed to open COM port '{configuration.PortName}'.");
        }

        try
        {
            var chunks = await ReadFramesAsync(port, [], "receive-put", cancellationToken);
            if (chunks.Status == DeviceByteTransportStatuses.Succeeded)
            {
                Diagnostic(MainControllerTransportDirection.Rx, chunks.Flattened, null, "receive_put_rx_ok", chunks.CrcResult);
                return new DeviceByteTransportResult(DeviceByteTransportStatuses.Succeeded, chunks.Chunks);
            }

            Diagnostic(MainControllerTransportDirection.Rx, chunks.Flattened, null, "receive_put_" + chunks.Status, chunks.CrcResult);
            return new DeviceByteTransportResult(chunks.Status, chunks.Chunks, chunks.ErrorCode, chunks.Message);
        }
        catch (OperationCanceledException)
        {
            return Failure(DeviceByteTransportStatuses.TimedOut, "main_controller_canceled", "Main-controller receive was canceled.");
        }
        catch (TimeoutException)
        {
            return Failure(DeviceByteTransportStatuses.TimedOut, "main_controller_read_timeout", "Main-controller did not send a PUT report within the read timeout.");
        }
        catch (InvalidOperationException ex)
        {
            return Failure(DeviceByteTransportStatuses.Disconnected, "main_controller_disconnected", "Main-controller serial port disconnected during I/O: " + ex.Message);
        }
        finally
        {
            try
            {
                port.Close();
                Diagnostic(MainControllerTransportDirection.None, null, null, "port_closed", null);
            }
            catch
            {
                Diagnostic(MainControllerTransportDirection.None, null, null, "port_close_failed", null);
            }
            port.Dispose();
        }
    }

    // 读取至少一个完整协议帧；使用 IceImmunoFrameStreamDecoder 拆包并校验 CRC。
    //
    // 实现约束（不可回退）：超时与取消是不同语义——超时由 readTimeout 截止时间判定，
    // 返回 main_controller_no_response / main_controller_partial_timeout；只有调用方
    // 显式取消 cancellationToken 才返回 main_controller_canceled。绝不能用内部 linked
    // CTS 把超时误判成取消（会在负载下抖动）。每次单个 ReadByte 由 port.ReadTimeout
    // （readTimeout，最少 50ms）自然截断，循环内只检查 deadline 与调用方 token。
    private async Task<FrameReadResult> ReadFramesAsync(
        ISerialPort port,
        byte[] requestBytes,
        string operation,
        CancellationToken cancellationToken)
    {
        var decoder = new IceImmunoFrameStreamDecoder();
        var chunks = new List<byte[]>();
        var collected = new List<byte>();
        var deadline = DateTimeOffset.UtcNow + readTimeout;

        string? crcResult = null;

        while (true)
        {
            // 仅响应调用方取消；超时由下方 deadline 判定并返回对应状态码。
            cancellationToken.ThrowIfCancellationRequested();

            int value;
            try
            {
                value = await Task.Run(() => port.ReadByte(), cancellationToken);
            }
            catch (TimeoutException) when (decoder.BufferedByteCount == 0 && chunks.Count == 0)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    return new FrameReadResult(
                        DeviceByteTransportStatuses.TimedOut,
                        chunks,
                        collected.ToArray(),
                        "main_controller_no_response",
                        "Main-controller did not return any frame within the read timeout.",
                        crcResult);
                }

                continue;
            }
            catch (TimeoutException) when (chunks.Count > 0)
            {
                // 已收到至少一个完整帧，且在帧间静默内无后续数据：结束。
                return new FrameReadResult(DeviceByteTransportStatuses.Succeeded, chunks, collected.ToArray(), null, null, crcResult);
            }
            catch (TimeoutException)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    return new FrameReadResult(
                        DeviceByteTransportStatuses.TimedOut,
                        chunks,
                        collected.ToArray(),
                        "main_controller_partial_timeout",
                        "Main-controller response timed out before a complete frame arrived.",
                        crcResult);
                }

                continue;
            }

            if (value < 0)
            {
                return new FrameReadResult(
                    DeviceByteTransportStatuses.Disconnected,
                    chunks,
                    collected.ToArray(),
                    "main_controller_disconnected",
                    "Main-controller serial port returned end-of-stream.",
                    crcResult);
            }

            collected.Add((byte)value);
            var results = decoder.Feed(new[] { (byte)value });
            foreach (var result in results)
            {
                if (!result.Ok)
                {
                    crcResult ??= result.Error.ToString();
                    return new FrameReadResult(
                        DeviceByteTransportStatuses.InvalidFrame,
                        chunks,
                        collected.ToArray(),
                        $"main_controller_{result.Error}",
                        result.ErrorMessage ?? "Main-controller response frame is invalid.",
                        result.Error.ToString());
                }

                // 重新编码该帧作为返回块（保持与 ACK 解析一致的整帧字节）。
                var frame = result.Frame!;
                var encoded = IceImmunoSerialProtocol.EncodeFrame(
                    frame.ParentClass,
                    frame.SubClass,
                    frame.MessageType,
                    frame.Payload);
                chunks.Add(encoded);
                crcResult = "Ok";

                // 单次只读请求只取一个响应帧即可结束，避免无限等待。
                if (frame.MessageType == IceImmunoSerialProtocol.ResponseType)
                {
                    return new FrameReadResult(DeviceByteTransportStatuses.Succeeded, chunks, collected.ToArray(), null, null, crcResult);
                }
            }
        }
    }

    private void ConfigurePort(ISerialPort port)
    {
        port.PortName = configuration.PortName!;
        port.BaudRate = configuration.BaudRate;
        port.DataBits = configuration.DataBits;
        port.Parity = MapParity(configuration.Parity);
        port.StopBits = MapStopBits(configuration.StopBits);
        port.Handshake = MapHandshake(configuration.Handshake);
        port.ReadTimeout = Math.Max((int)readTimeout.TotalMilliseconds, 50);
        port.WriteTimeout = Math.Max((int)writeTimeout.TotalMilliseconds, 50);
    }

    // 已审核主控命令白名单（不再是纯只读边界）：按 (ParentClass, SubClass, Payload) 精确放行
    // 已评审的主控命令。协议中主机查询帧与控制/写帧的 MessageType 均为 RequestType（0x01），
    // 因此不能按 MessageType 区分读/写，只能逐条 (ParentClass, SubClass, Payload) 校验。
    //
    // 当前放行：
    //   - SystemClass 0x01：0x08（工作状态）、0x09（节点状态）
    //   - HeatingClass 0x04：0x09/0x0A/0x0B（板温度读取，payload 恰 1 字节 boardId ≤ 3）
    //   - CoolingClass 0x03：只读 0x01/0x02/0x03/0x05（payload 空）；写入 0x04（目标温度）、0x06（开关）
    //     payload 恰 2 字节 UINT16 LE，且值在协议量程内（温度 0..40，开关 0/1）
    // 其他命令（含 RESET、加热写、PWM、混匀、IO 等）一律拒绝，不发出字节。
    private static bool IsApprovedMainControllerCommand(IceImmunoFrame frame)
    {
        if (frame.MessageType != IceImmunoSerialProtocol.RequestType)
        {
            return false;
        }

        // SystemClass 只读命令
        if (frame.ParentClass == MainControllerProtocol.SystemClass
            && (frame.SubClass == 0x08 || frame.SubClass == 0x09))
        {
            return true;
        }

        // HeatingClass 温度读取：sub 0x09（当前温度）、0x0A（目标温度）、0x0B（开关状态）
        if (frame.ParentClass == MainControllerProtocol.HeatingClass
            && (frame.SubClass == 0x09 || frame.SubClass == 0x0A || frame.SubClass == 0x0B)
            && frame.Payload.Length == 1
            && frame.Payload[0] <= 3)
        {
            return true;
        }

        // CoolingClass（制冷，父类 0x03）：只读 0x01/0x02/0x03/0x05 payload 须为空；
        // 写入 0x04（目标温度 UINT16 LE 0..40）、0x06（开关 UINT16 LE 仅 0/1）payload 须恰 2 字节且量程合法。
        if (frame.ParentClass == MainControllerProtocol.CoolingClass)
        {
            return frame.SubClass switch
            {
                MainControllerProtocol.CoolingConnectionStatusSub => frame.Payload.Length == 0,
                MainControllerProtocol.CoolingCurrentTemperatureSub => frame.Payload.Length == 0,
                MainControllerProtocol.CoolingTargetTemperatureSub => frame.Payload.Length == 0,
                MainControllerProtocol.CoolingSwitchStateSub => frame.Payload.Length == 0,
                MainControllerProtocol.CoolingSetTargetTemperatureSub => IsValidCoolingTargetPayload(frame.Payload),
                MainControllerProtocol.CoolingSetSwitchStateSub => IsValidCoolingSwitchPayload(frame.Payload),
                _ => false
            };
        }

        return false;
    }

    private static bool IsValidCoolingTargetPayload(byte[] payload)
    {
        if (payload.Length != 2)
        {
            return false;
        }

        var celsius = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        return celsius <= 40;
    }

    private static bool IsValidCoolingSwitchPayload(byte[] payload)
    {
        if (payload.Length != 2)
        {
            return false;
        }

        var value = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        return value == 0 || value == 1;
    }

    private static Parity MapParity(MainControllerParity parity) => parity switch
    {
        MainControllerParity.None => Parity.None,
        MainControllerParity.Odd => Parity.Odd,
        MainControllerParity.Even => Parity.Even,
        MainControllerParity.Mark => Parity.Mark,
        MainControllerParity.Space => Parity.Space,
        _ => Parity.None
    };

    private static StopBits MapStopBits(MainControllerStopBits stopBits) => stopBits switch
    {
        MainControllerStopBits.One => StopBits.One,
        MainControllerStopBits.OnePointFive => StopBits.OnePointFive,
        MainControllerStopBits.Two => StopBits.Two,
        _ => StopBits.One
    };

    private static Handshake MapHandshake(MainControllerHandshake handshake) => handshake switch
    {
        MainControllerHandshake.None => Handshake.None,
        MainControllerHandshake.RequestToSend => Handshake.RequestToSend,
        MainControllerHandshake.RequestToSendXOnXOff => Handshake.RequestToSendXOnXOff,
        MainControllerHandshake.XOnXOff => Handshake.XOnXOff,
        _ => Handshake.None
    };

    private static Task OpenAsync(ISerialPort port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            port.Open();
        }, cancellationToken);
    }

    private void Diagnostic(
        MainControllerTransportDirection direction,
        byte[]? data,
        IceImmunoFrame? frame,
        string reason,
        string? detail)
    {
        if (recordDiagnostic is null)
        {
            return;
        }

        recordDiagnostic(new MainControllerTransportDiagnostic(
            DateTimeOffset.UtcNow,
            direction,
            data?.Length ?? 0,
            frame is null ? null : ((byte?)frame.ParentClass, (byte?)frame.SubClass, (byte?)frame.MessageType),
            reason,
            detail));
    }

    private static DeviceByteTransportResult Failure(string status, string code, string message) =>
        new(status, [], code, message);

    private static ISerialPort CreateDefaultPort() => new SystemIoSerialPortAdapter();

    private sealed record FrameReadResult(
        string Status,
        List<byte[]> Chunks,
        byte[] Flattened,
        string? ErrorCode,
        string? Message,
        string? CrcResult);
}

// 通讯诊断记录（仅包含协议维度信息；禁止记录本机用户名 / 路径 / 敏感设备信息）。
public sealed record MainControllerTransportDiagnostic(
    DateTimeOffset Timestamp,
    MainControllerTransportDirection Direction,
    int DataLength,
    (byte? ParentClass, byte? SubClass, byte? MessageType)? Command,
    string Reason,
    string? Detail);

public enum MainControllerTransportDirection
{
    None,
    Tx,
    Rx
}
