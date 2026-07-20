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
            private readonly TextWriter output;

            public SelfTest(TextWriter output)
            {
                this.output = output;
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
                ManagedAssemblyLoadFailureIsReported();
                RequiredTypeFailureIsReported();
                DeploymentValidatedWithCompleteFakeFiles();
                DeploymentValidatedWithRuntimeWarnings();
                ActionCommandsAreNotSupported();
                ReadOnlySessionGateFailsClosed();
                ReadOnlySessionDispatchesOnlyFakeAdapter();
                LengthPrefixedProtocolRejectsMalformedRequests();
                ConfiguredSdkRuntimeValidation();
                ReadOnlySessionRejectedWhenRuntimeDependencyWarning();
                ReadOnlySessionRejectedWhenPortOrBaudInvalid();
                ReadOnlySessionCloseFailsWhenClosePortReturnsFalse();
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

            private void ManagedAssemblyLoadFailureIsReported()
            {
                WithTempDirectory(delegate(string sdkDirectory)
                {
                    WriteCompleteFakeSdk(sdkDirectory, true);

                    var validator = CreateValidator(sdkDirectory, sdkDirectory, true, false);
                    var result = validator.Validate();

                    Assert(result.Status == BridgeStatus.SdkFilesMissing, "Managed assembly load failure should block validation.");
                    Assert(result.RuntimeDetails.ManagedAssemblyLoadSucceeded == false, "Managed assembly load failure should be reported.");
                    Assert(result.RuntimeDetails.ExceptionDetails.Count > 0, "Managed assembly load exception details should be captured.");
                });
            }

            private void RequiredTypeFailureIsReported()
            {
                WithTempDirectory(delegate(string sdkDirectory)
                {
                    WriteCompleteFakeSdk(sdkDirectory, true);

                    var validator = CreateValidator(sdkDirectory, sdkDirectory, true, true, false);
                    var result = validator.Validate();

                    Assert(result.Success, "Missing diagnostic types should not change deployment validation status.");
                    Assert(result.RuntimeDetails.RequiredTypesAvailable == false, "Missing required SOCON types should be reported.");
                    Assert(!result.DiagnosticSuccess, "Missing required SOCON types should fail the diagnostic result.");
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

            private void ReadOnlySessionGateFailsClosed()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var factoryCalls = 0;
                var processor = new BridgeRequestProcessor(
                    new CountingValidator(BridgeStatus.DeploymentValidated),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, false),
                    delegate
                    {
                        factoryCalls++;
                        return new FakeReadOnlyAdapter();
                    });

                var response = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-read-only-gate",
                    Command = "OpenConfiguredReadOnlySession"
                });

                Assert(!response.Success, "Read-only session must be rejected when the launch gate is off.");
                Assert(response.Message == "RealReadOnlyNotEnabled", "Gate rejection must be explicit.");
                Assert(factoryCalls == 0, "Disabled gate must not construct an adapter.");
            }

            private void ReadOnlySessionDispatchesOnlyFakeAdapter()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter();
                var processor = new BridgeRequestProcessor(
                    new CountingValidator(BridgeStatus.DeploymentValidated),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var validation = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-read-only-validate",
                    Command = "ValidateSdkDeployment"
                });
                Assert(validation.Success, "Read-only session requires successful deployment validation.");

                var open = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-read-only-open",
                    Command = "OpenConfiguredReadOnlySession"
                });
                Assert(open.Success, "Enabled read-only session should open through the fake adapter.");
                Assert(fake.OpenCount == 1, "Open must be the only session-start adapter call.");

                var status = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-read-only-status",
                    Command = "GetConfiguredNodeBasicStatus"
                });
                Assert(status.Success, "Basic status should be read through the fake adapter.");
                Assert(status.Details.Initialized == "true", "Basic status must return initialized state.");
                Assert(status.Details.Homed == "true", "Basic status must return homed state.");

                var position = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-read-only-position",
                    Command = "GetConfiguredAxisPositions",
                    Axis = "x"
                });
                Assert(position.Success, "Calibrated X position should be read through the fake adapter.");
                Assert(position.Details.Position == "x=12.5", "Position response should be role-labeled and invariant-culture formatted.");
                Assert(fake.PositionReadCount == 1, "Position read should call the fake adapter exactly once.");

                var close = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-read-only-close",
                    Command = "CloseConfiguredReadOnlySession"
                });
                Assert(close.Success, "Read-only session should close through the fake adapter.");
                Assert(close.Details.SessionState == "Closed", "Close should report a closed session.");
                Assert(close.Details.CacheValid == false, "Close must invalidate cached reads.");
                Assert(fake.CloseCount == 1, "Close must be called exactly once.");
            }

            private void ReadOnlySessionRejectedWhenRuntimeDependencyWarning()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var factoryCalls = 0;
                var processor = new BridgeRequestProcessor(
                    new CountingValidator(BridgeStatus.DeploymentValidated, new List<string> { BridgeWarningCodes.SdkRuntimeDependenciesWarning }),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate
                    {
                        factoryCalls++;
                        return new FakeReadOnlyAdapter();
                    });

                var response = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-runtime-warning-reject",
                    Command = "OpenConfiguredReadOnlySession"
                });

                Assert(!response.Success, "Runtime dependency warning must reject open.");
                Assert(factoryCalls == 0, "Adapter must not be constructed when runtime warning is present.");
                Assert(response.Details.SessionState == "Blocked", "Session must be blocked when runtime warning is present.");
                Assert(response.Details.BlockReason == "DeploymentNotValidated", "Block reason must be DeploymentNotValidated.");
            }

            private void ReadOnlySessionRejectedWhenPortOrBaudInvalid()
            {
                // Sub-case 1: portNumber=0
                {
                    var baseConfig = CreateReadOnlyConfig();
                    baseConfig.Usb2Can.PortNumber = 0;
                    var config = SoconReadOnlyConfig.FromBridgeConfig(baseConfig);
                    var factoryCalls = 0;
                    var processor = new BridgeRequestProcessor(
                        new CountingValidator(BridgeStatus.DeploymentValidated),
                        BridgeStatus.Offline,
                        config,
                        new RealReadOnlySessionGate(config, true),
                        delegate
                        {
                            factoryCalls++;
                            return new FakeReadOnlyAdapter();
                        });

                    var response = processor.Process(new BridgeRequest
                    {
                        RequestId = "self-test-port-invalid",
                        Command = "OpenConfiguredReadOnlySession"
                    });

                    Assert(!response.Success, "PortNumber=0 must reject open.");
                    Assert(factoryCalls == 0, "Adapter must not be constructed when PortNumber=0.");
                    Assert(response.Details.SessionState == "Blocked", "Session must be blocked when PortNumber=0.");
                    Assert(response.Details.BlockReason == "OpenPortNumberInvalid", "Block reason must be OpenPortNumberInvalid.");
                }

                // Sub-case 2: baudRate=0
                {
                    var baseConfig = CreateReadOnlyConfig();
                    baseConfig.Usb2Can.BaudRate = 0;
                    var config = SoconReadOnlyConfig.FromBridgeConfig(baseConfig);
                    var factoryCalls = 0;
                    var processor = new BridgeRequestProcessor(
                        new CountingValidator(BridgeStatus.DeploymentValidated),
                        BridgeStatus.Offline,
                        config,
                        new RealReadOnlySessionGate(config, true),
                        delegate
                        {
                            factoryCalls++;
                            return new FakeReadOnlyAdapter();
                        });

                    var response = processor.Process(new BridgeRequest
                    {
                        RequestId = "self-test-baud-invalid",
                        Command = "OpenConfiguredReadOnlySession"
                    });

                    Assert(!response.Success, "BaudRate=0 must reject open.");
                    Assert(factoryCalls == 0, "Adapter must not be constructed when BaudRate=0.");
                    Assert(response.Details.SessionState == "Blocked", "Session must be blocked when BaudRate=0.");
                    Assert(response.Details.BlockReason == "OpenBaudRateInvalid", "Block reason must be OpenBaudRateInvalid.");
                }
            }

            private void ReadOnlySessionCloseFailsWhenClosePortReturnsFalse()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter(false);
                var processor = new BridgeRequestProcessor(
                    new CountingValidator(BridgeStatus.DeploymentValidated),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var open = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-close-fail-open",
                    Command = "OpenConfiguredReadOnlySession"
                });
                Assert(open.Success, "Open must succeed before testing close failure.");
                Assert(fake.OpenCount == 1, "Open must be called exactly once.");

                var close = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-close-fail-close",
                    Command = "CloseConfiguredReadOnlySession"
                });
                Assert(!close.Success, "Close must fail when adapter Close returns false.");
                Assert(close.Details.SessionState == "Blocked", "Session must be blocked after failed close.");
                Assert(close.Details.CacheValid == false, "Cache must be invalid after failed close.");
                Assert(fake.CloseCount == 1, "Adapter Close must be called exactly once.");
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
                return CreateValidator(baseDirectory, sdkDirectory, isX86Process, true);
            }

            private SdkDeploymentValidator CreateValidator(string baseDirectory, string sdkDirectory, bool isX86Process, bool managedLoadSucceeds)
            {
                return CreateValidator(baseDirectory, sdkDirectory, isX86Process, managedLoadSucceeds, true);
            }

            private SdkDeploymentValidator CreateValidator(
                string baseDirectory,
                string sdkDirectory,
                bool isX86Process,
                bool managedLoadSucceeds,
                bool requiredTypesAvailable)
            {
                var options = new SdkDeploymentValidatorOptions(baseDirectory);
                options.EnvironmentSdkDirectoryProvider = delegate { return sdkDirectory; };

                return new SdkDeploymentValidator(
                    options,
                    new FixedArchitectureProbe(isX86Process),
                    new PeArchitectureInspector(),
                    new FixedManagedAssemblyLoadProbe(managedLoadSucceeds, requiredTypesAvailable));
            }

            private void ConfiguredSdkRuntimeValidation()
            {
                var options = new SdkDeploymentValidatorOptions(AppDomain.CurrentDomain.BaseDirectory);
                var validator = new SdkDeploymentValidator(
                    options,
                    new DefaultProcessArchitectureProbe(),
                    new PeArchitectureInspector(),
                    new ReflectionOnlyManagedAssemblyLoadProbe());

                var result = validator.Validate();
                WriteDiagnosticReport(result);

                if (result.RuntimeDetails.SdkPathConfigured == true)
                {
                    Assert(result.DiagnosticSuccess, "Configured SDK diagnostic validation should pass.");
                }
            }

            private void WriteDiagnosticReport(SdkDeploymentValidationResult result)
            {
                var details = result.RuntimeDetails;

                output.WriteLine("SOCON Diagnostic Report");
                output.WriteLine();
                output.WriteLine("SDK Path: {0}", details.SdkDirectory ?? "(not configured)");
                output.WriteLine("SDK Source: {0}", details.SdkPathSource ?? "unknown");
                output.WriteLine("SDK Path Exists: {0}", FormatAvailability(details.SdkDirectoryExists));
                output.WriteLine("Process Architecture: {0}", details.ProcessArchitecture ?? "unknown");

                output.WriteLine();
                output.WriteLine("Assemblies:");
                WriteManagedAssemblyReport(details, "SOCON.API.dll", "SOCON.API");
                WriteManagedAssemblyReport(details, "SOCON.Utility.dll", "SOCON.Utility");
                output.WriteLine("can_bootloader:");
                output.WriteLine("  Exists: {0}", FormatFileCheck(details, "can_bootloader.dll"));

                output.WriteLine(
                    "  PE Architecture: {0}{1}",
                    details.NativeDllCheckSucceeded == true ? "x86" : FormatNullableResult(details.NativeDllCheckSucceeded),
                    string.IsNullOrEmpty(details.CanBootloaderMachine) ? string.Empty : " (" + details.CanBootloaderMachine + ")");
                output.WriteLine(
                    "  File Version: {0}",
                    string.IsNullOrEmpty(details.CanBootloaderFileVersion) ? "unavailable" : details.CanBootloaderFileVersion);

                output.WriteLine();
                output.WriteLine("Available Types:");
                foreach (var typeCheck in details.TypeChecks)
                {
                    output.WriteLine(
                        "{0}: {1} ({2})",
                        typeCheck.DisplayName,
                        typeCheck.Available ? "available" : "missing",
                        typeCheck.FullName);
                }

                output.WriteLine();
                output.WriteLine(
                    "Exception Details: {0}",
                    details.ExceptionDetails.Count == 0 ? "none" : string.Join("; ", details.ExceptionDetails.ToArray()));
                output.WriteLine("Result: {0}", result.DiagnosticSuccess ? "PASS" : "FAIL");
            }

            private void WriteManagedAssemblyReport(
                SdkRuntimeValidationDetails details,
                string fileName,
                string displayName)
            {
                var load = FindManagedAssemblyLoad(details, fileName);

                output.WriteLine("{0}:", displayName);
                output.WriteLine("  Exists: {0}", FormatFileCheck(details, fileName));
                output.WriteLine(
                    "  Assembly Version: {0}",
                    load == null || string.IsNullOrEmpty(load.AssemblyVersion) ? "unavailable" : load.AssemblyVersion);
                output.WriteLine(
                    "  Metadata Load: {0}",
                    load == null ? "not run" : (load.Success ? "PASS" : "FAIL"));
            }

            private static ManagedAssemblyLoadResult FindManagedAssemblyLoad(
                SdkRuntimeValidationDetails details,
                string fileName)
            {
                foreach (var load in details.ManagedAssemblyLoads)
                {
                    if (string.Equals(load.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return load;
                    }
                }

                return null;
            }

            private static string FormatFileCheck(SdkRuntimeValidationDetails details, string fileName)
            {
                return details.FileChecks.Contains(fileName + "=present") ? "yes" : "no";
            }

            private static string FormatAvailability(bool? value)
            {
                if (!value.HasValue)
                {
                    return "unknown";
                }

                return value.Value ? "yes" : "no";
            }

            private static string FormatNullableResult(bool? result)
            {
                if (!result.HasValue)
                {
                    return "not run";
                }

                return result.Value ? "OK" : "failed";
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

        private static BridgeConfig CreateReadOnlyConfig()
        {
            return new BridgeConfig
            {
                SdkDirectory = "self-test-sdk",
                RealReadOnlyEnabled = true,
                Usb2Can = new Usb2CanConfig
                {
                    ConnectionType = "CONN_USB",
                    PortNumber = 9,
                    BaudRate = 9600
                },
                WhitelistNodes = new List<int> { 10, 11, 12, 13 },
                AxisMappings = new AxisMappings
                {
                    X = new AxisMapping { NodeId = 10, Axis = "X" },
                    Y = new AxisMapping { NodeId = 11, Axis = "Y" },
                    Z1 = new AxisMapping { NodeId = 12, Axis = "Z" },
                    Z2 = new AxisMapping { NodeId = 13, Axis = "Z" }
                },
                AxisCalibration = new AxisCalibration
                {
                    X = true,
                    Y = true,
                    Z1 = true,
                    Z2 = true
                }
            };
        }

        private sealed class FakeReadOnlyAdapter : ISoconReadOnlyAdapter
        {
            private readonly bool closeSucceeds;

            public int OpenCount { get; private set; }
            public int PositionReadCount { get; private set; }
            public int CloseCount { get; private set; }

            public FakeReadOnlyAdapter()
                : this(true)
            {
            }

            public FakeReadOnlyAdapter(bool closeSucceeds)
            {
                this.closeSucceeds = closeSucceeds;
            }

            public SoconAdapterResult Open(ReadOnlySessionParameters parameters)
            {
                OpenCount++;
                return new SoconAdapterResult { Success = true };
            }

            public SoconBasicStatusResult ReadBasicStatus(ReadOnlySessionParameters parameters)
            {
                return new SoconBasicStatusResult { Confirmed = true, Initialized = true, Homed = true };
            }

            public SoconAxisPositionResult ReadAxisPosition(ReadOnlySessionParameters parameters)
            {
                PositionReadCount++;
                return new SoconAxisPositionResult { Success = true, PositionMillimeters = 12.5d };
            }

            public SoconAdapterResult Close()
            {
                CloseCount++;
                return new SoconAdapterResult { Success = closeSucceeds };
            }

            public void Dispose()
            {
            }
        }

        private sealed class CountingValidator : ISdkDeploymentValidator
        {
            private readonly BridgeStatus status;
            private readonly List<string> warnings;

            public CountingValidator(BridgeStatus status)
                : this(status, null)
            {
            }

            public CountingValidator(BridgeStatus status, List<string> warnings)
            {
                this.status = status;
                this.warnings = warnings ?? new List<string>();
            }

            public int Count { get; private set; }

            public SdkDeploymentValidationResult Validate()
            {
                Count++;
                return new SdkDeploymentValidationResult(status, new BridgeResponseDetails(), warnings);
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

        private sealed class FixedManagedAssemblyLoadProbe : IManagedAssemblyLoadProbe
        {
            private readonly bool success;
            private readonly bool requiredTypesAvailable;

            public FixedManagedAssemblyLoadProbe(bool success, bool requiredTypesAvailable)
            {
                this.success = success;
                this.requiredTypesAvailable = requiredTypesAvailable;
            }

            public ManagedAssemblyLoadResult Load(string filePath, IEnumerable<ManagedTypeRequirement> typeRequirements)
            {
                var fileName = Path.GetFileName(filePath);
                if (!success)
                {
                    return ManagedAssemblyLoadResult.Failed(
                        fileName,
                        new BadImageFormatException("Simulated managed load failure."));
                }

                var typeChecks = new List<ManagedTypeCheckResult>();
                if (typeRequirements != null)
                {
                    foreach (var requirement in typeRequirements)
                    {
                        typeChecks.Add(new ManagedTypeCheckResult(
                            requirement.DisplayName,
                            requirement.FullName,
                            requiredTypesAvailable));
                    }
                }

                return ManagedAssemblyLoadResult.Succeeded(fileName, fileName, "1.0.0.0", typeChecks);
            }
        }
    }
}
