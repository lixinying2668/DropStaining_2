using System.Buffers.Binary;
using System.Linq;
using System.Text;

namespace Stainer.Web.Application.Devices;

public static class MainControllerProtocol
{
    public const byte SystemClass = 0x01;
    public const byte CoolingClass = 0x03;
    public const byte HeatingClass = 0x04;
    public const byte OptocouplerClass = 0x05;
    public const byte PwmClass = 0x07;
    public const byte QrClass = 0x08;
    public const byte MixerClass = 0x0A;

    // 制冷（Cooling）子命令字 —— 父类 0x03，由主控统一处理（冰免通讯协议 ver1.0.6）。
    // 注意：协议温度单位是“整摄氏度”UINT16，与 DB/API 的 deci-C 不同，转换在适配层完成。
    public const byte CoolingConnectionStatusSub = 0x01;   // TL_COOL_GET_MODULE_CONNECT
    public const byte CoolingCurrentTemperatureSub = 0x02; // TL_COOL_GET_TEMP_CURRENT  (0..100 ℃)
    public const byte CoolingTargetTemperatureSub = 0x03;  // TL_COOL_GET_TEMP_TARGET   (0..40 ℃)
    public const byte CoolingSetTargetTemperatureSub = 0x04; // TL_COOL_SET_TEMP_TARGET (0..40 ℃)
    public const byte CoolingSwitchStateSub = 0x05;        // TL_COOL_GET_SWITCH_STATUS (0/1)
    public const byte CoolingSetSwitchStateSub = 0x06;     // TL_COOL_SET_SWITCH_STATUS (0/1)

    // 试剂二维码扫码（QR，父类 0x08）子命令 —— 主控内置多通道扫码模块（冰免通讯协议 ver1.0.6）。
    // 试剂瓶条码由主控 0x08 扫码（不随机械臂、不用 DCR55）；只读 0x01=文本、0x06=状态见下方 builder。
    public const byte QrStartScanSub = 0x04;   // TL_QR_START_SCAN（启动扫描，写）
    public const byte QrResetScanSub = 0x05;   // TL_QR_RESET_SCAN（复位，写）

    // 清洗泵 PWM（父类 0x07）写入子命令 —— 4 通道清洗泵，INT16 小端 -100~100（冰免通讯协议 ver1.0.6）。
    // pwm0~3 对应通道0~3 清洗泵；只读 0x06（检测值）见 BuildPwmSpeedsRequest。
    public const byte PwmSetIdValueSub = 0x02;   // TL_PWM_SET_ID_SET_VALUE（单通道写）
    public const byte PwmSetAllValueSub = 0x04;  // TL_PWM_SET_ALL_SET_VALUE（全通道写）

    public static byte[] BuildWorkStatusRequest() => Build(SystemClass, 0x08);
    public static byte[] BuildNodeStatusRequest() => Build(SystemClass, 0x09);
    public static byte[] BuildRunTimeRequest() => Build(SystemClass, 0x05);
    public static byte[] BuildBoardTemperaturesRequest(byte boardId) => BuildBoardRequest(HeatingClass, 0x09, boardId);
    public static byte[] BuildBoardTargetTemperaturesRequest(byte boardId) => BuildBoardRequest(HeatingClass, 0x0A, boardId);
    public static byte[] BuildBoardSwitchStatesRequest(byte boardId) => BuildBoardRequest(HeatingClass, 0x0B, boardId);
    public static byte[] BuildPwmSpeedsRequest() => Build(PwmClass, 0x06);
    public static byte[] BuildMixerOriginRequest(byte boardId) => BuildBoardRequest(MixerClass, 0x02, boardId);
    public static byte[] BuildMixerRemainingCountRequest(byte boardId) => BuildBoardRequest(MixerClass, 0x03, boardId);
    public static byte[] BuildQrScanStatusRequest() => Build(QrClass, 0x06);
    public static byte[] BuildQrTextRequest() => Build(QrClass, 0x01);
    public static byte[] BuildQrStartScanRequest() => Build(QrClass, QrStartScanSub);
    public static byte[] BuildQrResetScanRequest() => Build(QrClass, QrResetScanSub);

    // 清洗泵 PWM 写入：单通道（0x07/0x02，payload=[pwmId:uint8 0~3][值:int16 LE -100~100]）。构造期校验量程，避免把非法值打到线上。
    public static byte[] BuildSetPwmValueRequest(byte pwmId, short value)
    {
        if (pwmId > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(pwmId), "PWM id must be 0..3.");
        }
        if (value < -100 || value > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "PWM value must be -100..100.");
        }

        var payload = new byte[3];
        payload[0] = pwmId;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), value);
        return IceImmunoSerialProtocol.BuildRequestFrame(PwmClass, PwmSetIdValueSub, payload);
    }

    // 清洗泵 PWM 写入：全通道（0x07/0x04，payload=4×int16 LE -100~100）。构造期校验量程。
    public static byte[] BuildSetAllPwmValuesRequest(IReadOnlyList<short> values)
    {
        if (values.Count != 4)
        {
            throw new ArgumentException("Exactly 4 PWM values are required.", nameof(values));
        }
        foreach (var value in values)
        {
            if (value < -100 || value > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(values), "PWM value must be -100..100.");
            }
        }

        var payload = new byte[8];
        for (var index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(index * 2, 2), values[index]);
        }
        return IceImmunoSerialProtocol.BuildRequestFrame(PwmClass, PwmSetAllValueSub, payload);
    }

    // 制冷只读请求：父类 0x03，无 payload（请求帧 payload 为空）。
    public static byte[] BuildCoolingConnectionStatusRequest() => Build(CoolingClass, CoolingConnectionStatusSub);
    public static byte[] BuildCoolingCurrentTemperatureRequest() => Build(CoolingClass, CoolingCurrentTemperatureSub);
    public static byte[] BuildCoolingTargetTemperatureRequest() => Build(CoolingClass, CoolingTargetTemperatureSub);
    public static byte[] BuildCoolingSwitchStateRequest() => Build(CoolingClass, CoolingSwitchStateSub);

    // 制冷写入请求：payload 为 2 字节 UINT16 little-endian。构造期即校验协议量程，避免把非法值打到线上。
    public static byte[] BuildSetCoolingTargetTemperatureRequest(ushort targetCelsius)
    {
        if (targetCelsius > 40)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCelsius), "Cooling target temperature must be 0..40 Celsius.");
        }

        var payload = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, targetCelsius);
        return IceImmunoSerialProtocol.BuildRequestFrame(CoolingClass, CoolingSetTargetTemperatureSub, payload);
    }

    public static byte[] BuildSetCoolingSwitchStateRequest(bool enabled)
    {
        var payload = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, enabled ? (ushort)1 : (ushort)0);
        return IceImmunoSerialProtocol.BuildRequestFrame(CoolingClass, CoolingSetSwitchStateSub, payload);
    }

    public static MainControllerAck ParseAck(IceImmunoFrame frame)
    {
        EnsureFrame(frame, frame.ParentClass, frame.SubClass, IceImmunoSerialProtocol.ResponseType);
        EnsurePayloadAtLeast(frame, 1);
        return new MainControllerAck(frame.Payload[0] == 0x01, frame.Payload[0], frame.Payload[1..]);
    }

    public static MainControllerWorkStatus ParseWorkStatus(IceImmunoFrame frame)
    {
        var data = EnsureSuccessResponse(frame, SystemClass, 0x08, 1);
        return new MainControllerWorkStatus(data[0]);
    }

    public static MainControllerNodeStatuses ParseNodeStatuses(IceImmunoFrame frame)
    {
        var data = EnsureSuccessResponse(frame, SystemClass, 0x09, 64);
        return new MainControllerNodeStatuses(data);
    }

    public static MainControllerRunTime ParseRunTime(IceImmunoFrame frame)
    {
        EnsureFrame(frame, SystemClass, 0x05, IceImmunoSerialProtocol.ResponseType);
        EnsurePayloadAtLeast(frame, 1);
        return new MainControllerRunTime(frame.Payload.ToArray());
    }

    public static MainControllerTemperatureBoard ParseBoardTemperatures(IceImmunoFrame frame, bool target)
    {
        var data = EnsureSuccessResponse(frame, HeatingClass, target ? (byte)0x0A : (byte)0x09, 9);
        return new MainControllerTemperatureBoard(data[0], ReadInt16Values(data.AsSpan(1), 4), target);
    }

    public static MainControllerSwitchBoard ParseBoardSwitchStates(IceImmunoFrame frame)
    {
        var data = EnsureSuccessResponse(frame, HeatingClass, 0x0B, 9);
        var values = ReadUInt16Values(data.AsSpan(1), 4);
        if (values.Any(v => v != 0 && v != 1))
        {
            throw Error(IceImmunoProtocolError.InvalidPayload, "Temperature switch values must be 0 or 1.");
        }
        return new MainControllerSwitchBoard(data[0], values);
    }

    public static MainControllerOptocouplerStatus ParseOptocouplerPut(IceImmunoFrame frame)
    {
        EnsureFrame(frame, OptocouplerClass, 0x04, IceImmunoSerialProtocol.RequestType);
        EnsurePayload(frame, 3);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(1, 2));
        if (value != 0 && value != 1)
        {
            throw Error(IceImmunoProtocolError.InvalidPayload, "Optocoupler value must be 0 (not triggered) or 1 (triggered).");
        }
        return new MainControllerOptocouplerStatus(frame.Payload[0], value, true);
    }

    public static MainControllerPwmSpeeds ParsePwmSpeeds(IceImmunoFrame frame)
    {
        EnsureResponse(frame, PwmClass, 0x06, 8);
        return new MainControllerPwmSpeeds(ReadUInt16Values(frame.Payload, 4));
    }

    public static MainControllerMixerValue ParseMixerOrigin(IceImmunoFrame frame)
    {
        EnsureResponse(frame, MixerClass, 0x02, 3);
        return ParseMixerValue(frame, MainControllerMixerValueKind.Origin);
    }

    public static MainControllerMixerValue ParseMixerRemainingCount(IceImmunoFrame frame)
    {
        EnsureResponse(frame, MixerClass, 0x03, 3);
        return ParseMixerValue(frame, MainControllerMixerValueKind.RemainingCount);
    }

    public static MainControllerQrScanStatus ParseQrScanStatus(IceImmunoFrame frame)
    {
        EnsureResponse(frame, QrClass, 0x06, 2);
        return new MainControllerQrScanStatus(BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload));
    }

    public static MainControllerQrText ParseQrText(IceImmunoFrame frame)
    {
        var isPullResponse = frame.ParentClass == QrClass
            && frame.SubClass == 0x01
            && frame.MessageType == IceImmunoSerialProtocol.ResponseType;
        var isPutReport = frame.ParentClass == QrClass
            && frame.SubClass == 0x03
            && frame.MessageType == IceImmunoSerialProtocol.RequestType;
        if (!isPullResponse && !isPutReport)
        {
            throw Error(IceImmunoProtocolError.UnexpectedCommand, "The frame is not a QR text response or PUT report.");
        }

        var maximumLength = isPullResponse ? 512 : 1024;
        if (frame.Payload.Length > maximumLength || frame.Payload.Any(value => value > 0x7F))
        {
            throw Error(
                IceImmunoProtocolError.InvalidPayload,
                $"QR text must be 0..{maximumLength} bytes of ASCII data.");
        }

        return new MainControllerQrText(
            Encoding.ASCII.GetString(frame.Payload),
            isPutReport ? MainControllerQrTextSource.PutReport : MainControllerQrTextSource.PullResponse);
    }

    // 制冷响应解析：响应帧 payload = [0x01 成功 ack, UINT16_LO, UINT16_HI]（setter ack 仅 [0x01]，无业务数据）。
    // EnsureSuccessResponse 会校验 parent/sub/MessageType=Response/ack=0x01/payload 长度，这里再约束协议量程与枚举值。
    public static MainControllerCoolingConnectionStatus ParseCoolingConnectionStatus(IceImmunoFrame frame)
    {
        var data = EnsureSuccessResponse(frame, CoolingClass, CoolingConnectionStatusSub, 2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (value != 0 && value != 1)
        {
            throw Error(IceImmunoProtocolError.InvalidPayload, "Cooling module connection status must be 0 (disconnected) or 1 (connected).");
        }

        return new MainControllerCoolingConnectionStatus(value == 1);
    }

    public static MainControllerCoolingTemperature ParseCoolingCurrentTemperature(IceImmunoFrame frame)
    {
        var data = EnsureSuccessResponse(frame, CoolingClass, CoolingCurrentTemperatureSub, 2);
        var celsius = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (celsius > 100)
        {
            throw Error(IceImmunoProtocolError.InvalidPayload, "Cooling current temperature must be 0..100 Celsius.");
        }

        return new MainControllerCoolingTemperature((int)celsius, IsCurrent: true);
    }

    public static MainControllerCoolingTemperature ParseCoolingTargetTemperature(IceImmunoFrame frame)
    {
        var data = EnsureSuccessResponse(frame, CoolingClass, CoolingTargetTemperatureSub, 2);
        var celsius = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (celsius > 40)
        {
            throw Error(IceImmunoProtocolError.InvalidPayload, "Cooling target temperature must be 0..40 Celsius.");
        }

        return new MainControllerCoolingTemperature((int)celsius, IsCurrent: false);
    }

    public static MainControllerCoolingSwitchState ParseCoolingSwitchState(IceImmunoFrame frame)
    {
        var data = EnsureSuccessResponse(frame, CoolingClass, CoolingSwitchStateSub, 2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (value != 0 && value != 1)
        {
            throw Error(IceImmunoProtocolError.InvalidPayload, "Cooling switch status must be 0 (off) or 1 (on).");
        }

        return new MainControllerCoolingSwitchState(value == 1);
    }

    // 制冷 setter 成功 ack：parent=0x03/sub=messageType=Response，payload 恰为 [0x01]（成功 ack，无业务数据）。
    public static void ParseCoolingAck(IceImmunoFrame frame, byte subClass) =>
        EnsureSuccessResponse(frame, CoolingClass, subClass, 0);

    // 试剂 QR 启动/复位成功 ack：parent=0x08、sub=0x04/0x05、messageType=Response，payload 恰为 [0x01]（成功 ack，无业务数据）。
    public static void ParseQrStartScanAck(IceImmunoFrame frame) =>
        EnsureSuccessResponse(frame, QrClass, QrStartScanSub, 0);
    public static void ParseQrResetScanAck(IceImmunoFrame frame) =>
        EnsureSuccessResponse(frame, QrClass, QrResetScanSub, 0);

    // 清洗泵 PWM 写入成功 ack：单通道 0x07/0x02 应答=[ack][pwmId]（返回回显的 PWM ID）；全通道 0x07/0x04 应答=[ack]。
    public static byte ParsePwmSetIdValueAck(IceImmunoFrame frame)
    {
        var data = EnsureSuccessResponse(frame, PwmClass, PwmSetIdValueSub, 1);
        return data[0];
    }

    public static void ParsePwmSetAllAck(IceImmunoFrame frame) =>
        EnsureSuccessResponse(frame, PwmClass, PwmSetAllValueSub, 0);

    private static byte[] Build(byte parentClass, byte subClass) =>
        IceImmunoSerialProtocol.BuildRequestFrame(parentClass, subClass);

    private static byte[] BuildBoardRequest(byte parentClass, byte subClass, byte boardId) =>
        IceImmunoSerialProtocol.BuildRequestFrame(parentClass, subClass, [boardId]);

    private static MainControllerMixerValue ParseMixerValue(
        IceImmunoFrame frame,
        MainControllerMixerValueKind kind) =>
        new(frame.Payload[0], BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(1, 2)), kind);

    private static short[] ReadInt16Values(ReadOnlySpan<byte> bytes, int count)
    {
        var values = new short[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(index * 2, 2));
        }

        return values;
    }

    private static ushort[] ReadUInt16Values(ReadOnlySpan<byte> bytes, int count)
    {
        var values = new ushort[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(index * 2, 2));
        }

        return values;
    }

    // Validates a response frame (parentClass/subClass, MessageType == Response), validates that
    // the leading ack-type byte is 0x01 (success), and returns the business data that follows it.
    // expectedDataLength is the required length of the business data EXCLUDING the ack byte, so the
    // full payload must be exactly expectedDataLength + 1 bytes.
    private static byte[] EnsureSuccessResponse(IceImmunoFrame frame, byte parentClass, byte subClass, int expectedDataLength)
    {
        EnsureFrame(frame, parentClass, subClass, IceImmunoSerialProtocol.ResponseType);
        EnsurePayload(frame, expectedDataLength + 1);
        if (frame.Payload[0] != 0x01)
        {
            throw Error(
                IceImmunoProtocolError.InvalidPayload,
                $"Response ack type is 0x{frame.Payload[0]:X2}; expected 0x01 (success).");
        }

        return frame.Payload.AsSpan(1, expectedDataLength).ToArray();
    }

    private static void EnsureResponse(IceImmunoFrame frame, byte parentClass, byte subClass, int payloadLength)
    {
        EnsureFrame(frame, parentClass, subClass, IceImmunoSerialProtocol.ResponseType);
        EnsurePayload(frame, payloadLength);
    }

    private static void EnsureFrame(IceImmunoFrame frame, byte parentClass, byte subClass, byte messageType)
    {
        if (frame.ParentClass != parentClass || frame.SubClass != subClass || frame.MessageType != messageType)
        {
            throw Error(
                IceImmunoProtocolError.UnexpectedCommand,
                $"Unexpected frame 0x{frame.ParentClass:X2}/0x{frame.SubClass:X2}/0x{frame.MessageType:X2}.");
        }
    }

    private static void EnsurePayload(IceImmunoFrame frame, int payloadLength)
    {
        if (frame.Payload.Length != payloadLength)
        {
            throw Error(
                IceImmunoProtocolError.InvalidPayload,
                $"Payload length is {frame.Payload.Length}; expected {payloadLength}.");
        }
    }

    private static void EnsurePayloadAtLeast(IceImmunoFrame frame, int minimumLength)
    {
        if (frame.Payload.Length < minimumLength)
        {
            throw Error(
                IceImmunoProtocolError.InvalidPayload,
                $"Payload length is {frame.Payload.Length}; expected at least {minimumLength}.");
        }
    }

    private static IceImmunoProtocolException Error(IceImmunoProtocolError error, string message) => new(error, message);
}

public sealed record MainControllerAck(bool Succeeded, byte ResponseCode, byte[] AdditionalData);
public sealed record MainControllerWorkStatus(byte Value);
public sealed record MainControllerNodeStatuses(byte[] Values);

// V1.0.4 identifies this payload as run time but does not define its width or unit.
public sealed record MainControllerRunTime(byte[] RawValue);

public sealed record MainControllerTemperatureBoard(byte BoardId, short[] ValuesCelsius, bool IsTarget);
public sealed record MainControllerSwitchBoard(byte BoardId, ushort[] Values);
public sealed record MainControllerOptocouplerStatus(byte ChannelId, ushort Value, bool IsPutReport)
{
    public bool IsTriggered => Value == 1;
}
public sealed record MainControllerPwmSpeeds(ushort[] ValuesRpm);
public sealed record MainControllerMixerValue(byte BoardId, ushort Value, MainControllerMixerValueKind Kind);
public sealed record MainControllerQrScanStatus(ushort Value);
public sealed record MainControllerQrText(string Text, MainControllerQrTextSource Source);

// 制冷（主控 0x03）只读解析结果。Celsius 为协议“整摄氏度”，到 deci-C 的 ×10 转换在适配层完成。
public sealed record MainControllerCoolingConnectionStatus(bool IsConnected);
public sealed record MainControllerCoolingTemperature(int Celsius, bool IsCurrent);
public sealed record MainControllerCoolingSwitchState(bool IsEnabled);

public enum MainControllerMixerValueKind
{
    Origin,
    RemainingCount
}

public enum MainControllerQrTextSource
{
    PullResponse,
    PutReport
}
