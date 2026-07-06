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

        var work = MainControllerProtocol.ParseWorkStatus(Decode("A50104000108020140BA5A"));
        Assert.Equal(0x01, work.Value);

        var nodes = Enumerable.Repeat((byte)0x01, 64).ToArray();
        nodes[2] = 0x02;
        var parsedNodes = MainControllerProtocol.ParseNodeStatuses(Response(0x01, 0x09, nodes));
        Assert.Equal(64, parsedNodes.Values.Length);
        Assert.Equal(0x02, parsedNodes.Values[2]);

        var runtime = MainControllerProtocol.ParseRunTime(Response(0x01, 0x05, [0x78, 0x56, 0x34, 0x12]));
        Assert.Equal<byte>([0x78, 0x56, 0x34, 0x12], runtime.RawValue);
    }

    [Fact]
    public void Main_controller_parses_little_endian_temperature_switch_and_pwm_fields()
    {
        var temperatures = MainControllerProtocol.ParseBoardTemperatures(
            Decode("A5010C0004090202FBFF05002A0080002B8E5A"),
            target: false);
        Assert.Equal(2, temperatures.BoardId);
        Assert.False(temperatures.IsTarget);
        Assert.Equal<short>([-5, 5, 42, 128], temperatures.ValuesCelsius);

        var targets = MainControllerProtocol.ParseBoardTemperatures(
            Response(0x04, 0x0A, [0x01, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00]),
            target: true);
        Assert.True(targets.IsTarget);
        Assert.Equal<short>([1, 2, 3, 4], targets.ValuesCelsius);

        var switches = MainControllerProtocol.ParseBoardSwitchStates(
            Response(0x04, 0x0B, [0x03, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00]));
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
    public void Dcr55_parses_crlf_single_multiple_pending_and_no_barcode_timeout_results()
    {
        var single = Dcr55Protocol.ParseBarcodeResult("SAMPLE-001\r\n");
        Assert.Equal(Dcr55ScanOutcome.Completed, single.Outcome);
        Assert.Equal(["SAMPLE-001"], single.Barcodes);

        var multiple = Dcr55Protocol.ParseBarcodeResult("SAMPLE-001\r\nSAMPLE-002\r\n");
        Assert.Equal(Dcr55ScanOutcome.Completed, multiple.Outcome);
        Assert.Equal(["SAMPLE-001", "SAMPLE-002"], multiple.Barcodes);

        var partial = Dcr55Protocol.ParseBarcodeResult("SAMPLE-003");
        Assert.Equal(Dcr55ScanOutcome.Pending, partial.Outcome);
        Assert.Empty(partial.Barcodes);
        Assert.Equal("SAMPLE-003", partial.PendingFragment);

        var unclassifiedEmpty = Dcr55Protocol.ParseBarcodeResult("\r\n");
        Assert.Equal(Dcr55ScanOutcome.Pending, unclassifiedEmpty.Outcome);
        Assert.Equal(1, unclassifiedEmpty.EmptyRecordCount);

        var timedOut = Dcr55Protocol.NoBarcodeTimeout();
        Assert.Equal(Dcr55ScanOutcome.NoBarcodeTimeout, timedOut.Outcome);
        Assert.Empty(timedOut.Barcodes);
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

    private static void AssertRequest(byte[] bytes, byte parentClass, byte subClass, byte[] payload)
    {
        var frame = IceImmunoSerialProtocol.DecodeFrame(bytes);
        Assert.Equal(parentClass, frame.ParentClass);
        Assert.Equal(subClass, frame.SubClass);
        Assert.Equal(IceImmunoSerialProtocol.RequestType, frame.MessageType);
        Assert.Equal(payload, frame.Payload);
    }
}
