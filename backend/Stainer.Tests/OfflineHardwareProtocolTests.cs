using System.Text;
using Stainer.Web.Application.Devices;

namespace Stainer.Tests;

public sealed class OfflineHardwareProtocolTests
{
    [Fact]
    public void Main_controller_crc_and_fixed_request_frame_match_v104_fixture()
    {
        Assert.Equal(
            0x4B37,
            IceImmunoSerialProtocol.CalculateCrc16Modbus(Encoding.ASCII.GetBytes("123456789")));

        var expected = Convert.FromHexString("A501030008040133025A");
        var encoded = IceImmunoSerialProtocol.BuildRequestFrame(0x08, 0x04);

        Assert.Equal(expected, encoded);
        var decoded = IceImmunoSerialProtocol.DecodeFrame(expected);
        Assert.Equal(0x08, decoded.ParentClass);
        Assert.Equal(0x04, decoded.SubClass);
        Assert.Equal(IceImmunoSerialProtocol.RequestType, decoded.MessageType);
        Assert.Empty(decoded.Payload);
    }

    [Fact]
    public void Main_controller_stream_decoder_buffers_partial_frame_and_splits_sticky_frames()
    {
        var first = Convert.FromHexString("A50104000108020140BA5A");
        var second = Convert.FromHexString("A5010500080602010065195A");
        var decoder = new IceImmunoFrameStreamDecoder();

        Assert.Empty(decoder.Feed(first.AsSpan(0, 5)));
        Assert.Equal(5, decoder.BufferedByteCount);

        var joined = first.AsSpan(5).ToArray().Concat(second).ToArray();
        var decoded = decoder.Feed(joined);

        Assert.Equal(2, decoded.Count);
        Assert.All(decoded, result => Assert.True(result.Ok, result.ErrorMessage));
        Assert.Equal((byte)0x08, decoded[0].Frame!.SubClass);
        Assert.Equal((byte)0x06, decoded[1].Frame!.SubClass);
        Assert.Equal(0, decoder.BufferedByteCount);
    }

    [Fact]
    public void Main_controller_rejects_abnormal_length_crc_and_tail()
    {
        var invalidLength = Convert.FromHexString("A501020001080000005A");
        var lengthError = Assert.Throws<IceImmunoProtocolException>(() =>
            IceImmunoSerialProtocol.DecodeFrame(invalidLength));
        Assert.Equal(IceImmunoProtocolError.InvalidLength, lengthError.Error);

        var invalidCrc = Convert.FromHexString("A50104000108020141BA5A");
        var crcError = Assert.Throws<IceImmunoProtocolException>(() =>
            IceImmunoSerialProtocol.DecodeFrame(invalidCrc));
        Assert.Equal(IceImmunoProtocolError.CrcMismatch, crcError.Error);

        var invalidTail = Convert.FromHexString("A50104000108020140BA00");
        var tailError = Assert.Throws<IceImmunoProtocolException>(() =>
            IceImmunoSerialProtocol.DecodeFrame(invalidTail));
        Assert.Equal(IceImmunoProtocolError.InvalidTail, tailError.Error);
    }

    [Fact]
    public void Main_controller_stream_decoder_reports_bad_length_and_recovers_for_next_frame()
    {
        var invalidPrefix = Convert.FromHexString("A501FF7F");
        var valid = Convert.FromHexString("A50104000108020140BA5A");
        var decoder = new IceImmunoFrameStreamDecoder(maximumDataLength: 128);

        var results = decoder.Feed(invalidPrefix.Concat(valid).ToArray());

        Assert.Equal(2, results.Count);
        Assert.Equal(IceImmunoProtocolError.InvalidLength, results[0].Error);
        Assert.True(results[1].Ok, results[1].ErrorMessage);
    }

    [Fact]
    public void Main_controller_parses_ack_work_nodes_and_preserves_unconfirmed_runtime_payload()
    {
        var ackFrame = Decode("A50104000104020180B95A");
        var ack = MainControllerProtocol.ParseAck(ackFrame);
        Assert.True(ack.Succeeded);
        Assert.Equal(0x01, ack.ResponseCode);

        var work = MainControllerProtocol.ParseWorkStatus(Response(0x01, 0x08, [0x01, 0x01]));
        Assert.Equal(0x01, work.Value);

        var nodes = Enumerable.Repeat((byte)0x01, 64).ToArray();
        nodes[2] = 0x02;
        var parsedNodes = MainControllerProtocol.ParseNodeStatuses(Response(0x01, 0x09, [(byte)0x01, ..nodes]));
        Assert.Equal(64, parsedNodes.Values.Length);
        Assert.Equal(0x02, parsedNodes.Values[2]);

        var runtime = MainControllerProtocol.ParseRunTime(Response(0x01, 0x05, [0x78, 0x56, 0x34, 0x12]));
        Assert.Equal<byte>([0x78, 0x56, 0x34, 0x12], runtime.RawValue);
    }

    [Fact]
    public void Main_controller_parses_little_endian_temperature_switch_and_pwm_fields()
    {
        var temperatures = MainControllerProtocol.ParseBoardTemperatures(
            Response(0x04, 0x09, [0x01, 0x02, 0xFB, 0xFF, 0x05, 0x00, 0x2A, 0x00, 0x80, 0x00]),
            target: false);
        Assert.Equal(2, temperatures.BoardId);
        Assert.False(temperatures.IsTarget);
        Assert.Equal<short>([-5, 5, 42, 128], temperatures.ValuesCelsius);

        var targets = MainControllerProtocol.ParseBoardTemperatures(
            Response(0x04, 0x0A, [0x01, 0x01, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00]),
            target: true);
        Assert.Equal(1, targets.BoardId);
        Assert.True(targets.IsTarget);
        Assert.Equal<short>([1, 2, 3, 4], targets.ValuesCelsius);

        var switches = MainControllerProtocol.ParseBoardSwitchStates(
            Response(0x04, 0x0B, [0x01, 0x03, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00]));
        Assert.Equal(3, switches.BoardId);
        Assert.Equal<ushort>([1, 0, 1, 0], switches.Values);

        var pwm = MainControllerProtocol.ParsePwmSpeeds(
            Decode("A5010B0007060234120100FF000001250C5A"));
        Assert.Equal<ushort>([0x1234, 1, 255, 256], pwm.ValuesRpm);
    }

    [Fact]
    public void Main_controller_parses_optocoupler_mixer_and_qr_models()
    {
        var liquid = MainControllerProtocol.ParseOptocouplerPut(
            Decode("A501060005040103010001E25A"));
        Assert.Equal(3, liquid.ChannelId);
        Assert.Equal(1, liquid.Value);
        Assert.True(liquid.IsPutReport);

        var origin = MainControllerProtocol.ParseMixerOrigin(
            Decode("A50106000A020201000029095A"));
        Assert.Equal(1, origin.BoardId);
        Assert.Equal(0, origin.Value);
        Assert.Equal(MainControllerMixerValueKind.Origin, origin.Kind);

        var remaining = MainControllerProtocol.ParseMixerRemainingCount(
            Decode("A50106000A030201341282045A"));
        Assert.Equal(0x1234, remaining.Value);
        Assert.Equal(MainControllerMixerValueKind.RemainingCount, remaining.Kind);

        var status = MainControllerProtocol.ParseQrScanStatus(
            Decode("A5010500080602010065195A"));
        Assert.Equal(1, status.Value);

        var putText = MainControllerProtocol.ParseQrText(
            Decode("A5010D000803016368310D0A4142430D0A63AE5A"));
        Assert.Equal("ch1\r\nABC\r\n", putText.Text);
        Assert.Equal(MainControllerQrTextSource.PutReport, putText.Source);

        var pullText = MainControllerProtocol.ParseQrText(Response(0x08, 0x01, Encoding.ASCII.GetBytes("ABC")));
        Assert.Equal("ABC", pullText.Text);
        Assert.Equal(MainControllerQrTextSource.PullResponse, pullText.Source);
    }

    [Fact]
    public void Main_controller_read_command_models_have_confirmed_classes_subclasses_and_payloads()
    {
        AssertRequest(MainControllerProtocol.BuildWorkStatusRequest(), 0x01, 0x08, []);
        AssertRequest(MainControllerProtocol.BuildNodeStatusRequest(), 0x01, 0x09, []);
        AssertRequest(MainControllerProtocol.BuildRunTimeRequest(), 0x01, 0x05, []);
        AssertRequest(MainControllerProtocol.BuildBoardTemperaturesRequest(2), 0x04, 0x09, [0x02]);
        AssertRequest(MainControllerProtocol.BuildBoardTargetTemperaturesRequest(1), 0x04, 0x0A, [0x01]);
        AssertRequest(MainControllerProtocol.BuildBoardSwitchStatesRequest(3), 0x04, 0x0B, [0x03]);
        AssertRequest(MainControllerProtocol.BuildPwmSpeedsRequest(), 0x07, 0x06, []);
        AssertRequest(MainControllerProtocol.BuildMixerOriginRequest(0), 0x0A, 0x02, [0x00]);
        AssertRequest(MainControllerProtocol.BuildMixerRemainingCountRequest(3), 0x0A, 0x03, [0x03]);
        AssertRequest(MainControllerProtocol.BuildQrScanStatusRequest(), 0x08, 0x06, []);
        AssertRequest(MainControllerProtocol.BuildQrTextRequest(), 0x08, 0x01, []);
    }

    [Fact]
    public void Cooling_builds_confirmed_main_controller_request_frames_with_little_endian_payload()
    {
        AssertRequest(MainControllerProtocol.BuildCoolingConnectionStatusRequest(), 0x03, 0x01, []);
        AssertRequest(MainControllerProtocol.BuildCoolingCurrentTemperatureRequest(), 0x03, 0x02, []);
        AssertRequest(MainControllerProtocol.BuildCoolingTargetTemperatureRequest(), 0x03, 0x03, []);
        AssertRequest(MainControllerProtocol.BuildCoolingSwitchStateRequest(), 0x03, 0x05, []);

        // 10℃ → payload 0A 00（little-endian），不是 0A 整度或 64 00。
        AssertRequest(MainControllerProtocol.BuildSetCoolingTargetTemperatureRequest(10), 0x03, 0x04, [0x0A, 0x00]);
        AssertRequest(MainControllerProtocol.BuildSetCoolingTargetTemperatureRequest(0), 0x03, 0x04, [0x00, 0x00]);
        AssertRequest(MainControllerProtocol.BuildSetCoolingSwitchStateRequest(true), 0x03, 0x06, [0x01, 0x00]);
        AssertRequest(MainControllerProtocol.BuildSetCoolingSwitchStateRequest(false), 0x03, 0x06, [0x00, 0x00]);

        Assert.Throws<ArgumentOutOfRangeException>(() => MainControllerProtocol.BuildSetCoolingTargetTemperatureRequest(41));
    }

    [Fact]
    public void Cooling_parses_connection_temperature_target_and_switch_with_range_validation()
    {
        var connected = MainControllerProtocol.ParseCoolingConnectionStatus(Response(0x03, 0x01, [0x01, 0x01, 0x00]));
        Assert.True(connected.IsConnected);
        var disconnected = MainControllerProtocol.ParseCoolingConnectionStatus(Response(0x03, 0x01, [0x01, 0x00, 0x00]));
        Assert.False(disconnected.IsConnected);

        var current = MainControllerProtocol.ParseCoolingCurrentTemperature(Response(0x03, 0x02, [0x01, 0x08, 0x00]));
        Assert.Equal(8, current.Celsius);
        Assert.True(current.IsCurrent);

        var target = MainControllerProtocol.ParseCoolingTargetTemperature(Response(0x03, 0x03, [0x01, 0x0A, 0x00]));
        Assert.Equal(10, target.Celsius);
        Assert.False(target.IsCurrent);

        var swOn = MainControllerProtocol.ParseCoolingSwitchState(Response(0x03, 0x05, [0x01, 0x01, 0x00]));
        Assert.True(swOn.IsEnabled);
        var swOff = MainControllerProtocol.ParseCoolingSwitchState(Response(0x03, 0x05, [0x01, 0x00, 0x00]));
        Assert.False(swOff.IsEnabled);
    }

    [Fact]
    public void Cooling_parse_rejects_wrong_parent_sub_ack_payload_length_and_out_of_range_values()
    {
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingCurrentTemperature(Response(0x04, 0x02, [0x01, 0x08, 0x00]))); // 错父类
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingCurrentTemperature(Response(0x03, 0x03, [0x01, 0x08, 0x00]))); // 错子类
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingCurrentTemperature(Response(0x03, 0x02, [0x02, 0x08, 0x00]))); // ack 非成功
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingCurrentTemperature(Response(0x03, 0x02, [0x01, 0x08])));       // payload 长度错
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingCurrentTemperature(Response(0x03, 0x02, [0x01, 0xFF, 0x00])));  // 当前温度 >100
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingTargetTemperature(Response(0x03, 0x03, [0x01, 0x29, 0x00])));   // 目标温度 41 >40
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingConnectionStatus(Response(0x03, 0x01, [0x01, 0x02, 0x00])));   // 连接状态非 0/1
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingSwitchState(Response(0x03, 0x05, [0x01, 0x02, 0x00])));       // 开关值非 0/1
    }

    [Fact]
    public void Cooling_setter_ack_requires_success_ack_with_empty_payload()
    {
        MainControllerProtocol.ParseCoolingAck(Response(0x03, 0x04, [0x01]), MainControllerProtocol.CoolingSetTargetTemperatureSub);
        MainControllerProtocol.ParseCoolingAck(Response(0x03, 0x06, [0x01]), MainControllerProtocol.CoolingSetSwitchStateSub);

        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingAck(Response(0x03, 0x04, [0x02]), MainControllerProtocol.CoolingSetTargetTemperatureSub)); // 失败 ack
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingAck(Response(0x03, 0x04, [0x01, 0x00]), MainControllerProtocol.CoolingSetTargetTemperatureSub)); // 多余 payload
        Assert.Throws<IceImmunoProtocolException>(() => MainControllerProtocol.ParseCoolingAck(Response(0x03, 0x06, [0x01]), MainControllerProtocol.CoolingSetTargetTemperatureSub)); // 子类不匹配
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x02)]
    public void Work_status_success_parses_status_byte(byte status)
    {
        var work = MainControllerProtocol.ParseWorkStatus(Response(0x01, 0x08, [0x01, status]));
        Assert.Equal(status, work.Value);
    }

    [Fact]
    public void Work_status_failure_missing_status_byte()
    {
        var ex = Assert.Throws<IceImmunoProtocolException>(() =>
            MainControllerProtocol.ParseWorkStatus(Response(0x01, 0x08, [0x01])));
        Assert.Equal(IceImmunoProtocolError.InvalidPayload, ex.Error);
    }

    [Fact]
    public void Work_status_failure_multi_byte_status()
    {
        var ex = Assert.Throws<IceImmunoProtocolException>(() =>
            MainControllerProtocol.ParseWorkStatus(Response(0x01, 0x08, [0x01, 0x01, 0x02])));
        Assert.Equal(IceImmunoProtocolError.InvalidPayload, ex.Error);
    }

    [Fact]
    public void Work_status_failure_ack_type_not_success()
    {
        var ex = Assert.Throws<IceImmunoProtocolException>(() =>
            MainControllerProtocol.ParseWorkStatus(Response(0x01, 0x08, [0x02, 0x01])));
        Assert.Equal(IceImmunoProtocolError.InvalidPayload, ex.Error);
    }

    [Fact]
    public void Node_status_success_excludes_ack_byte_from_values()
    {
        var data = Enumerable.Repeat((byte)0x02, 64).ToArray();
        var statuses = MainControllerProtocol.ParseNodeStatuses(Response(0x01, 0x09, [(byte)0x01, ..data]));
        Assert.Equal(64, statuses.Values.Length);
        Assert.Equal(0x02, statuses.Values[0]);
    }

    [Fact]
    public void Node_status_failure_too_short()
    {
        var data = Enumerable.Repeat((byte)0x01, 63).ToArray();
        var ex = Assert.Throws<IceImmunoProtocolException>(() =>
            MainControllerProtocol.ParseNodeStatuses(Response(0x01, 0x09, [(byte)0x01, ..data])));
        Assert.Equal(IceImmunoProtocolError.InvalidPayload, ex.Error);
    }

    [Fact]
    public void Node_status_failure_too_long()
    {
        var data = Enumerable.Repeat((byte)0x01, 65).ToArray();
        var ex = Assert.Throws<IceImmunoProtocolException>(() =>
            MainControllerProtocol.ParseNodeStatuses(Response(0x01, 0x09, [(byte)0x01, ..data])));
        Assert.Equal(IceImmunoProtocolError.InvalidPayload, ex.Error);
    }

    [Fact]
    public void Switch_state_value_out_of_range_throws_InvalidPayload()
    {
        var ex = Assert.Throws<IceImmunoProtocolException>(() =>
            MainControllerProtocol.ParseBoardSwitchStates(
                Response(0x04, 0x0B, [0x01, 0x01, 0x01, 0x00, 0x02, 0x00, 0x01, 0x00, 0x01, 0x00])));
        Assert.Equal(IceImmunoProtocolError.InvalidPayload, ex.Error);
    }

    [Fact]
    public void Optocoupler_put_value_out_of_range_throws_InvalidPayload()
    {
        var ex = Assert.Throws<IceImmunoProtocolException>(() =>
            MainControllerProtocol.ParseOptocouplerPut(Put(0x05, 0x04, [0x00, 0x02, 0x00])));
        Assert.Equal(IceImmunoProtocolError.InvalidPayload, ex.Error);
    }

    [Fact]
    public void Optocoupler_put_value_0_and_1_map_to_IsTriggered()
    {
        var off = MainControllerProtocol.ParseOptocouplerPut(Put(0x05, 0x04, [0x00, 0x00, 0x00]));
        Assert.False(off.IsTriggered);
        Assert.Equal(0, off.Value);

        var on = MainControllerProtocol.ParseOptocouplerPut(Put(0x05, 0x04, [0x00, 0x01, 0x00]));
        Assert.True(on.IsTriggered);
        Assert.Equal(1, on.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Confirmed_optocoupler_channels_parse_with_ChannelId_and_IsTriggered(byte channelId)
    {
        var notTriggered = MainControllerProtocol.ParseOptocouplerPut(
            Put(0x05, 0x04, [channelId, 0x00, 0x00]));
        Assert.Equal(channelId, notTriggered.ChannelId);
        Assert.False(notTriggered.IsTriggered);

        var triggered = MainControllerProtocol.ParseOptocouplerPut(
            Put(0x05, 0x04, [channelId, 0x01, 0x00]));
        Assert.Equal(channelId, triggered.ChannelId);
        Assert.True(triggered.IsTriggered);
    }

    [Fact]
    public void Dcr55_trigger_text_requires_explicit_terminator_configuration()
    {
        Assert.Equal("RDCMXEV1,P11,P20", Dcr55Protocol.GetCommandText(Dcr55TriggerMode.Single));
        Assert.Equal("RDCMXEV1,P10", Dcr55Protocol.GetCommandText(Dcr55TriggerMode.Stop));
        Assert.Equal("RDCMXEV1,P11,P21", Dcr55Protocol.GetCommandText(Dcr55TriggerMode.Continuous));

        Assert.Equal(
            Encoding.ASCII.GetBytes("RDCMXEV1,P11,P20<END>"),
            Dcr55Protocol.EncodeCommand(Dcr55TriggerMode.Single, Encoding.ASCII.GetBytes("<END>")));
        Assert.Throws<ArgumentNullException>(() =>
            Dcr55Protocol.EncodeCommand(Dcr55TriggerMode.Single, null!));
    }

    [Fact]
    public void Dcr55_parses_crlf_and_rejects_empty_partial_and_ambiguous_results()
    {
        var timestamp = new DateTimeOffset(2026, 7, 8, 1, 2, 3, TimeSpan.Zero);
        var single = Dcr55Protocol.ParseBarcodeResult("SAMPLE-001\r\n");
        Assert.Equal(Dcr55ScanStatus.Success, single.Status);
        Assert.Equal("SAMPLE-001", single.Barcode);

        var multiple = Dcr55Protocol.ParseBarcodeResult("SAMPLE-001\r\nSAMPLE-002\r\n");
        Assert.Equal(Dcr55ScanStatus.InvalidResponse, multiple.Status);
        Assert.Null(multiple.Barcode);

        var partial = Dcr55Protocol.ParseBarcodeResult("SAMPLE-003");
        Assert.Equal(Dcr55ScanStatus.InvalidResponse, partial.Status);
        Assert.Equal("SAMPLE-003", partial.RawText);

        var unclassifiedEmpty = Dcr55Protocol.ParseBarcodeResult("\r\n");
        Assert.Equal(Dcr55ScanStatus.InvalidResponse, unclassifiedEmpty.Status);

        var timedOut = Dcr55Protocol.FromTransportStatus(Dcr55ScanStatus.Timeout, string.Empty, timestamp);
        Assert.Equal(Dcr55ScanStatus.Timeout, timedOut.Status);
        Assert.Equal(timestamp, timedOut.Timestamp);
        Assert.Null(timedOut.Barcode);
    }

    [Fact]
    public void Standalone_cooling_builds_confirmed_fixed_frames_and_target_frame()
    {
        Assert.Equal(Convert.FromHexString("FF008A75"), StandaloneCoolingProtocol.BuildReadTemperatureFrame());
        Assert.Equal(Convert.FromHexString("FF0005FA"), StandaloneCoolingProtocol.BuildSetTargetTemperatureFrame(5));
        Assert.Equal(Convert.FromHexString("FF00817E"), StandaloneCoolingProtocol.BuildStartFrame());
        Assert.Equal(Convert.FromHexString("FF00827D"), StandaloneCoolingProtocol.BuildStopFrame());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StandaloneCoolingProtocol.BuildSetTargetTemperatureFrame(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StandaloneCoolingProtocol.BuildSetTargetTemperatureFrame(11));
    }

    [Fact]
    public void Standalone_cooling_parses_temperature_control_echo_unknown_invalid_and_timeout_models()
    {
        var temperature = StandaloneCoolingProtocol.ParseResponse(Convert.FromHexString("FF0005FA"));
        Assert.Equal(StandaloneCoolingResponseStatus.Valid, temperature.Status);
        Assert.Equal(StandaloneCoolingFrameKind.Temperature, temperature.Frame!.Kind);
        Assert.Equal<byte?>(5, temperature.Frame.TemperatureCelsius);

        var start = StandaloneCoolingProtocol.ParseResponse(Convert.FromHexString("FF00817E"));
        Assert.Equal(StandaloneCoolingFrameKind.Start, start.Frame!.Kind);

        var stop = StandaloneCoolingProtocol.ParseResponse(Convert.FromHexString("FF00827D"));
        Assert.Equal(StandaloneCoolingFrameKind.Stop, stop.Frame!.Kind);

        var unknown = StandaloneCoolingProtocol.ParseResponse(Convert.FromHexString("FF00906F"));
        Assert.Equal(StandaloneCoolingFrameKind.Unknown, unknown.Frame!.Kind);

        var wrongLength = StandaloneCoolingProtocol.ParseResponse(Convert.FromHexString("FF0005"));
        Assert.Equal(StandaloneCoolingResponseError.InvalidLength, wrongLength.Error);

        var wrongHeader = StandaloneCoolingProtocol.ParseResponse(Convert.FromHexString("FE0005FA"));
        Assert.Equal(StandaloneCoolingResponseError.InvalidHeader, wrongHeader.Error);

        var wrongChecksum = StandaloneCoolingProtocol.ParseResponse(Convert.FromHexString("FF0005FB"));
        Assert.Equal(StandaloneCoolingResponseError.ChecksumMismatch, wrongChecksum.Error);

        var timeout = StandaloneCoolingProtocol.Timeout();
        Assert.Equal(StandaloneCoolingResponseStatus.TimedOut, timeout.Status);
        Assert.Null(timeout.Frame);
    }

    private static IceImmunoFrame Decode(string hex) =>
        IceImmunoSerialProtocol.DecodeFrame(Convert.FromHexString(hex));

    private static IceImmunoFrame Response(byte parentClass, byte subClass, byte[] payload) =>
        IceImmunoSerialProtocol.DecodeFrame(IceImmunoSerialProtocol.EncodeFrame(
            parentClass,
            subClass,
            IceImmunoSerialProtocol.ResponseType,
            payload));

    private static IceImmunoFrame Put(byte parentClass, byte subClass, byte[] payload) =>
        IceImmunoSerialProtocol.DecodeFrame(IceImmunoSerialProtocol.EncodeFrame(
            parentClass,
            subClass,
            IceImmunoSerialProtocol.RequestType,
            payload));

    private static void AssertRequest(byte[] bytes, byte parentClass, byte subClass, byte[] payload)
    {
        var frame = IceImmunoSerialProtocol.DecodeFrame(bytes);
        Assert.Equal(parentClass, frame.ParentClass);
        Assert.Equal(subClass, frame.SubClass);
        Assert.Equal(IceImmunoSerialProtocol.RequestType, frame.MessageType);
        Assert.Equal(payload, frame.Payload);
    }
}
