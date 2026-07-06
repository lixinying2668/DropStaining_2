namespace Stainer.Web.Application.Devices;

public static class StandaloneCoolingProtocol
{
    public static ReadOnlySpan<byte> ReadTemperatureFrame => [0xFF, 0x00, 0x8A, 0x75];
    public static ReadOnlySpan<byte> StartFrame => [0xFF, 0x00, 0x81, 0x7E];
    public static ReadOnlySpan<byte> StopFrame => [0xFF, 0x00, 0x82, 0x7D];

    public static byte[] BuildReadTemperatureFrame() => ReadTemperatureFrame.ToArray();
    public static byte[] BuildStartFrame() => StartFrame.ToArray();
    public static byte[] BuildStopFrame() => StopFrame.ToArray();

    public static byte[] BuildSetTargetTemperatureFrame(byte targetTemperatureCelsius)
    {
        if (targetTemperatureCelsius is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetTemperatureCelsius),
                "The confirmed target-temperature range is 1..10 Celsius.");
        }

        return BuildFrame(targetTemperatureCelsius);
    }

    public static StandaloneCoolingResponse ParseResponse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
        {
            return StandaloneCoolingResponse.Invalid(
                StandaloneCoolingResponseError.InvalidLength,
                $"Cooling frame length is {bytes.Length}; expected 4.",
                bytes.ToArray());
        }

        if (bytes[0] != 0xFF || bytes[1] != 0x00)
        {
            return StandaloneCoolingResponse.Invalid(
                StandaloneCoolingResponseError.InvalidHeader,
                "Cooling frame must start with FF 00.",
                bytes.ToArray());
        }

        var expectedCheck = unchecked((byte)(0xFF - bytes[2]));
        if (bytes[3] != expectedCheck)
        {
            return StandaloneCoolingResponse.Invalid(
                StandaloneCoolingResponseError.ChecksumMismatch,
                $"Cooling checksum is 0x{bytes[3]:X2}; expected 0x{expectedCheck:X2}.",
                bytes.ToArray());
        }

        var kind = bytes[2] <= 128
            ? StandaloneCoolingFrameKind.Temperature
            : bytes[2] switch
            {
                0x81 => StandaloneCoolingFrameKind.Start,
                0x82 => StandaloneCoolingFrameKind.Stop,
                0x8A => StandaloneCoolingFrameKind.ReadTemperature,
                _ => StandaloneCoolingFrameKind.Unknown
            };
        return StandaloneCoolingResponse.Valid(new StandaloneCoolingFrame(bytes[2], kind), bytes.ToArray());
    }

    public static StandaloneCoolingResponse Timeout() => StandaloneCoolingResponse.TimedOut();

    private static byte[] BuildFrame(byte value) => [0xFF, 0x00, value, unchecked((byte)(0xFF - value))];
}

public enum StandaloneCoolingFrameKind
{
    Temperature,
    Start,
    Stop,
    ReadTemperature,
    Unknown
}

public enum StandaloneCoolingResponseStatus
{
    Valid,
    Invalid,
    TimedOut
}

public enum StandaloneCoolingResponseError
{
    InvalidLength,
    InvalidHeader,
    ChecksumMismatch
}

public sealed record StandaloneCoolingFrame(byte Value, StandaloneCoolingFrameKind Kind)
{
    public byte? TemperatureCelsius => Kind == StandaloneCoolingFrameKind.Temperature ? Value : null;
}

public sealed record StandaloneCoolingResponse(
    StandaloneCoolingResponseStatus Status,
    StandaloneCoolingFrame? Frame,
    StandaloneCoolingResponseError? Error,
    string? Message,
    byte[] RawFrame)
{
    public static StandaloneCoolingResponse Valid(StandaloneCoolingFrame frame, byte[] rawFrame) =>
        new(StandaloneCoolingResponseStatus.Valid, frame, null, null, rawFrame);

    public static StandaloneCoolingResponse Invalid(
        StandaloneCoolingResponseError error,
        string message,
        byte[] rawFrame) =>
        new(StandaloneCoolingResponseStatus.Invalid, null, error, message, rawFrame);

    public static StandaloneCoolingResponse TimedOut() =>
        new(StandaloneCoolingResponseStatus.TimedOut, null, null, "Cooling response timed out.", []);
}
