using System;
using System.IO;

namespace Stainer.SoconBridge
{
    internal interface IPeArchitectureInspector
    {
        PeArchitectureResult Inspect(string filePath);
    }

    internal sealed class PeArchitectureResult
    {
        public PeArchitectureResult(bool isValidPe, bool isX86Native, ushort? machine)
        {
            IsValidPe = isValidPe;
            IsX86Native = isX86Native;
            Machine = machine;
        }

        public bool IsValidPe { get; private set; }

        public bool IsX86Native { get; private set; }

        public ushort? Machine { get; private set; }

        public string MachineHex
        {
            get { return Machine.HasValue ? "0x" + Machine.Value.ToString("X4") : null; }
        }
    }

    internal sealed class PeArchitectureInspector : IPeArchitectureInspector
    {
        private const ushort X86Machine = 0x014C;
        private const int DosHeaderLength = 64;
        private const int PeSignatureAndMachineLength = 6;

        public PeArchitectureResult Inspect(string filePath)
        {
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length < DosHeaderLength)
                    {
                        return Invalid(null);
                    }

                    var dosHeader = ReadExactly(stream, DosHeaderLength);
                    if (dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
                    {
                        return Invalid(null);
                    }

                    var peOffset = ReadInt32LittleEndian(dosHeader, 0x3C);
                    if (peOffset < DosHeaderLength || peOffset > stream.Length - PeSignatureAndMachineLength)
                    {
                        return Invalid(null);
                    }

                    stream.Position = peOffset;
                    var peHeader = ReadExactly(stream, PeSignatureAndMachineLength);
                    if (peHeader[0] != (byte)'P' || peHeader[1] != (byte)'E' || peHeader[2] != 0 || peHeader[3] != 0)
                    {
                        return Invalid(null);
                    }

                    var machine = (ushort)(peHeader[4] | (peHeader[5] << 8));
                    return new PeArchitectureResult(true, machine == X86Machine, machine);
                }
            }
            catch (IOException)
            {
                return Invalid(null);
            }
            catch (UnauthorizedAccessException)
            {
                return Invalid(null);
            }
        }

        private static PeArchitectureResult Invalid(ushort? machine)
        {
            return new PeArchitectureResult(false, false, machine);
        }

        private static int ReadInt32LittleEndian(byte[] bytes, int offset)
        {
            return bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24);
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;

            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    throw new IOException("Unexpected end of PE file.");
                }

                offset += read;
            }

            return buffer;
        }
    }
}
