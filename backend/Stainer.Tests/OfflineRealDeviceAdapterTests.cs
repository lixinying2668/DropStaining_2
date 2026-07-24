using System.Text;
using System.IO.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Services;
using Stainer.Web.Infrastructure.Devices;

namespace Stainer.Tests;

public sealed class OfflineRealDeviceAdapterTests
{
    [Fact]
    public async Task Main_controller_read_boundary_sends_confirmed_frames_and_parses_all_supported_models()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        var adapter = new UnavailableRealDeviceAdapter(fake);
        var nodes = Enumerable.Repeat((byte)1, 64).ToArray();
        nodes[7] = 2;

        fake.EnqueueExchange(MainControllerProtocol.BuildWorkStatusRequest(), Response(0x01, 0x08, [0x01, 0x01]));
        fake.EnqueueExchange(MainControllerProtocol.BuildNodeStatusRequest(), Response(0x01, 0x09, [(byte)0x01, ..nodes]));
        fake.EnqueueExchange(MainControllerProtocol.BuildRunTimeRequest(), Response(0x01, 0x05, [1, 2, 3, 4]));
        fake.EnqueueExchange(
            MainControllerProtocol.BuildBoardTemperaturesRequest(2),
            Response(0x04, 0x09, [0x01, 2, 0xFB, 0xFF, 5, 0, 42, 0, 80, 0]));
        fake.EnqueueExchange(
            MainControllerProtocol.BuildBoardTargetTemperaturesRequest(2),
            Response(0x04, 0x0A, [0x01, 2, 1, 0, 2, 0, 3, 0, 4, 0]));
        fake.EnqueueExchange(
            MainControllerProtocol.BuildBoardSwitchStatesRequest(2),
            Response(0x04, 0x0B, [0x01, 2, 1, 0, 0, 0, 1, 0, 0, 0]));
        fake.EnqueueReceive(
            DeviceByteTransportEndpoints.MainController,
            Request(0x05, 0x04, [3, 1, 0]));
        fake.EnqueueExchange(
            MainControllerProtocol.BuildPwmSpeedsRequest(),
            Response(0x07, 0x06, [1, 0, 2, 0, 3, 0, 4, 0]));
        fake.EnqueueExchange(MainControllerProtocol.BuildMixerOriginRequest(1), Response(0x0A, 0x02, [1, 0, 0]));
        fake.EnqueueExchange(MainControllerProtocol.BuildMixerRemainingCountRequest(1), Response(0x0A, 0x03, [1, 9, 0]));
        fake.EnqueueExchange(MainControllerProtocol.BuildQrScanStatusRequest(), Response(0x08, 0x06, [1, 0]));
        fake.EnqueueExchange(
            MainControllerProtocol.BuildQrTextRequest(),
            Response(0x08, 0x01, Encoding.ASCII.GetBytes("ch1\r\nABC\r\n")));

        var work = await adapter.ReadControllerWorkStatusAsync();
        var nodeStatuses = await adapter.ReadControllerNodeStatusesAsync();
        var runTime = await adapter.ReadControllerRunTimeAsync();
        var temperatures = await adapter.ReadTemperaturesAsync(2);
        var targetTemperatures = await adapter.ReadTargetTemperaturesAsync(2);
        var switches = await adapter.ReadTemperatureSwitchesAsync(2);
        var liquid = await adapter.ReceiveLiquidLevelStatusAsync();
        var pwm = await adapter.ReadPwmSpeedsAsync();
        var origin = await adapter.ReadMixerOriginAsync(1);
        var remaining = await adapter.ReadMixerRemainingCountAsync(1);
        var qrStatus = await adapter.ReadQrScanStatusAsync();
        var qrText = await adapter.ReadQrTextAsync();

        Assert.Equal(1, work.Value!.Value);
        Assert.Equal(2, nodeStatuses.Value!.Values[7]);
        Assert.Equal<byte>([1, 2, 3, 4], runTime.Value!.RawValue);
        Assert.Equal<short>([-5, 5, 42, 80], temperatures.Value!.ValuesCelsius);
        Assert.Equal<short>([1, 2, 3, 4], targetTemperatures.Value!.ValuesCelsius);
        Assert.Equal<ushort>([1, 0, 1, 0], switches.Value!.Values);
        Assert.Equal(1, liquid.Value!.Value);
        Assert.Equal<ushort>([1, 2, 3, 4], pwm.Value!.ValuesRpm);
        Assert.Equal(0, origin.Value!.Value);
        Assert.Equal(9, remaining.Value!.Value);
        Assert.Equal(1, qrStatus.Value!.Value);
        Assert.Equal("ch1\r\nABC\r\n", qrText.Value!.Text);

        Assert.Equal(11, fake.ExchangeRequests.Count);
        Assert.Single(fake.ReceiveEndpoints);
        Assert.All(fake.ExchangeRequests, request =>
            Assert.Equal(DeviceByteTransportEndpoints.MainController, request.Endpoint));
    }

    [Fact]
    public async Task Main_controller_boundary_handles_ack_put_partial_sticky_crc_timeout_and_disconnect()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        var adapter = new UnavailableRealDeviceAdapter(fake);
        var ack = Response(0x01, 0x04, [0x01]);
        var put = Request(0x08, 0x03, Encoding.ASCII.GetBytes("ch1\r\nABC\r\n"));
        var response = Response(0x01, 0x08, [0x01, 0x01]);
        var sticky = ack.Concat(put).Concat(response).ToArray();
        fake.EnqueueExchange(
            MainControllerProtocol.BuildWorkStatusRequest(),
            sticky[..5],
            sticky[5..17],
            sticky[17..]);

        var combined = await adapter.ReadControllerWorkStatusAsync();
        Assert.True(combined.Ok, combined.Message);
        Assert.Single(combined.Acknowledgements);
        Assert.True(combined.Acknowledgements[0].Succeeded);
        var putReport = Assert.Single(combined.PutReports);
        var qr = Assert.IsType<MainControllerQrText>(putReport.Value);
        Assert.Equal("ch1\r\nABC\r\n", qr.Text);

        var badCrc = Response(0x01, 0x08, [0x01, 0x01]);
        badCrc[^3] ^= 0x01;
        fake.EnqueueExchange(MainControllerProtocol.BuildWorkStatusRequest(), badCrc);
        var crc = await adapter.ReadControllerWorkStatusAsync();
        Assert.False(crc.Ok);
        Assert.Contains(nameof(IceImmunoProtocolError.CrcMismatch), crc.ErrorCode);

        fake.EnqueueExchangeResult(
            MainControllerProtocol.BuildWorkStatusRequest(),
            new DeviceByteTransportResult(DeviceByteTransportStatuses.TimedOut, [], "controller_timeout", "Timed out."));
        var timeout = await adapter.ReadControllerWorkStatusAsync();
        Assert.Equal(DeviceCommandStatuses.TimedOut, timeout.Status);

        fake.EnqueueExchangeResult(
            MainControllerProtocol.BuildWorkStatusRequest(),
            new DeviceByteTransportResult(DeviceByteTransportStatuses.Disconnected, [], "controller_disconnected", "Disconnected."));
        var disconnected = await adapter.ReadControllerWorkStatusAsync();
        Assert.Equal(DeviceCommandStatuses.Offline, disconnected.Status);
    }

    [Fact]
    public async Task Dcr55_boundary_prepares_only_explicit_terminators_and_receives_success_invalid_and_timeout()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        var adapter = new UnavailableRealDeviceAdapter(fake);

        var missing = adapter.PrepareDcr55Trigger(Dcr55TriggerMode.Single, null);
        Assert.False(missing.Ok);
        Assert.Equal(DeviceCommandStatuses.NotConfigured, missing.Status);
        Assert.Empty(missing.CommandBytes);
        Assert.False(adapter.PrepareDcr55Trigger(Dcr55TriggerMode.Single, []).Ok);

        var prepared = adapter.PrepareDcr55Trigger(Dcr55TriggerMode.Single, [0x03]);
        Assert.True(prepared.Ok);
        Assert.False(prepared.Sent);
        Assert.Equal(
            Encoding.ASCII.GetBytes(Dcr55Protocol.SingleTriggerCommandText).Append((byte)0x03),
            prepared.CommandBytes);
        Assert.Empty(fake.ExchangeRequests);

        fake.EnqueueReceive(DeviceByteTransportEndpoints.Dcr55, Encoding.ASCII.GetBytes("SAMPLE-001\r\n"));
        fake.EnqueueReceive(
            DeviceByteTransportEndpoints.Dcr55,
            Encoding.ASCII.GetBytes("SAMPLE-002\r\n"),
            Encoding.ASCII.GetBytes("SAMPLE-003\r\n"));
        fake.EnqueueReceiveResult(
            DeviceByteTransportEndpoints.Dcr55,
            new DeviceByteTransportResult(DeviceByteTransportStatuses.TimedOut, [], "dcr55_timeout", "No barcode."));

        var single = await adapter.ReceiveDcr55ResultAsync();
        Assert.Equal("SAMPLE-001", single.Value!.Barcode);
        Assert.Equal(Dcr55ScanStatus.Success, single.Value.Status);
        var multiple = await adapter.ReceiveDcr55ResultAsync();
        Assert.False(multiple.Ok);
        Assert.Equal(Dcr55ScanStatus.InvalidResponse, multiple.Value!.Status);
        var timeout = await adapter.ReceiveDcr55ResultAsync();
        Assert.False(timeout.Ok);
        Assert.Equal(DeviceCommandStatuses.TimedOut, timeout.Status);
        Assert.Equal(Dcr55ScanStatus.Timeout, timeout.Value!.Status);
    }

    [Fact]
    public async Task Cooling_reads_go_through_main_controller_endpoint()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        var adapter = new UnavailableRealDeviceAdapter(fake);

        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingConnectionStatusRequest(), Response(0x03, 0x01, [0x01, 0x01, 0x00]));
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingCurrentTemperatureRequest(), Response(0x03, 0x02, [0x01, 0x08, 0x00]));
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingTargetTemperatureRequest(), Response(0x03, 0x03, [0x01, 0x0A, 0x00]));
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingSwitchStateRequest(), Response(0x03, 0x05, [0x01, 0x01, 0x00]));

        Assert.True((await adapter.ReadCoolingConnectionStatusAsync()).Value!.IsConnected);
        Assert.Equal(8, (await adapter.ReadCoolingCurrentTemperatureAsync()).Value!.Celsius);
        Assert.Equal(10, (await adapter.ReadCoolingTargetTemperatureAsync()).Value!.Celsius);
        Assert.True((await adapter.ReadCoolingSwitchStateAsync()).Value!.IsEnabled);

        Assert.Equal(4, fake.ExchangeRequests.Count);
        Assert.All(fake.ExchangeRequests, request => Assert.Equal(DeviceByteTransportEndpoints.MainController, request.Endpoint));
    }

    [Fact]
    public async Task Cooling_snapshot_aggregates_four_reads_and_fails_closed_on_invalid_frame()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        var adapter = new UnavailableRealDeviceAdapter(fake);
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingConnectionStatusRequest(), Response(0x03, 0x01, [0x01, 0x01, 0x00]));
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingCurrentTemperatureRequest(), Response(0x03, 0x02, [0x01, 0x08, 0x00]));
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingTargetTemperatureRequest(), Response(0x03, 0x03, [0x01, 0x0A, 0x00]));
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingSwitchStateRequest(), Response(0x03, 0x05, [0x01, 0x01, 0x00]));

        var snapshot = await adapter.ReadCoolingSnapshotAsync();
        Assert.True(snapshot.Ok, snapshot.Message);
        Assert.True(snapshot.Value!.IsConnected);
        Assert.Equal(80, snapshot.Value.CurrentTemperatureDeciC);   // 8℃ × 10
        Assert.Equal(100, snapshot.Value.TargetTemperatureDeciC);   // 10℃ × 10
        Assert.True(snapshot.Value.IsEnabled);
        Assert.Equal(4, fake.ExchangeRequests.Count);

        // fail closed：目标温度帧 CRC 错 → 不拼凑半截快照
        var badFake = new InMemoryFakeDeviceByteTransport();
        var badAdapter = new UnavailableRealDeviceAdapter(badFake);
        badFake.EnqueueExchange(MainControllerProtocol.BuildCoolingConnectionStatusRequest(), Response(0x03, 0x01, [0x01, 0x01, 0x00]));
        badFake.EnqueueExchange(MainControllerProtocol.BuildCoolingCurrentTemperatureRequest(), Response(0x03, 0x02, [0x01, 0x08, 0x00]));
        var badCrc = Response(0x03, 0x03, [0x01, 0x0A, 0x00]);
        badCrc[^3] ^= 0x01;
        badFake.EnqueueExchange(MainControllerProtocol.BuildCoolingTargetTemperatureRequest(), badCrc);

        var failed = await badAdapter.ReadCoolingSnapshotAsync();
        Assert.False(failed.Ok);
        Assert.Contains(nameof(IceImmunoProtocolError.CrcMismatch), failed.ErrorCode ?? string.Empty);
        Assert.Equal(3, badFake.ExchangeRequests.Count); // 连接 + 当前 + 失败的目标，未再读开关
    }

    [Fact]
    public async Task SetCoolingAsync_writes_target_and_switch_then_reads_back_with_deci_c_conversion()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);

        var setTargetRequest = MainControllerProtocol.BuildSetCoolingTargetTemperatureRequest(8); // 0x03/0x04, 08 00
        var setSwitchRequest = MainControllerProtocol.BuildSetCoolingSwitchStateRequest(true);     // 0x03/0x06, 01 00

        fake.EnqueueExchange(setTargetRequest, Response(0x03, 0x04, [0x01]));
        fake.EnqueueExchange(setSwitchRequest, Response(0x03, 0x06, [0x01]));
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingConnectionStatusRequest(), Response(0x03, 0x01, [0x01, 0x01, 0x00]));
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingCurrentTemperatureRequest(), Response(0x03, 0x02, [0x01, 0x09, 0x00])); // 9℃
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingTargetTemperatureRequest(), Response(0x03, 0x03, [0x01, 0x08, 0x00]));   // 8℃
        fake.EnqueueExchange(MainControllerProtocol.BuildCoolingSwitchStateRequest(), Response(0x03, 0x05, [0x01, 0x01, 0x00]));

        var result = await adapter.SetCoolingAsync(CoolingRequest(80, true));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(DeviceCommandStatuses.Succeeded, result.Status);

        Assert.Equal(6, fake.ExchangeRequests.Count);
        Assert.All(fake.ExchangeRequests, request => Assert.Equal(DeviceByteTransportEndpoints.MainController, request.Endpoint));

        // targetTemperatureDeciC=80 → 线上 payload 必须是 08 00（8℃），不是 50 00 或 80 00。
        var setTargetFrame = IceImmunoSerialProtocol.DecodeFrame(fake.ExchangeRequests[0].RequestBytes);
        Assert.Equal(MainControllerProtocol.CoolingClass, setTargetFrame.ParentClass);
        Assert.Equal(MainControllerProtocol.CoolingSetTargetTemperatureSub, setTargetFrame.SubClass);
        Assert.Equal<byte>([0x08, 0x00], setTargetFrame.Payload);

        Assert.Equal(90, Convert.ToInt32(result.Data["currentTemperatureDeciC"]));  // 9℃ × 10
        Assert.Equal(80, Convert.ToInt32(result.Data["targetTemperatureDeciC"]));   // 8℃ × 10
        Assert.True(Convert.ToBoolean(result.Data["isEnabled"]));
        Assert.True(Convert.ToBoolean(result.Data["isConnected"]));
    }

    [Theory]
    [InlineData(85)]   // 非整度
    [InlineData(-10)]  // 负值
    [InlineData(410)]  // 超 40℃
    public async Task SetCoolingAsync_rejects_out_of_protocol_range_without_any_io(int targetDeciC)
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);

        var result = await adapter.SetCoolingAsync(CoolingRequest(targetDeciC, true));
        Assert.False(result.Ok);
        Assert.Equal("cooling_target_out_of_range", result.ErrorCode);
        Assert.Empty(fake.ExchangeRequests); // 未发送任何字节
    }

    [Fact]
    public async Task SetCoolingAsync_fails_closed_when_target_write_ack_fails()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);
        // 目标温度写入 ack 失败（ack 字节 0x02）→ 开关写入与回读都不应发生。
        fake.EnqueueExchange(MainControllerProtocol.BuildSetCoolingTargetTemperatureRequest(8), Response(0x03, 0x04, [0x02]));

        var result = await adapter.SetCoolingAsync(CoolingRequest(80, true));
        Assert.False(result.Ok);
        Assert.Single(fake.ExchangeRequests);
    }

    [Fact]
    public async Task ScanReagentAsync_start_scan_writes_qr_start_frame_and_confirms_ack()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);
        fake.EnqueueExchange(
            MainControllerProtocol.BuildQrStartScanRequest(),
            Response(MainControllerProtocol.QrClass, MainControllerProtocol.QrStartScanSub, [0x01]));

        var result = await adapter.ScanReagentAsync(RequestFor(DeviceModules.ReagentScanner, ReagentQrCommands.StartScan));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(DeviceCommandStatuses.Succeeded, result.Status);
        Assert.True(result.Acknowledged);
        Assert.Single(fake.ExchangeRequests);
        Assert.All(fake.ExchangeRequests, request => Assert.Equal(DeviceByteTransportEndpoints.MainController, request.Endpoint));

        // 线上帧必须是 0x08/0x04 空 payload（启动试剂扫码），不是 DCR55 或带数据。
        var frame = IceImmunoSerialProtocol.DecodeFrame(fake.ExchangeRequests[0].RequestBytes);
        Assert.Equal(MainControllerProtocol.QrClass, frame.ParentClass);
        Assert.Equal(MainControllerProtocol.QrStartScanSub, frame.SubClass);
        Assert.Empty(frame.Payload);
    }

    [Fact]
    public async Task ScanReagentAsync_reset_scan_writes_qr_reset_frame_and_confirms_ack()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);
        fake.EnqueueExchange(
            MainControllerProtocol.BuildQrResetScanRequest(),
            Response(MainControllerProtocol.QrClass, MainControllerProtocol.QrResetScanSub, [0x01]));

        var result = await adapter.ScanReagentAsync(RequestFor(DeviceModules.ReagentScanner, ReagentQrCommands.ResetScan));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(DeviceCommandStatuses.Succeeded, result.Status);
        Assert.True(result.Acknowledged);
        var frame = IceImmunoSerialProtocol.DecodeFrame(fake.ExchangeRequests[0].RequestBytes);
        Assert.Equal(MainControllerProtocol.QrResetScanSub, frame.SubClass);
        Assert.Empty(frame.Payload);
    }

    [Theory]
    [InlineData("reagent.stateChanged")]   // 试剂状态通知（ReagentHardwareSink 用）须保持 fail-closed，不误触发扫码
    [InlineData("TL_QR_GET_TEXT")]
    [InlineData("unknown")]
    public async Task ScanReagentAsync_rejects_non_start_reset_actions_without_io(string action)
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);

        var result = await adapter.ScanReagentAsync(RequestFor(DeviceModules.ReagentScanner, action));

        Assert.False(result.Ok);
        Assert.Equal(DeviceCommandStatuses.NotSupported, result.Status);
        Assert.Empty(fake.ExchangeRequests); // 未发送任何字节
    }

    [Fact]
    public async Task RunPumpAsync_set_pwm_writes_single_channel_frame_and_confirms_ack()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);
        fake.EnqueueExchange(
            MainControllerProtocol.BuildSetPwmValueRequest(1, 60),
            Response(MainControllerProtocol.PwmClass, MainControllerProtocol.PwmSetIdValueSub, [0x01, 0x01]));

        var result = await adapter.RunPumpAsync(PumpRequest("set-pwm", 1, 60));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(DeviceCommandStatuses.Succeeded, result.Status);
        Assert.True(result.Acknowledged);
        Assert.Single(fake.ExchangeRequests);
        var frame = IceImmunoSerialProtocol.DecodeFrame(fake.ExchangeRequests[0].RequestBytes);
        Assert.Equal(MainControllerProtocol.PwmClass, frame.ParentClass);
        Assert.Equal(MainControllerProtocol.PwmSetIdValueSub, frame.SubClass);
        // 线上 payload 必须是 [pwmId=1][value=60 int16 LE = 3C 00]。
        Assert.Equal<byte>([0x01, 0x3C, 0x00], frame.Payload);
    }

    [Fact]
    public async Task RunPumpAsync_business_wash_maps_pwm_channel_code_to_single_channel_frame()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);
        fake.EnqueueExchange(
            MainControllerProtocol.BuildSetPwmValueRequest(2, -40),
            Response(MainControllerProtocol.PwmClass, MainControllerProtocol.PwmSetIdValueSub, [0x01, 0x02]));

        var result = await adapter.RunPumpAsync(new DeviceOperationRequest(
            new DeviceCommandContext("cmd-pwm-business-wash", null, "test", nameof(OfflineRealDeviceAdapterTests)),
            DeviceModules.Pump,
            "Wash",
            new Dictionary<string, object?>
            {
                ["pwmChannelCode"] = "PWM2",
                ["speedPercent"] = -40
            }));

        Assert.True(result.Ok, result.Message);
        var frame = IceImmunoSerialProtocol.DecodeFrame(fake.ExchangeRequests[0].RequestBytes);
        Assert.Equal(MainControllerProtocol.PwmSetIdValueSub, frame.SubClass);
        Assert.Equal<byte>([0x02, 0xD8, 0xFF], frame.Payload);
    }

    [Fact]
    public async Task RunPumpAsync_business_wash_stop_maps_drawer_to_zero_pwm()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);
        fake.EnqueueExchange(
            MainControllerProtocol.BuildSetPwmValueRequest(3, 0),
            Response(MainControllerProtocol.PwmClass, MainControllerProtocol.PwmSetIdValueSub, [0x01, 0x03]));

        var result = await adapter.RunPumpAsync(new DeviceOperationRequest(
            new DeviceCommandContext("cmd-pwm-business-wash-stop", null, "test", nameof(OfflineRealDeviceAdapterTests)),
            DeviceModules.Pump,
            "WashStop",
            new Dictionary<string, object?>
            {
                ["drawerCode"] = "D"
            }));

        Assert.True(result.Ok, result.Message);
        var frame = IceImmunoSerialProtocol.DecodeFrame(fake.ExchangeRequests[0].RequestBytes);
        Assert.Equal<byte>([0x03, 0x00, 0x00], frame.Payload);
    }

    [Fact]
    public async Task RunPumpAsync_set_all_pwm_writes_four_channel_frame_and_confirms_ack()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);
        fake.EnqueueExchange(
            MainControllerProtocol.BuildSetAllPwmValuesRequest(new short[] { 10, 20, -30, 0 }),
            Response(MainControllerProtocol.PwmClass, MainControllerProtocol.PwmSetAllValueSub, [0x01]));

        var result = await adapter.RunPumpAsync(PumpAllRequest("set-all-pwm", new[] { 10, 20, -30, 0 }));

        Assert.True(result.Ok, result.Message);
        Assert.True(result.Acknowledged);
        var frame = IceImmunoSerialProtocol.DecodeFrame(fake.ExchangeRequests[0].RequestBytes);
        Assert.Equal(MainControllerProtocol.PwmSetAllValueSub, frame.SubClass);
        Assert.Equal(8, frame.Payload.Length);
        // [10=0A 00][20=14 00][-30=E2 FF][0=00 00]
        Assert.Equal<byte>([0x0A, 0x00, 0x14, 0x00, 0xE2, 0xFF, 0x00, 0x00], frame.Payload);
    }

    [Theory]
    [InlineData("drain")]
    [InlineData("detox")]
    [InlineData("unknown")]
    public async Task RunPumpAsync_rejects_non_pwm_actions_without_io(string action)
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);

        var result = await adapter.RunPumpAsync(RequestFor(DeviceModules.Pump, action));

        Assert.False(result.Ok);
        Assert.Equal(DeviceCommandStatuses.NotSupported, result.Status);
        Assert.Empty(fake.ExchangeRequests);
    }

    [Fact]
    public async Task ScanSampleAsync_reads_barcode_from_dcr55_transport()
    {
        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);
        // DCR55 扫码枪：触发后返回 CRLF 结尾的 ASCII 条码帧（样本/玻片条码由机械臂末端 DCR55 读取）。
        fake.EnqueueReceive(DeviceByteTransportEndpoints.Dcr55, Encoding.ASCII.GetBytes("TLG001\r\n"));

        var result = await adapter.ScanSampleAsync(RequestFor(DeviceModules.SampleScanner, "scan"));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(DeviceCommandStatuses.Succeeded, result.Status);
        Assert.True(result.Acknowledged);
        Assert.Equal("TLG001", result.Data["barcode"]);
        Assert.Equal("Dcr55", result.Data["scanSource"]);
        Assert.Equal(DeviceByteTransportEndpoints.Dcr55, Assert.Single(fake.ReceiveEndpoints));
    }

    private static DeviceOperationRequest PumpRequest(string action, int pwmId, int value) =>
        new(
            new DeviceCommandContext($"cmd-pwm-{action}-{pwmId}-{value}", null, "test", nameof(OfflineRealDeviceAdapterTests)),
            DeviceModules.Pump,
            action,
            new Dictionary<string, object?>
            {
                ["pwmId"] = pwmId,
                ["value"] = value
            });

    private static DeviceOperationRequest PumpAllRequest(string action, int[] values) =>
        new(
            new DeviceCommandContext($"cmd-pwm-{action}", null, "test", nameof(OfflineRealDeviceAdapterTests)),
            DeviceModules.Pump,
            action,
            new Dictionary<string, object?>
            {
                ["pwm0"] = values[0],
                ["pwm1"] = values[1],
                ["pwm2"] = values[2],
                ["pwm3"] = values[3]
            });

    private static DeviceOperationRequest CoolingRequest(int targetDeciC, bool isEnabled) =>
        new(
            new DeviceCommandContext($"cmd-cooling-{targetDeciC}-{isEnabled}", null, "test", nameof(OfflineRealDeviceAdapterTests)),
            DeviceModules.Cooling,
            "set-cooling",
            new Dictionary<string, object?>
            {
                ["targetTemperatureDeciC"] = targetDeciC,
                ["isEnabled"] = isEnabled
            });

    [Fact]
    public async Task Real_adapter_without_transport_is_not_configured_and_all_formal_control_paths_reject_without_io()
    {
        var unavailable = new UnavailableRealDeviceAdapter();
        var status = await unavailable.GetStatusAsync();
        Assert.False(status.Ready);
        Assert.Equal(DeviceConnectionStatuses.NotConfigured, Assert.Single(status.Modules).ConnectionStatus);
        var read = await unavailable.ReadControllerWorkStatusAsync();
        Assert.False(read.Ok);
        Assert.Equal(DeviceCommandStatuses.NotConfigured, read.Status);

        var fake = new InMemoryFakeDeviceByteTransport();
        IDeviceAdapter adapter = new UnavailableRealDeviceAdapter(fake);
        var requests = new[]
        {
            RequestFor(DeviceModules.Controller, "reset"),
            RequestFor(DeviceModules.Temperature, "set-target"),
            RequestFor(DeviceModules.Temperature, "set-switch"),
            RequestFor(DeviceModules.Pump, "write-pwm"),
            RequestFor(DeviceModules.Mixer, "start"),
            RequestFor(DeviceModules.LiquidLevel, "write-io"),
            RequestFor(DeviceModules.Pump, "drain"),
            RequestFor(DeviceModules.Pump, "detox"),
            RequestFor(DeviceModules.ReagentScanner, ReagentQrCommands.GetText), // 试剂扫码启动/复位已真发字节；此处用非扫码 action 验证其余试剂 action 仍 reject
            RequestFor(DeviceModules.Workflow, "execute")
        };

        var results = new[]
        {
            await adapter.InitializeModuleAsync(requests[0]),
            await adapter.SetTemperatureAsync(requests[1]),
            await adapter.SetTemperatureAsync(requests[2]),
            await adapter.RunPumpAsync(requests[3]),
            await adapter.MixAsync(requests[4]),
            await adapter.ReadLiquidLevelsAsync(requests[5]),
            await adapter.RunPumpAsync(requests[6]),
            await adapter.RunPumpAsync(requests[7]),
            await adapter.ScanReagentAsync(requests[8]),
            await adapter.ExecuteWorkflowActionAsync(requests[9])
        };

        Assert.All(results, result =>
        {
            Assert.False(result.Ok);
            Assert.Equal(DeviceCommandStatuses.NotSupported, result.Status);
            Assert.False(result.Acknowledged);
        });
        Assert.Empty(fake.ExchangeRequests);
        Assert.Empty(fake.ReceiveEndpoints);
    }

    [Fact]
    public async Task Real_mode_di_reuses_one_adapter_and_only_test_injected_fake_enables_offline_reads()
    {
        var root = Path.Combine(TestPaths.TempRoot, "stainer-real-boundary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fake = new InMemoryFakeDeviceByteTransport();
        fake.EnqueueExchange(MainControllerProtocol.BuildWorkStatusRequest(), Response(0x01, 0x08, [0x01, 0x01]));

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Device:Mode", DeviceModes.Real);
            builder.UseSetting("Device:HardwareAvailable", "true");
            builder.UseSetting("Device:UseMockWhenHardwareUnavailable", "false");
            builder.UseSetting("Device:StartupInitialization:Enabled", "false");
            builder.UseSetting("ConnectionStrings:StainerDatabase", $"Data Source={Path.Combine(root, "stainer.db")}");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Device:Mode"] = DeviceModes.Real,
                ["Device:HardwareAvailable"] = "true",
                ["Device:UseMockWhenHardwareUnavailable"] = "false",
                ["Device:StartupInitialization:Enabled"] = "false",
                ["ConnectionStrings:StainerDatabase"] = $"Data Source={Path.Combine(root, "stainer.db")}",
                ["MachineExecutor:LeasePath"] = Path.Combine(root, "machine-executor.lock"),
                ["Safety:LogDirectory"] = Path.Combine(root, "logs")
            }));
            builder.ConfigureServices(services => services.AddSingleton<IDeviceByteTransport>(fake));
        });

        var formal = factory.Services.GetRequiredService<IDeviceAdapter>();
        var reads = factory.Services.GetRequiredService<IRealDeviceReadAdapter>();
        Assert.IsType<UnavailableRealDeviceAdapter>(formal);
        Assert.Same(formal, reads);
        var status = await formal.GetStatusAsync();
        Assert.False(status.Ready);
        Assert.Equal(DeviceConnectionStatuses.Offline, Assert.Single(status.Modules).ConnectionStatus);
        Assert.True((await reads.ReadControllerWorkStatusAsync()).Ok);
    }

    [Fact]
    public async Task GetStatusAsync_with_MainControllerSerialTransport_returns_Disconnected_without_opening_port()
    {
        var diagnosticCount = 0;
        var serialTransport = new MainControllerSerialTransport(
            new MainControllerConnectionOptions { PortName = "COM_MCU05_TEST_NONEXISTENT" },
            _ => diagnosticCount++);
        var adapter = new UnavailableRealDeviceAdapter(serialTransport);
        var status = await adapter.GetStatusAsync();

        Assert.False(status.Ready);
        var module = Assert.Single(status.Modules);
        Assert.Equal("main-controller", module.ModuleCode);
        Assert.Equal(DeviceConnectionStatuses.Disconnected, module.ConnectionStatus);
        Assert.Null(module.CurrentParametersJson);
        Assert.Null(module.TargetParametersJson);
        Assert.Equal(0, diagnosticCount);
    }

    [Fact]
    public async Task GetStatusAsync_with_fake_MainControllerSerialTransport_does_not_create_or_open_serial_port()
    {
        var createCount = 0;
        var fakePort = new NeverOpenedSerialPort();
        var diagnostics = new List<MainControllerTransportDiagnostic>();
        var serialTransport = new MainControllerSerialTransport(
            new MainControllerConnectionOptions
            {
                PortName = "COM_MCU05_TEST_FAKE",
                BaudRate = 115200,
                DataBits = 8,
                Parity = MainControllerParity.None,
                StopBits = MainControllerStopBits.One,
                Handshake = MainControllerHandshake.None
            },
            () =>
            {
                createCount++;
                return fakePort;
            },
            diagnostics.Add);
        var adapter = new UnavailableRealDeviceAdapter(serialTransport);

        var status = await adapter.GetStatusAsync();

        Assert.False(status.Ready);
        Assert.Equal(DeviceConnectionStatuses.Disconnected, Assert.Single(status.Modules).ConnectionStatus);
        Assert.Null(status.Modules[0].CurrentParametersJson);
        Assert.Equal(0, createCount);
        Assert.False(fakePort.OpenCalled);
        Assert.Empty(diagnostics);
    }

    private static DeviceOperationRequest RequestFor(string moduleCode, string action) =>
        new(
            new DeviceCommandContext($"cmd-{moduleCode}-{action}", null, "test", nameof(OfflineRealDeviceAdapterTests)),
            moduleCode,
            action,
            new Dictionary<string, object?>());

    private static byte[] Response(byte parentClass, byte subClass, byte[] payload) =>
        IceImmunoSerialProtocol.EncodeFrame(parentClass, subClass, IceImmunoSerialProtocol.ResponseType, payload);

    private static byte[] Request(byte parentClass, byte subClass, byte[] payload) =>
        IceImmunoSerialProtocol.EncodeFrame(parentClass, subClass, IceImmunoSerialProtocol.RequestType, payload);

    private sealed class InMemoryFakeDeviceByteTransport : IDeviceByteTransport
    {
        private readonly Queue<ExchangeScript> exchangeScripts = new();
        private readonly Queue<ReceiveScript> receiveScripts = new();

        public string Name => nameof(InMemoryFakeDeviceByteTransport);
        public bool IsConfigured => true;
        public List<DeviceByteTransportRequest> ExchangeRequests { get; } = [];
        public List<string> ReceiveEndpoints { get; } = [];

        public void EnqueueExchange(byte[] expectedRequest, params byte[][] chunks) =>
            EnqueueExchangeResult(
                expectedRequest,
                new DeviceByteTransportResult(DeviceByteTransportStatuses.Succeeded, chunks));

        public void EnqueueExchangeResult(byte[] expectedRequest, DeviceByteTransportResult result) =>
            exchangeScripts.Enqueue(new ExchangeScript(expectedRequest, result));

        public void EnqueueReceive(string endpoint, params byte[][] chunks) =>
            EnqueueReceiveResult(endpoint, new DeviceByteTransportResult(DeviceByteTransportStatuses.Succeeded, chunks));

        public void EnqueueReceiveResult(string endpoint, DeviceByteTransportResult result) =>
            receiveScripts.Enqueue(new ReceiveScript(endpoint, result));

        public Task<DeviceByteTransportResult> ExchangeAsync(
            DeviceByteTransportRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exchangeScripts.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected transport exchange for {request.Operation}.");
            }

            var script = exchangeScripts.Dequeue();
            if (!request.RequestBytes.SequenceEqual(script.ExpectedRequest))
            {
                throw new InvalidOperationException(
                    $"Unexpected request bytes. Expected {Convert.ToHexString(script.ExpectedRequest)}, actual {Convert.ToHexString(request.RequestBytes)}.");
            }

            ExchangeRequests.Add(request);
            return Task.FromResult(script.Result);
        }

        public Task<DeviceByteTransportResult> ReceiveAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (receiveScripts.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected transport receive for {endpoint}.");
            }

            var script = receiveScripts.Dequeue();
            if (!string.Equals(endpoint, script.Endpoint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected receive endpoint {endpoint}; expected {script.Endpoint}.");
            }

            ReceiveEndpoints.Add(endpoint);
            return Task.FromResult(script.Result);
        }

        private sealed record ExchangeScript(byte[] ExpectedRequest, DeviceByteTransportResult Result);
        private sealed record ReceiveScript(string Endpoint, DeviceByteTransportResult Result);
    }

    private sealed class NeverOpenedSerialPort : ISerialPort
    {
        public string PortName { get; set; } = string.Empty;
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public Parity Parity { get; set; }
        public StopBits StopBits { get; set; }
        public Handshake Handshake { get; set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public bool IsOpen => false;
        public int BytesToRead => 0;
        public bool OpenCalled { get; private set; }

        public void Open()
        {
            OpenCalled = true;
            throw new InvalidOperationException("GetStatusAsync must not open the fake port.");
        }

        public void Close() { }
        public void Write(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("GetStatusAsync must not write to the fake port.");
        public int ReadByte() =>
            throw new InvalidOperationException("GetStatusAsync must not read from the fake port.");
        public void DiscardInBuffer() { }
        public void Dispose() { }
    }
}
