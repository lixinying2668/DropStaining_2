using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml;

namespace Stainer.SoconBridge
{
    internal sealed class BridgeHost
    {
        public const string DefaultPipeName = "Stainer.SoconBridge";
        public const int MaxRequestBytes = 64 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly string pipeName;
        private readonly BridgeRequestProcessor processor;
        private volatile bool stopRequested;

        public BridgeHost(string pipeName, BridgeRequestProcessor processor)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException("Pipe name is required.", "pipeName");
            }

            if (processor == null)
            {
                throw new ArgumentNullException("processor");
            }

            this.pipeName = pipeName;
            this.processor = processor;
        }

        public void Run()
        {
            while (!stopRequested)
            {
                using (var pipe = CreateServerStream())
                {
                    try
                    {
                        pipe.WaitForConnection();
                        if (stopRequested)
                        {
                            return;
                        }

                        var request = ReadRequest(pipe);
                        var response = processor.Process(request);
                        WriteResponse(pipe, response);
                    }
                    catch (InvalidDataException ex)
                    {
                        Console.WriteLine("IPC request rejected: {0}", ex.GetType().Name);
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine("IPC I/O ended: {0}", ex.GetType().Name);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("IPC error: {0}", ex.GetType().Name);
                    }
                }
            }
        }

        public void Stop()
        {
            stopRequested = true;

            try
            {
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    client.Connect(100);
                }
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public NamedPipeServerStream CreateServerStream()
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.None,
                4096,
                4096,
                CreatePipeSecurity());
        }

        public static PipeSecurity CreatePipeSecurity()
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser == null)
            {
                throw new InvalidOperationException("Current Windows user SID is unavailable.");
            }

            var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            security.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(localSystem, PipeAccessRights.FullControl, AccessControlType.Allow));

            return security;
        }

        public static BridgeRequest ReadRequest(Stream stream)
        {
            var length = ReadInt32LittleEndian(stream);
            if (length <= 0)
            {
                throw new InvalidDataException("Request length must be positive.");
            }

            if (length > MaxRequestBytes)
            {
                throw new InvalidDataException("Request length exceeds limit.");
            }

            var payload = ReadExactly(stream, length);
            string json;
            try
            {
                json = StrictUtf8.GetString(payload);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("Request payload is not valid UTF-8.", ex);
            }

            return DeserializeRequest(json);
        }

        public static void WriteResponse(Stream stream, BridgeResponse response)
        {
            var json = Serialize(response);
            WriteUtf8JsonFrame(stream, json);
        }

        internal static void WriteUtf8JsonFrame(Stream stream, string json)
        {
            var payload = StrictUtf8.GetBytes(json ?? string.Empty);
            WriteInt32LittleEndian(stream, payload.Length);
            stream.Write(payload, 0, payload.Length);
        }

        private static BridgeRequest DeserializeRequest(string json)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(BridgeRequest));
                using (var memory = new MemoryStream(StrictUtf8.GetBytes(json)))
                {
                    var request = serializer.ReadObject(memory) as BridgeRequest;
                    if (request == null)
                    {
                        throw new InvalidDataException("Request JSON did not produce a request.");
                    }

                    return request;
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (ex is SerializationException || ex is XmlException || ex is ArgumentException)
                {
                    throw new InvalidDataException("Request JSON is invalid.", ex);
                }

                throw;
            }
        }

        private static string Serialize(BridgeResponse response)
        {
            var serializer = new DataContractJsonSerializer(typeof(BridgeResponse));
            using (var memory = new MemoryStream())
            {
                serializer.WriteObject(memory, response);
                return StrictUtf8.GetString(memory.ToArray());
            }
        }

        private static int ReadInt32LittleEndian(Stream stream)
        {
            var bytes = ReadExactly(stream, 4);
            return bytes[0]
                | (bytes[1] << 8)
                | (bytes[2] << 16)
                | (bytes[3] << 24);
        }

        private static void WriteInt32LittleEndian(Stream stream, int value)
        {
            var bytes = new[]
            {
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 24) & 0xFF)
            };

            stream.Write(bytes, 0, bytes.Length);
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
                    throw new InvalidDataException("Unexpected end of stream.");
                }

                offset += read;
            }

            return buffer;
        }
    }
}
