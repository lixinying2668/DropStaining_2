using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Stainer.SoconBridge
{
    internal static class SelfTestRunner
    {
        private const ushort X86Machine = 0x014C;
        private const ushort X64Machine = 0x8664;

        public static int Run(TextWriter output)
        {
            var test = new SelfTest(output);
            try
            {
                test.RunAll();
                output.WriteLine("Self-test passed: {0} checks.", test.CheckCount);
                return 0;
            }
            catch (Exception ex)
            {
                output.WriteLine("Self-test failed: {0}", ex.GetType().Name);
                return 1;
            }
        }

        private sealed class SelfTest
        {
            public SelfTest(TextWriter output)
            {
            }

            public int CheckCount { get; private set; }

            public void RunAll()
            {
                PingReturnsOffline();
                GetBridgeStatusDoesNotValidate();
                UnknownCommandIsRejected();
                MissingSdkPath();
                MissingCoreFiles();
                InvalidCanBootloaderPe();
                NonX86ProcessProbe();
                DeploymentValidatedWithCompleteFakeFiles();
                DeploymentValidatedWithRuntimeWarnings();
                ActionCommandsAreNotSupported();
                LengthPrefixedProtocolRejectsMalformedRequests();
            }

            private void PingReturnsOffline()
            {
                var validator = new CountingValidator(BridgeStatus.DeploymentValidated);
                var processor = new BridgeRequestProcessor(validator, BridgeStatus.Offline);
                var response = processor.Process(new BridgeRequest { RequestId = "self-test-1", Command = "Ping" });

                Assert(response.Success, "Ping should succeed.");
                Assert(response.BridgeStatus == BridgeStatus.Offline.ToString(), "Ping should report Offline.");
                Assert(validator.Count == 0, "Ping must not validate SDK deployment.");
            }

            private void GetBridgeStatusDoesNotValidate()
            {
                var validator = new CountingValidator(BridgeStatus.DeploymentValidated);
                var processor = new BridgeRequestProcessor(validator, BridgeStatus.Offline);
                var response = processor.Process(new BridgeRequest { RequestId = "self-test-2", Command = "GetBridgeStatus" });

                Assert(response.Success, "GetBridgeStatus should succeed.");
                Assert(response.BridgeStatus == BridgeStatus.Offline.ToString(), "GetBridgeStatus should report Offline.");
                Assert(validator.Count == 0, "GetBridgeStatus must not validate SDK deployment.");
            }

            private void UnknownCommandIsRejected()
            {
                var processor = new BridgeRequestProcessor(new CountingValidator(BridgeStatus.DeploymentValidated), BridgeStatus.Offline);
                var response = processor.Process(new BridgeRequest { RequestId = "self-test-3", Command = "Unknown" });

                Assert(!response.Success, "Unknown command should fail.");
                Assert(response.Message == "NotSupported", "Unknown command should return NotSupported.");
                Assert(response.BridgeStatus == BridgeStatus.Offline.ToString(), "Unknown command should keep current status.");
            }

            private void MissingSdkPath()
            {
                WithTempDirectory(delegate(string baseDirectory)
                {
                    var validator = CreateValidator(baseDirectory, null, true);
                    var result = validator.Validate();

                    Assert(result.Status == BridgeStatus.SdkPathMissing, "Missing SDK path should report SdkPathMissing.");
                    Assert(result.Details.SdkPathConfigured == false, "Missing SDK path should not be configured.");
                });
            }

            private void MissingCoreFiles()
            {
                WithTempDirectory(delegate(string sdkDirectory)
                {
                    var validator = CreateValidator(sdkDirectory, sdkDirectory, true);
                    var result = validator.Validate();

                    Assert(result.Status == BridgeStatus.SdkFilesMissing, "Missing core DLLs should report SdkFilesMissing.");
                    Assert(Contains(result.Details.MissingFiles, "SOCON.API.dll"), "Missing SOCON.API.dll should be reported.");
                    Assert(Contains(result.Details.MissingFiles, "SOCON.Utility.dll"), "Missing SOCON.Utility.dll should be reported.");
                    Assert(Contains(result.Details.MissingFiles, "can_bootloader.dll"), "Missing can_bootloader.dll should be reported.");
                });
            }

            private void InvalidCanBootloaderPe()
            {
                WithTempDirectory(delegate(string sdkDirectory)
                {
                    WriteTextFile(Path.Combine(sdkDirectory, "SOCON.API.dll"), "fake");
                    WriteTextFile(Path.Combine(sdkDirectory, "SOCON.Utility.dll"), "fake");
                    WriteFakePe(Path.Combine(sdkDirectory, "can_bootloader.dll"), X64Machine);

                    var validator = CreateValidator(sdkDirectory, sdkDirectory, true);
                    var result = validator.Validate();

                    Assert(result.Status == BridgeStatus.SdkFilesMissing, "Non-x86 can_bootloader PE should report SdkFilesMissing.");
                    Assert(result.Details.CanBootloaderIsX86 == false, "Non-x86 can_bootloader PE should be marked not x86.");
                });
            }

            private void NonX86ProcessProbe()
            {
                WithTempDirectory(delegate(string sdkDirectory)
                {
                    var validator = CreateValidator(sdkDirectory, sdkDirectory, false);
                    var result = validator.Validate();

                    Assert(result.Status == BridgeStatus.ArchitectureInvalid, "Non-x86 process probe should report ArchitectureInvalid.");
                    Assert(result.Details.IsX86Process == false, "Non-x86 process should be reported.");
                });
            }

            private void DeploymentValidatedWithCompleteFakeFiles()
            {
                WithTempDirectory(delegate(string sdkDirectory)
                {
                    WriteCompleteFakeSdk(sdkDirectory, true);

                    var validator = CreateValidator(sdkDirectory, sdkDirectory, true);
                    var result = validator.Validate();

                    Assert(result.Status == BridgeStatus.DeploymentValidated, "Complete fake SDK should report DeploymentValidated.");
                    Assert(result.Warnings.Count == 0, "Complete fake SDK should not warn.");
                });
            }

            private void DeploymentValidatedWithRuntimeWarnings()
            {
                WithTempDirectory(delegate(string sdkDirectory)
                {
                    WriteCompleteFakeSdk(sdkDirectory, false);

                    var validator = CreateValidator(sdkDirectory, sdkDirectory, true);
                    var result = validator.Validate();

                    Assert(result.Status == BridgeStatus.DeploymentValidated, "Missing runtime dependencies should not block core validation.");
                    Assert(Contains(result.Warnings, BridgeWarningCodes.SdkRuntimeDependenciesWarning), "Runtime dependency warning should be returned.");
                    Assert(Contains(result.Details.MissingFiles, "SOCON.ScEventBus.dll"), "Missing SOCON.ScEventBus.dll should be reported.");
                    Assert(Contains(result.Details.MissingFiles, "C1.C1Zip.4.dll"), "Missing C1.C1Zip.4.dll should be reported.");
                });
            }

            private void ActionCommandsAreNotSupported()
            {
                var validator = new CountingValidator(BridgeStatus.DeploymentValidated);
                var processor = new BridgeRequestProcessor(validator, BridgeStatus.Offline);
                var actionCommands = new[]
                {
                    "Connect",
                    "OpenPort",
                    "ClosePort",
                    "Disconnect",
                    "InitArm",
                    "MoveTo",
                    "LiqDet",
                    "Aspirate",
                    "Dispense",
                    "WaitActionDone"
                };

                foreach (var command in actionCommands)
                {
                    var response = processor.Process(new BridgeRequest { RequestId = "self-test-action", Command = command });
                    Assert(!response.Success, "Action command should fail.");
                    Assert(response.Message == "NotSupported", "Action command should return NotSupported.");
                }

                Assert(validator.Count == 0, "Action commands must not validate SDK deployment.");
            }

            private void LengthPrefixedProtocolRejectsMalformedRequests()
            {
                var validRequest = ReadRequestFromFrame(CreateFrame(Encoding.UTF8.GetBytes("{\"requestId\":\"self-test-11\",\"command\":\"Ping\"}")));
                Assert(validRequest.Command == "Ping", "Length-prefixed valid request should parse.");

                using (var memory = new MemoryStream())
                {
                    BridgeHost.WriteResponse(memory, new BridgeResponse
                    {
                        RequestId = "self-test-11",
                        Command = "Ping",
                        Success = true,
                        BridgeStatus = BridgeStatus.Offline.ToString(),
                        Message = "Pong",
                        Details = new BridgeResponseDetails(),
                        Warnings = new List<string>()
                    });
                    Assert(memory.Length > 4, "Response should include length prefix and JSON body.");
                }

                AssertRejected(CreateFrame(new byte[0], 0), "Zero length should be rejected.");
                AssertRejected(CreateFrame(new byte[] { 1 }, -1), "Negative length should be rejected.");
                AssertRejected(CreateFrame(new byte[] { 1 }, BridgeHost.MaxRequestBytes + 1), "Oversized length should be rejected.");
                AssertRejected(CreateFrame(new byte[] { 0xC3, 0x28 }), "Invalid UTF-8 should be rejected.");
                AssertRejected(CreateFrame(Encoding.UTF8.GetBytes("{")), "Invalid JSON should be rejected.");
            }

            private SdkDeploymentValidator CreateValidator(string baseDirectory, string sdkDirectory, bool isX86Process)
            {
                var options = new SdkDeploymentValidatorOptions(baseDirectory);
                options.EnvironmentSdkDirectoryProvider = delegate { return sdkDirectory; };

                return new SdkDeploymentValidator(
                    options,
                    new FixedArchitectureProbe(isX86Process),
                    new PeArchitectureInspector());
            }

            private void AssertRejected(byte[] frame, string message)
            {
                try
                {
                    ReadRequestFromFrame(frame);
                    throw new InvalidOperationException(message);
                }
                catch (InvalidDataException)
                {
                    CheckCount++;
                }
            }

            private BridgeRequest ReadRequestFromFrame(byte[] frame)
            {
                using (var memory = new MemoryStream(frame))
                {
                    return BridgeHost.ReadRequest(memory);
                }
            }

            private static byte[] CreateFrame(byte[] payload)
            {
                return CreateFrame(payload, payload.Length);
            }

            private static byte[] CreateFrame(byte[] payload, int declaredLength)
            {
                using (var memory = new MemoryStream())
                {
                    WriteInt32LittleEndian(memory, declaredLength);
                    if (payload != null && payload.Length > 0)
                    {
                        memory.Write(payload, 0, payload.Length);
                    }

                    return memory.ToArray();
                }
            }

            private static void WriteCompleteFakeSdk(string sdkDirectory, bool includeRuntimeDependencies)
            {
                WriteTextFile(Path.Combine(sdkDirectory, "SOCON.API.dll"), "fake");
                WriteTextFile(Path.Combine(sdkDirectory, "SOCON.Utility.dll"), "fake");
                WriteFakePe(Path.Combine(sdkDirectory, "can_bootloader.dll"), X86Machine);

                if (includeRuntimeDependencies)
                {
                    WriteTextFile(Path.Combine(sdkDirectory, "SOCON.ScEventBus.dll"), "fake");
                    WriteTextFile(Path.Combine(sdkDirectory, "C1.C1Zip.4.dll"), "fake");
                }
            }

            private static void WriteFakePe(string path, ushort machine)
            {
                var bytes = new byte[0x86];
                bytes[0] = (byte)'M';
                bytes[1] = (byte)'Z';
                bytes[0x3C] = 0x80;
                bytes[0x80] = (byte)'P';
                bytes[0x81] = (byte)'E';
                bytes[0x82] = 0;
                bytes[0x83] = 0;
                bytes[0x84] = (byte)(machine & 0xFF);
                bytes[0x85] = (byte)((machine >> 8) & 0xFF);
                File.WriteAllBytes(path, bytes);
            }

            private static void WriteTextFile(string path, string content)
            {
                File.WriteAllText(path, content, Encoding.UTF8);
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

            private static bool Contains(ICollection<string> values, string expected)
            {
                return values != null && values.Contains(expected);
            }

            private void Assert(bool condition, string message)
            {
                if (!condition)
                {
                    throw new InvalidOperationException(message);
                }

                CheckCount++;
            }

            private static void WithTempDirectory(Action<string> action)
            {
                var directory = Path.Combine(Path.GetTempPath(), "Stainer.SoconBridge.SelfTest." + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try
                {
                    action(directory);
                }
                finally
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                }
            }
        }

        private sealed class CountingValidator : ISdkDeploymentValidator
        {
            private readonly BridgeStatus status;

            public CountingValidator(BridgeStatus status)
            {
                this.status = status;
            }

            public int Count { get; private set; }

            public SdkDeploymentValidationResult Validate()
            {
                Count++;
                return new SdkDeploymentValidationResult(status, new BridgeResponseDetails(), new List<string>());
            }
        }

        private sealed class FixedArchitectureProbe : IProcessArchitectureProbe
        {
            private readonly bool isX86Process;

            public FixedArchitectureProbe(bool isX86Process)
            {
                this.isX86Process = isX86Process;
            }

            public bool IsX86Process()
            {
                return isX86Process;
            }
        }
    }
}
