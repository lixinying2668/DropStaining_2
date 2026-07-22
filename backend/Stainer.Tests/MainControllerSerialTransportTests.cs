using System.IO.Ports;
using System.Text;
using Stainer.Web.Application.Devices;
using Stainer.Web.Infrastructure.Devices;

namespace Stainer.Tests;

// P1-03-01：主控（Main Controller）串口 Transport 的离线单元测试。
// 通过注入 ISerialPort 的可控假实现，验证只读边界、白名单、CRC 校验、
// 非法帧、超时、断开、写命令拒绝等关键路径；不打开任何真实 COM 口。
public sealed class MainControllerSerialTransportTests
{
    private static readonly MainControllerConnectionOptions Configured = new()
    {
        PortName = "TEST-MC-COM",
        BaudRate = 115200,
        DataBits = 8,
        Parity = MainControllerParity.None,
        StopBits = MainControllerStopBits.One,
        Handshake = MainControllerHandshake.None,
        ReadTimeoutMilliseconds = 300,
        WriteTimeoutMilliseconds = 300
    };

    [Fact]
    public void Transport_name_and_configuration_match_connection_options()
    {
        var transport = CreateTransport(FakeMainControllerSerialPort.Empty());

        Assert.Equal("main-controller-serial", transport.Name);
        Assert.True(transport.IsConfigured);

        var unconfigured = new MainControllerSerialTransport(new MainControllerConnectionOptions());
        Assert.False(unconfigured.IsConfigured);
    }

    [Fact]
    public async Task Exchange_with_non_main_controller_endpoint_is_rejected_without_opening_port()
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.Dcr55,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("main_controller_endpoint_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
    }

    [Fact]
    public async Task Exchange_when_not_configured_fails_closed_without_attempting_io()
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = new MainControllerSerialTransport(
            new MainControllerConnectionOptions(),
            () => port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.NotConnected, result.Status);
        Assert.Equal("main_controller_not_configured", result.ErrorCode);
        Assert.False(port.OpenCalled);
    }

    [Fact]
    public async Task Exchange_with_malformed_request_frame_returns_invalid_frame()
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                [0x00, 0x01, 0x02])); // 既不是合法帧，也非白名单

        Assert.Equal(DeviceByteTransportStatuses.InvalidFrame, result.Status);
        Assert.False(port.OpenCalled);
    }

    [Fact]
    public async Task Exchange_rejects_write_command_and_does_not_send_bytes()
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = CreateTransport(port);

        // RESET 主控属于写/控制命令（SystemClass / 0x01），必须在白名单外被拒绝。
        var resetFrame = IceImmunoSerialProtocol.BuildRequestFrame(MainControllerProtocol.SystemClass, 0x01);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "reset-controller",
                resetFrame));

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("main_controller_command_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
        Assert.Empty(port.WriteCalls);
    }

    [Fact]
    public async Task Exchange_rejects_other_read_commands_outside_whitelist()
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = CreateTransport(port);

        // 运行时间读取（SystemClass / 0x05）属其他读命令，本阶段不开放，保持拒绝。
        var runTimeRequest = MainControllerProtocol.BuildRunTimeRequest();

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-run-time",
                runTimeRequest));

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("main_controller_command_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
    }

    [Fact]
    public async Task Exchange_work_status_sends_request_and_parses_ack_response()
    {
        // 构造一个合法的 work-status 响应帧（SystemClass / 0x08 / ResponseType / 1 字节 payload）。
        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x08,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]); // ACK 成功

        var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var transport = CreateTransport(port);

        var request = MainControllerProtocol.BuildWorkStatusRequest();
        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                request));

        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
        Assert.Single(port.WriteCalls);
        Assert.Equal(request, port.WrittenBytes);
        Assert.True(port.OpenCalled);
        Assert.True(port.CloseCalled);
        Assert.True(port.Disposed);

        // 返回的整帧必须可被 ACK 解析。
        var frame = IceImmunoSerialProtocol.DecodeFrame(result.ResponseChunks.SelectMany(c => c).ToArray());
        var ack = MainControllerProtocol.ParseAck(frame);
        Assert.True(ack.Succeeded);
    }

    [Fact]
    public async Task Exchange_node_status_sends_request_and_parses_ack_response()
    {
        // 节点状态响应 payload 为 65 字节：1 字节 ack (0x01) + 64 字节业务数据。
        var payload = new[] { (byte)0x01 }.Concat(Enumerable.Repeat((byte)0x00, 64)).ToArray();
        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x09,
            IceImmunoSerialProtocol.ResponseType,
            payload);

        var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var transport = CreateTransport(port);

        var request = MainControllerProtocol.BuildNodeStatusRequest();
        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-node-statuses",
                request));

        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
        Assert.Single(port.WriteCalls);
        Assert.Equal(request, port.WrittenBytes);

        var frame = IceImmunoSerialProtocol.DecodeFrame(result.ResponseChunks.SelectMany(c => c).ToArray());
        var statuses = MainControllerProtocol.ParseNodeStatuses(frame);
        Assert.Equal(64, statuses.Values.Length);
    }

    [Fact]
    public async Task Exchange_returns_invalid_frame_when_crc_is_corrupt()
    {
        // 构造一帧 work-status 响应，然后翻转 CRC 中的一个字节制造 CRC 错误。
        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x08,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]).ToArray();
        responseBytes[^2] ^= 0xFF; // 破坏 CRC

        var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.InvalidFrame, result.Status);
        Assert.Contains("CrcMismatch", result.ErrorCode ?? string.Empty);
    }

    [Fact]
    public async Task Exchange_returns_invalid_frame_when_tail_is_missing()
    {
        // 构造一帧合法响应后移除尾字节 0x5A，制造非法帧。
        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x08,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]).ToList();
        responseBytes.RemoveAt(responseBytes.Count - 1); // 移除 0x5A
        // 补一个非 0x5A 的字节使长度满足解码器尝试，从而命中 InvalidTail。
        responseBytes.Add(0x00);

        var port = FakeMainControllerSerialPort.FromBytes(responseBytes.ToArray());
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.InvalidFrame, result.Status);
        Assert.Contains("InvalidTail", result.ErrorCode ?? string.Empty);
    }

    [Fact]
    public async Task Exchange_returns_timeout_when_device_never_responds()
    {
        var port = FakeMainControllerSerialPort.AlwaysTimeout();
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.TimedOut, result.Status);
        Assert.Equal("main_controller_no_response", result.ErrorCode);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task Exchange_returns_disconnected_when_port_returns_end_of_stream()
    {
        var port = FakeMainControllerSerialPort.ReturningEndOfStream();
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.Disconnected, result.Status);
        Assert.Equal("main_controller_disconnected", result.ErrorCode);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task Exchange_returns_communication_error_when_open_fails()
    {
        var port = FakeMainControllerSerialPort.Empty();
        port.ThrowOnOpen = new IOException("COM not present");
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.CommunicationError, result.Status);
        Assert.Equal("main_controller_open_failed", result.ErrorCode);
        Assert.Empty(port.WriteCalls);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task Exchange_returns_communication_error_when_port_factory_throws()
    {
        var transport = new MainControllerSerialTransport(
            Configured,
            () => throw new InvalidOperationException("port factory broken"));

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.CommunicationError, result.Status);
        Assert.Equal("main_controller_port_factory_failed", result.ErrorCode);
    }

    [Fact]
    public async Task Diagnostic_callback_records_tx_and_rx_without_sensitive_info()
    {
        var diagnostics = new List<MainControllerTransportDiagnostic>();

        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x08,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]);

        var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var transport = new MainControllerSerialTransport(
            Configured,
            () => port,
            diagnostics.Add);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);

        // 必须包含 TX 与 RX 两条诊断记录。
        Assert.Contains(diagnostics, d => d.Direction == MainControllerTransportDirection.Tx);
        Assert.Contains(diagnostics, d => d.Direction == MainControllerTransportDirection.Rx);

        // 诊断内容只能包含协议维度信息，禁止本机用户名 / 路径 / 敏感设备信息。
        foreach (var d in diagnostics)
        {
            Assert.DoesNotContain(Environment.UserName, d.Reason ?? string.Empty);
            Assert.DoesNotContain(Environment.UserName, d.Detail ?? string.Empty);
        }
    }

    [Fact]
    public async Task Receive_put_report_returns_success_for_valid_frame()
    {
        // 模拟 PUT 上报：OptocouplerClass / 0x04 / RequestType（设备主动上报）。
        var putReport = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.OptocouplerClass,
            0x04,
            IceImmunoSerialProtocol.RequestType,
            [0x01, 0x10, 0x20]);

        // Receive 场景不发送任何命令，设备数据应当在串口打开后即可被读取。
        var port = FakeMainControllerSerialPort.ImmediatelyReadable(putReport);
        var transport = CreateTransport(port);

        var result = await transport.ReceiveAsync(DeviceByteTransportEndpoints.MainController);

        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
        Assert.Empty(port.WriteCalls); // Receive 不发送任何命令
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task Composite_transport_routes_main_controller_and_dcr55_endpoints()
    {
        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x08,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]);

        var mcPort = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var mainController = new MainControllerSerialTransport(Configured, () => mcPort);

        var dcr55Port = new Dcr55EchoPort("ABC\r\n");
        var dcr55 = new Dcr55SerialTransport(
            new Dcr55ConnectionOptions { Port = "TEST-DCR55" },
            () => dcr55Port,
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(50));

        var composite = new CompositeDeviceByteTransport(mainController, dcr55);

        // MainController 路由
        var mcResult = await composite.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));
        Assert.Equal(DeviceByteTransportStatuses.Succeeded, mcResult.Status);

        // 未知 endpoint 拒绝
        var unknown = await composite.ReceiveAsync("standalone-cooling-v1.0");
        Assert.Equal(DeviceByteTransportStatuses.Failed, unknown.Status);
        Assert.Equal("composite_endpoint_not_configured", unknown.ErrorCode);
    }

    // ── MCU-01：端口生命周期 — 每次请求独立 Open→Close→Dispose ──

    [Fact]
    public async Task Exchange_opens_closes_and_disposes_port_per_single_request()
    {
        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x08,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]);

        var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
        Assert.True(port.OpenCalled);
        Assert.True(port.CloseCalled);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task Exchange_reopens_port_for_each_consecutive_request()
    {
        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x08,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]);

        // 两个独立 fake port 追踪各自生命周期
        var port1 = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var port2 = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var callIndex = 0;
        Func<FakeMainControllerSerialPort> nextPort = () => ++callIndex == 1 ? port1 : port2;

        var transport = new MainControllerSerialTransport(Configured, () => nextPort());

        var request = new DeviceByteTransportRequest(
            DeviceByteTransportEndpoints.MainController,
            "read-work-status",
            MainControllerProtocol.BuildWorkStatusRequest());

        var result1 = await transport.ExchangeAsync(request);
        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result1.Status);

        var result2 = await transport.ExchangeAsync(request);
        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result2.Status);

        // port1: 第一次请求 open→close→dispose
        Assert.True(port1.OpenCalled);
        Assert.True(port1.CloseCalled);
        Assert.True(port1.Disposed);

        // port2: 第二次请求 open→close→dispose
        Assert.True(port2.OpenCalled);
        Assert.True(port2.CloseCalled);
        Assert.True(port2.Disposed);
    }

    // ── MCU-03：温度白名单 — HeatingClass subs 0x09/0x0A/0x0B, boardId 0-3 ──

    [Fact]
    public async Task Exchange_allows_board_temperatures_request_for_valid_board_ids()
    {
        // boardId 0..3 应全部通过白名单并打开端口
        for (byte boardId = 0; boardId <= 3; boardId++)
        {
            var tempResponse = BuildTempResponse(MainControllerProtocol.HeatingClass, 0x09, boardId);
            var port = FakeMainControllerSerialPort.FromBytes(tempResponse);
            var transport = CreateTransport(port);

            var requestBytes = MainControllerProtocol.BuildBoardTemperaturesRequest(boardId);
            var result = await transport.ExchangeAsync(
                new DeviceByteTransportRequest(
                    DeviceByteTransportEndpoints.MainController,
                    "read-board-temperatures",
                    requestBytes));

            Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
            Assert.True(port.OpenCalled);
        }
    }

    [Fact]
    public async Task Exchange_allows_board_target_temperatures_request_for_valid_board_ids()
    {
        for (byte boardId = 0; boardId <= 3; boardId++)
        {
            var tempResponse = BuildTempResponse(MainControllerProtocol.HeatingClass, 0x0A, boardId);
            var port = FakeMainControllerSerialPort.FromBytes(tempResponse);
            var transport = CreateTransport(port);

            var requestBytes = MainControllerProtocol.BuildBoardTargetTemperaturesRequest(boardId);
            var result = await transport.ExchangeAsync(
                new DeviceByteTransportRequest(
                    DeviceByteTransportEndpoints.MainController,
                    "read-board-target-temperatures",
                    requestBytes));

            Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
            Assert.True(port.OpenCalled);
        }
    }

    [Fact]
    public async Task Exchange_allows_board_switch_states_request_for_valid_board_ids()
    {
        for (byte boardId = 0; boardId <= 3; boardId++)
        {
            var tempResponse = BuildTempResponse(MainControllerProtocol.HeatingClass, 0x0B, boardId);
            var port = FakeMainControllerSerialPort.FromBytes(tempResponse);
            var transport = CreateTransport(port);

            var requestBytes = MainControllerProtocol.BuildBoardSwitchStatesRequest(boardId);
            var result = await transport.ExchangeAsync(
                new DeviceByteTransportRequest(
                    DeviceByteTransportEndpoints.MainController,
                    "read-board-switch-states",
                    requestBytes));

            Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
            Assert.True(port.OpenCalled);
        }
    }

    [Fact]
    public async Task Exchange_rejects_temperature_request_with_board_id_above_three()
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = CreateTransport(port);

        var requestBytes = IceImmunoSerialProtocol.BuildRequestFrame(MainControllerProtocol.HeatingClass, 0x09, [0x04]);
        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-board-temperatures",
                requestBytes));

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("main_controller_command_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
    }

    [Fact]
    public async Task Exchange_rejects_temperature_request_with_empty_payload()
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = CreateTransport(port);

        var requestBytes = IceImmunoSerialProtocol.BuildRequestFrame(MainControllerProtocol.HeatingClass, 0x09);
        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-board-temperatures",
                requestBytes));

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("main_controller_command_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
    }

    [Fact]
    public async Task Exchange_rejects_temperature_request_with_two_byte_payload()
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = CreateTransport(port);

        var requestBytes = IceImmunoSerialProtocol.BuildRequestFrame(MainControllerProtocol.HeatingClass, 0x09, [0x00, 0x01]);
        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-board-temperatures",
                requestBytes));

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("main_controller_command_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
    }

    // ── CLG / MCU-06：制冷（CoolingClass 0x03）白名单 — 只读 0x01~0x03、0x05 空 payload；写入 0x04/0x06 校验 payload ──

    [Theory]
    [InlineData(MainControllerProtocol.CoolingConnectionStatusSub)]
    [InlineData(MainControllerProtocol.CoolingCurrentTemperatureSub)]
    [InlineData(MainControllerProtocol.CoolingTargetTemperatureSub)]
    [InlineData(MainControllerProtocol.CoolingSwitchStateSub)]
    public async Task Exchange_allows_cooling_read_commands_with_empty_payload(byte subClass)
    {
        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.CoolingClass,
            subClass,
            IceImmunoSerialProtocol.ResponseType,
            [0x01, 0x01, 0x00]);
        var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var transport = CreateTransport(port);

        var requestBytes = IceImmunoSerialProtocol.BuildRequestFrame(MainControllerProtocol.CoolingClass, subClass);
        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-cooling",
                requestBytes));

        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
        Assert.True(port.OpenCalled);
        Assert.Single(port.WriteCalls);
    }

    [Fact]
    public async Task Exchange_allows_cooling_target_write_with_valid_payload()
    {
        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.CoolingClass,
            MainControllerProtocol.CoolingSetTargetTemperatureSub,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]);
        var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var transport = CreateTransport(port);

        var request = MainControllerProtocol.BuildSetCoolingTargetTemperatureRequest(10); // 0A 00
        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "set-cooling-target",
                request));

        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
        Assert.True(port.OpenCalled);
        // 实际线上字节 payload 必须是 0A 00（10℃），不是 0A 单字节或 64 00。
        var written = IceImmunoSerialProtocol.DecodeFrame(port.WrittenBytes);
        Assert.Equal<byte>([0x0A, 0x00], written.Payload);
    }

    [Fact]
    public async Task Exchange_allows_reagent_qr_start_and_reset_scan_commands()
    {
        foreach (var request in new[] { MainControllerProtocol.BuildQrStartScanRequest(), MainControllerProtocol.BuildQrResetScanRequest() })
        {
            var subClass = IceImmunoSerialProtocol.DecodeFrame(request).SubClass;
            var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
                MainControllerProtocol.QrClass,
                subClass,
                IceImmunoSerialProtocol.ResponseType,
                [0x01]);
            var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
            var transport = CreateTransport(port);

            var result = await transport.ExchangeAsync(
                new DeviceByteTransportRequest(DeviceByteTransportEndpoints.MainController, "reagent-qr", request));

            Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
            Assert.True(port.OpenCalled);
            Assert.Single(port.WriteCalls);
        }
    }

    [Fact]
    public async Task Exchange_rejects_unapproved_reagent_qr_subclass_without_writing()
    {
        // 0x08/0x99 不在白名单：不得发字节、不得开端口。
        var request = IceImmunoSerialProtocol.BuildRequestFrame(MainControllerProtocol.QrClass, 0x99);
        var port = FakeMainControllerSerialPort.FromBytes([]);
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(DeviceByteTransportEndpoints.MainController, "reagent-qr-bad", request));

        Assert.NotEqual(DeviceByteTransportStatuses.Succeeded, result.Status);
        Assert.False(port.OpenCalled);
    }

    [Fact]
    public async Task Exchange_allows_wash_pwm_single_and_all_write_commands()
    {
        // 单通道 0x07/0x02
        var singleResponse = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.PwmClass,
            MainControllerProtocol.PwmSetIdValueSub,
            IceImmunoSerialProtocol.ResponseType,
            [0x01, 0x01]);
        var singlePort = FakeMainControllerSerialPort.FromBytes(singleResponse);
        var singleRequest = MainControllerProtocol.BuildSetPwmValueRequest(1, 60);
        var singleResult = await CreateTransport(singlePort).ExchangeAsync(
            new DeviceByteTransportRequest(DeviceByteTransportEndpoints.MainController, "set-wash-pwm", singleRequest));
        Assert.Equal(DeviceByteTransportStatuses.Succeeded, singleResult.Status);
        Assert.True(singlePort.OpenCalled);

        // 全通道 0x07/0x04
        var allResponse = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.PwmClass,
            MainControllerProtocol.PwmSetAllValueSub,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]);
        var allPort = FakeMainControllerSerialPort.FromBytes(allResponse);
        var allRequest = MainControllerProtocol.BuildSetAllPwmValuesRequest(new short[] { 10, 20, -30, 0 });
        var allResult = await CreateTransport(allPort).ExchangeAsync(
            new DeviceByteTransportRequest(DeviceByteTransportEndpoints.MainController, "set-wash-pwm-all", allRequest));
        Assert.Equal(DeviceByteTransportStatuses.Succeeded, allResult.Status);
    }

    [Fact]
    public async Task Exchange_rejects_invalid_wash_pwm_payload_without_writing()
    {
        // pwmId=4 越界（>3）：白名单须拒绝，不发字节、不开端口。
        var request = IceImmunoSerialProtocol.BuildRequestFrame(
            MainControllerProtocol.PwmClass,
            MainControllerProtocol.PwmSetIdValueSub,
            [0x04, 0x00, 0x00]);
        var port = FakeMainControllerSerialPort.FromBytes([]);
        var transport = CreateTransport(port);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(DeviceByteTransportEndpoints.MainController, "wash-pwm-bad", request));

        Assert.NotEqual(DeviceByteTransportStatuses.Succeeded, result.Status);
        Assert.False(port.OpenCalled);
    }

    [Fact]
    public async Task Exchange_allows_cooling_switch_write_with_valid_zero_or_one_payload()
    {
        foreach (var enabled in new[] { true, false })
        {
            var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
                MainControllerProtocol.CoolingClass,
                MainControllerProtocol.CoolingSetSwitchStateSub,
                IceImmunoSerialProtocol.ResponseType,
                [0x01]);
            var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
            var transport = CreateTransport(port);

            var request = MainControllerProtocol.BuildSetCoolingSwitchStateRequest(enabled);
            var result = await transport.ExchangeAsync(
                new DeviceByteTransportRequest(
                    DeviceByteTransportEndpoints.MainController,
                    "set-cooling-switch",
                    request));

            Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
            Assert.True(port.OpenCalled);
        }
    }

    [Theory]
    [InlineData(MainControllerProtocol.CoolingSetTargetTemperatureSub, new byte[] { 0x29, 0x00 })] // 目标 41℃ 超量程
    [InlineData(MainControllerProtocol.CoolingSetTargetTemperatureSub, new byte[] { 0x0A })]        // payload 长度错
    [InlineData(MainControllerProtocol.CoolingSetSwitchStateSub, new byte[] { 0x02, 0x00 })]       // 开关值 2 非法
    [InlineData(MainControllerProtocol.CoolingSetSwitchStateSub, new byte[] { 0x01 })]              // payload 长度错
    [InlineData((byte)0x07, new byte[] { })]                                                        // 未知制冷子命令 0x03/0x07
    public async Task Exchange_rejects_invalid_or_unapproved_cooling_commands_without_opening_port(
        byte subClass,
        byte[] payload)
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = CreateTransport(port);

        var requestBytes = IceImmunoSerialProtocol.BuildRequestFrame(MainControllerProtocol.CoolingClass, subClass, payload);
        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "cooling-command",
                requestBytes));

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("main_controller_command_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
        Assert.Empty(port.WriteCalls);
    }

    [Theory]
    [InlineData(0x04, 0x01, new byte[] { 0x00 })]
    [InlineData(0x04, 0x08, new byte[] { 0x00 })]
    [InlineData(0x05, 0x04, new byte[] { 0x00, 0x01, 0x00 })]
    [InlineData(0x07, 0x06, new byte[] { })]
    [InlineData(0x0A, 0x02, new byte[] { 0x00 })]
    [InlineData(0x08, 0x09, new byte[] { })]  // QR 未批准 subclass（0x08/0x04、0x08/0x05、0x08/0x06、0x08/0x01 已放行）
    public async Task Exchange_rejects_control_or_unapproved_commands_without_opening_port(
        byte parentClass,
        byte subClass,
        byte[] payload)
    {
        var port = FakeMainControllerSerialPort.Empty();
        var transport = CreateTransport(port);

        var requestBytes = IceImmunoSerialProtocol.BuildRequestFrame(parentClass, subClass, payload);
        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "unapproved-main-controller-command",
                requestBytes));

        Assert.Equal(DeviceByteTransportStatuses.Failed, result.Status);
        Assert.Equal("main_controller_command_not_supported", result.ErrorCode);
        Assert.False(port.OpenCalled);
        Assert.Empty(port.WriteCalls);
    }

    // ── MCU-05：port_closed / port_close_failed 诊断记录 ──

    [Fact]
    public async Task Exchange_records_port_closed_diagnostic_on_success()
    {
        var diagnostics = new List<MainControllerTransportDiagnostic>();

        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x08,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]);

        var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
        var transport = new MainControllerSerialTransport(
            Configured,
            () => port,
            diagnostics.Add);

        await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        Assert.Contains(diagnostics, d => d.Reason == "port_closed");
    }

    [Fact]
    public async Task Exchange_records_port_close_failed_diagnostic_when_close_throws()
    {
        var diagnostics = new List<MainControllerTransportDiagnostic>();

        var responseBytes = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.SystemClass,
            0x08,
            IceImmunoSerialProtocol.ResponseType,
            [0x01]);

        var port = FakeMainControllerSerialPort.FromBytes(responseBytes);
        port.ThrowOnClose = new InvalidOperationException("simulated close failure");
        var transport = new MainControllerSerialTransport(
            Configured,
            () => port,
            diagnostics.Add);

        var result = await transport.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                "read-work-status",
                MainControllerProtocol.BuildWorkStatusRequest()));

        // Exchange still succeeds because close failure doesn't affect already-read results
        Assert.Equal(DeviceByteTransportStatuses.Succeeded, result.Status);
        Assert.Contains(diagnostics, d => d.Reason == "port_close_failed");
        // Port is still disposed despite close failure
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task Receive_records_port_closed_diagnostic_on_success()
    {
        var diagnostics = new List<MainControllerTransportDiagnostic>();

        var putReport = IceImmunoSerialProtocol.EncodeFrame(
            MainControllerProtocol.OptocouplerClass,
            0x04,
            IceImmunoSerialProtocol.RequestType,
            [0x01, 0x10, 0x20]);

        var port = FakeMainControllerSerialPort.ImmediatelyReadable(putReport);
        var transport = new MainControllerSerialTransport(
            Configured,
            () => port,
            diagnostics.Add);

        await transport.ReceiveAsync(DeviceByteTransportEndpoints.MainController);

        Assert.Contains(diagnostics, d => d.Reason == "port_closed");
    }

    private static MainControllerSerialTransport CreateTransport(FakeMainControllerSerialPort port) =>
        new(Configured, () => port);

    private static byte[] BuildTempResponse(byte parentClass, byte subClass, byte boardId)
    {
        // 10 bytes: [0x01 ack, boardId, 4 x int16 values]
        var payload = new byte[10];
        payload[0] = 0x01; // ACK
        payload[1] = boardId;
        // remaining 8 bytes: 4 x int16 temperature values (zeros)
        return IceImmunoSerialProtocol.EncodeFrame(parentClass, subClass, IceImmunoSerialProtocol.ResponseType, payload);
    }

    // 主控假串口：可预置设备返回字节，并在写入触发命令后允许读取。
    private sealed class FakeMainControllerSerialPort : ISerialPort
    {
        private readonly Queue<byte> response;
        private readonly bool alwaysTimeout;
        private readonly bool endOfStream;
        private readonly bool immediatelyReadable;
        private bool responseAfterWrite;

        private FakeMainControllerSerialPort(IEnumerable<byte> bytes, bool alwaysTimeout, bool endOfStream, bool immediatelyReadable = false)
        {
            response = new Queue<byte>(bytes);
            this.alwaysTimeout = alwaysTimeout;
            this.endOfStream = endOfStream;
            this.immediatelyReadable = immediatelyReadable;
        }

        public string PortName { get; set; } = string.Empty;
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public Parity Parity { get; set; }
        public StopBits StopBits { get; set; }
        public Handshake Handshake { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public bool IsOpen { get; private set; }
        public int BytesToRead => response.Count;
        public List<byte[]> WriteCalls { get; } = [];
        public byte[] WrittenBytes => WriteCalls.SelectMany(c => c).ToArray();
        public bool OpenCalled { get; private set; }
        public bool CloseCalled { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? ThrowOnOpen { get; set; }
        public Exception? ThrowOnClose { get; set; }

        public static FakeMainControllerSerialPort Empty() => new([], false, false);

        public static FakeMainControllerSerialPort FromBytes(byte[] bytes) => new(bytes, false, false);

        public static FakeMainControllerSerialPort AlwaysTimeout() => new([], true, false);

        public static FakeMainControllerSerialPort ReturningEndOfStream() => new([], false, true);

        // 设备主动上报场景（Receive）：串口打开后即可读取，无需等待 Write 触发。
        public static FakeMainControllerSerialPort ImmediatelyReadable(byte[] bytes) =>
            new(bytes, false, false, immediatelyReadable: true);

        public void Open()
        {
            OpenCalled = true;
            if (ThrowOnOpen is not null)
            {
                throw ThrowOnOpen;
            }

            IsOpen = true;
        }

        public void Close()
        {
            CloseCalled = true;
            IsOpen = false;
            if (ThrowOnClose is not null)
            {
                throw ThrowOnClose;
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            var chunk = new byte[count];
            Array.Copy(buffer, offset, chunk, 0, count);
            WriteCalls.Add(chunk);
            responseAfterWrite = true;
        }

        public int ReadByte()
        {
            if (alwaysTimeout)
            {
                throw new TimeoutException();
            }

            if (!responseAfterWrite && !immediatelyReadable)
            {
                throw new TimeoutException();
            }

            if (response.Count > 0)
            {
                return response.Dequeue();
            }

            if (endOfStream)
            {
                return -1;
            }

            throw new TimeoutException();
        }

        public void DiscardInBuffer() { }

        public void Dispose() => Disposed = true;
    }

    // 专用于组合路由测试的最小 DCR55 假串口：触发后返回固定条码文本。
    private sealed class Dcr55EchoPort : ISerialPort
    {
        private readonly Queue<int> response;

        public Dcr55EchoPort(string text)
        {
            response = new Queue<int>(Encoding.ASCII.GetBytes(text).Select(b => (int)b));
        }

        public string PortName { get; set; } = string.Empty;
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public Parity Parity { get; set; }
        public StopBits StopBits { get; set; }
        public Handshake Handshake { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public bool IsOpen { get; private set; }
        public int BytesToRead => response.Count;
        public bool OpenCalled { get; private set; }
        public bool CloseCalled { get; private set; }
        public bool Disposed { get; private set; }

        public void Open()
        {
            OpenCalled = true;
            IsOpen = true;
        }

        public void Close()
        {
            CloseCalled = true;
            IsOpen = false;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            // 触发命令已发送：之后允许读取。
        }

        public int ReadByte() =>
            response.Count > 0 ? response.Dequeue() : throw new TimeoutException();

        public void DiscardInBuffer() { }

        public void Dispose() => Disposed = true;
    }
}
