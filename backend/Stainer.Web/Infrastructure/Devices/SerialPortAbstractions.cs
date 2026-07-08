using System.IO.Ports;

namespace Stainer.Web.Infrastructure.Devices;

// Transport 层内部的串口抽象。仅在本项目 (Infrastructure/Devices) 内使用，
// Application 层永远不会引用 System.IO.Ports，也不会直接调用串口。
internal interface ISerialPort : IDisposable
{
    string PortName { get; set; }

    int BaudRate { get; set; }

    int DataBits { get; set; }

    Parity Parity { get; set; }

    StopBits StopBits { get; set; }

    Handshake Handshake { get; set; }

    int ReadTimeout { get; set; }

    int WriteTimeout { get; set; }

    bool IsOpen { get; }

    int BytesToRead { get; }

    void Open();

    void Close();

    void Write(byte[] buffer, int offset, int count);

    int ReadByte();

    void DiscardInBuffer();
}

internal sealed class SystemIoSerialPortAdapter : ISerialPort
{
    private readonly SerialPort port = new();

    public string PortName
    {
        get => port.PortName;
        set => port.PortName = value;
    }

    public int BaudRate
    {
        get => port.BaudRate;
        set => port.BaudRate = value;
    }

    public int DataBits
    {
        get => port.DataBits;
        set => port.DataBits = value;
    }

    public Parity Parity
    {
        get => port.Parity;
        set => port.Parity = value;
    }

    public StopBits StopBits
    {
        get => port.StopBits;
        set => port.StopBits = value;
    }

    public Handshake Handshake
    {
        get => port.Handshake;
        set => port.Handshake = value;
    }

    public int ReadTimeout
    {
        get => port.ReadTimeout;
        set => port.ReadTimeout = value;
    }

    public int WriteTimeout
    {
        get => port.WriteTimeout;
        set => port.WriteTimeout = value;
    }

    public bool IsOpen => port.IsOpen;

    public int BytesToRead => port.BytesToRead;

    public void Open() => port.Open();

    public void Close() => port.Close();

    public void Write(byte[] buffer, int offset, int count) => port.Write(buffer, offset, count);

    public int ReadByte() => port.ReadByte();

    public void DiscardInBuffer() => port.DiscardInBuffer();

    public void Dispose() => port.Dispose();
}