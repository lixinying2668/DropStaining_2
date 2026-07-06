using System.Buffers.Binary;

namespace Stainer.Web.Application.Devices;

public static class IceImmunoSerialProtocol
{
    public const byte FrameHeader = 0xA5;
    public const byte Version = 0x01;
    public const byte FrameTail = 0x5A;
    public const byte RequestType = 0x01;
    public const byte ResponseType = 0x02;
    public const ushort Crc16ModbusPolynomial = 0x8005;
    public const ushort Crc16ModbusInitialValue = 0xFFFF;
    public const ushort Crc16ModbusXorOut = 0x0000;
    public const int DefaultMaximumDataLength = 4096;

    private const ushort ReflectedPolynomial = 0xA001;
    private const int FixedFrameLength = 7;
    private const int DataHeaderLength = 3;

    public static byte[] BuildRequestFrame(byte parentClass, byte subClass, ReadOnlySpan<byte> payload = default) =>
        EncodeFrame(parentClass, subClass, RequestType, payload);

    public static byte[] EncodeFrame(
        byte parentClass,
        byte subClass,
        byte messageType,
        ReadOnlySpan<byte> payload = default)
    {
        if (messageType is not (RequestType or ResponseType))
        {
            throw new ArgumentOutOfRangeException(nameof(messageType), "Message type must be 0x01 or 0x02.");
        }

        var dataLength = checked(DataHeaderLength + payload.Length);
        if (dataLength > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "The protocol data area cannot exceed 65535 bytes.");
        }

        var frame = new byte[FixedFrameLength + dataLength];
        frame[0] = FrameHeader;
        frame[1] = Version;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2, 2), (ushort)dataLength);
        frame[4] = parentClass;
        frame[5] = subClass;
        frame[6] = messageType;
        payload.CopyTo(frame.AsSpan(7, payload.Length));

        var crcOffset = 4 + dataLength;
        var crc = CalculateCrc16Modbus(frame.AsSpan(4, dataLength));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(crcOffset, 2), crc);
        frame[^1] = FrameTail;
        return frame;
    }

    public static IceImmunoFrame DecodeFrame(
        ReadOnlySpan<byte> bytes,
        int maximumDataLength = DefaultMaximumDataLength)
    {
        if (maximumDataLength < DataHeaderLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDataLength));
        }

        if (bytes.Length < FixedFrameLength + DataHeaderLength)
        {
            throw Error(IceImmunoProtocolError.TruncatedFrame, "The frame is incomplete.");
        }

        if (bytes[0] != FrameHeader)
        {
            throw Error(IceImmunoProtocolError.InvalidHeader, "The frame header must be 0xA5.");
        }

        if (bytes[1] != Version)
        {
            throw Error(IceImmunoProtocolError.UnsupportedVersion, $"Unsupported protocol version 0x{bytes[1]:X2}.");
        }

        var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2, 2));
        if (dataLength < DataHeaderLength || dataLength > maximumDataLength)
        {
            throw Error(
                IceImmunoProtocolError.InvalidLength,
                $"Data length {dataLength} is outside the accepted range {DataHeaderLength}..{maximumDataLength}.");
        }

        var expectedLength = FixedFrameLength + dataLength;
        if (bytes.Length != expectedLength)
        {
            throw Error(
                bytes.Length < expectedLength
                    ? IceImmunoProtocolError.TruncatedFrame
                    : IceImmunoProtocolError.UnexpectedTrailingData,
                $"Frame length is {bytes.Length}; expected {expectedLength}.");
        }

        if (bytes[^1] != FrameTail)
        {
            throw Error(IceImmunoProtocolError.InvalidTail, "The frame tail must be 0x5A.");
        }

        var crcOffset = 4 + dataLength;
        var expectedCrc = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(crcOffset, 2));
        var actualCrc = CalculateCrc16Modbus(bytes.Slice(4, dataLength));
        if (expectedCrc != actualCrc)
        {
            throw Error(
                IceImmunoProtocolError.CrcMismatch,
                $"CRC mismatch: received 0x{expectedCrc:X4}, calculated 0x{actualCrc:X4}.");
        }

        var messageType = bytes[6];
        if (messageType is not (RequestType or ResponseType))
        {
            throw Error(
                IceImmunoProtocolError.InvalidMessageType,
                $"Message type 0x{messageType:X2} is not supported.");
        }

        return new IceImmunoFrame(
            bytes[4],
            bytes[5],
            messageType,
            bytes.Slice(7, dataLength - DataHeaderLength).ToArray());
    }

    public static ushort CalculateCrc16Modbus(ReadOnlySpan<byte> data)
    {
        var crc = Crc16ModbusInitialValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x0001) == 0x0001
                    ? (ushort)((crc >> 1) ^ ReflectedPolynomial)
                    : (ushort)(crc >> 1);
            }
        }

        return (ushort)(crc ^ Crc16ModbusXorOut);
    }

    private static IceImmunoProtocolException Error(IceImmunoProtocolError error, string message) =>
        new(error, message);
}

public sealed record IceImmunoFrame(
    byte ParentClass,
    byte SubClass,
    byte MessageType,
    byte[] Payload)
{
    public bool IsResponse => MessageType == IceImmunoSerialProtocol.ResponseType;
}

public enum IceImmunoProtocolError
{
    InvalidHeader,
    UnsupportedVersion,
    InvalidLength,
    TruncatedFrame,
    UnexpectedTrailingData,
    InvalidTail,
    CrcMismatch,
    InvalidMessageType,
    InvalidPayload,
    UnexpectedCommand
}

public sealed class IceImmunoProtocolException(IceImmunoProtocolError error, string message) : Exception(message)
{
    public IceImmunoProtocolError Error { get; } = error;
}

public sealed record IceImmunoFrameDecodeResult(
    IceImmunoFrame? Frame,
    IceImmunoProtocolError? Error,
    string? ErrorMessage)
{
    public bool Ok => Frame is not null;

    public static IceImmunoFrameDecodeResult Success(IceImmunoFrame frame) => new(frame, null, null);

    public static IceImmunoFrameDecodeResult Failure(IceImmunoProtocolException exception) =>
        new(null, exception.Error, exception.Message);
}

public sealed class IceImmunoFrameStreamDecoder
{
    private readonly List<byte> buffer = [];
    private readonly int maximumDataLength;

    public IceImmunoFrameStreamDecoder(int maximumDataLength = IceImmunoSerialProtocol.DefaultMaximumDataLength)
    {
        if (maximumDataLength < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDataLength));
        }

        this.maximumDataLength = maximumDataLength;
    }

    public int BufferedByteCount => buffer.Count;

    public IReadOnlyList<IceImmunoFrameDecodeResult> Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            buffer.Add(value);
        }

        var results = new List<IceImmunoFrameDecodeResult>();
        while (true)
        {
            var headerIndex = buffer.IndexOf(IceImmunoSerialProtocol.FrameHeader);
            if (headerIndex < 0)
            {
                buffer.Clear();
                break;
            }

            if (headerIndex > 0)
            {
                buffer.RemoveRange(0, headerIndex);
            }

            if (buffer.Count < 4)
            {
                break;
            }

            var dataLength = buffer[2] | (buffer[3] << 8);
            if (dataLength < 3 || dataLength > maximumDataLength)
            {
                var exception = new IceImmunoProtocolException(
                    IceImmunoProtocolError.InvalidLength,
                    $"Data length {dataLength} is outside the accepted range 3..{maximumDataLength}.");
                results.Add(IceImmunoFrameDecodeResult.Failure(exception));
                buffer.RemoveAt(0);
                continue;
            }

            var frameLength = 7 + dataLength;
            if (buffer.Count < frameLength)
            {
                break;
            }

            var candidate = buffer.GetRange(0, frameLength).ToArray();
            buffer.RemoveRange(0, frameLength);
            try
            {
                results.Add(IceImmunoFrameDecodeResult.Success(
                    IceImmunoSerialProtocol.DecodeFrame(candidate, maximumDataLength)));
            }
            catch (IceImmunoProtocolException exception)
            {
                results.Add(IceImmunoFrameDecodeResult.Failure(exception));
            }
        }

        return results;
    }

    public void Reset() => buffer.Clear();
}
