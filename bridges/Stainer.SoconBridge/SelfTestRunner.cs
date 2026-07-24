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
                DeploymentValidatedWhenOnlyScEventBusMissing();
                BlockingRuntimeDependencyReportedWhenC1ZipMissing();
                ActionCommandsAreNotSupported();
                ReadOnlySessionGateFailsClosed();
                ReadOnlySessionDispatchesOnlyFakeAdapter();
                ReadOnlySessionOpensWhenOnlyScEventBusMissing();
                ReadOnlySessionBlockedWhenC1ZipMissing();
                ReadOnlySessionBlockedWhenBothRuntimeDepsMissing();
                ReadOnlySessionFailsClosedWhenBlockingPresenceNotAttested();
                LengthPrefixedProtocolRejectsMalformedRequests();
                ConfiguredSdkRuntimeValidation();
                ReadOnlySessionRejectedWhenPortOrBaudInvalid();
                ReadOnlySessionCloseFailsWhenClosePortReturnsFalse();
                ActionSessionDispatchesWhitelistedCommands();
                // P0-4 ClosePort / Dispose lifecycle coverage:
                CloseThenDisposeOrderAfterOpen();
                AdapterReleasedWhenOpenFails();
                AdapterReleasedWhenOpenThrows();
                CloseFailedStillDisposedAndBlocked();
                CloseThrowsStillDisposedAndBlocked();
                ProcessorDisposeReleasesOpenAdapter();
                RepeatedDisposeAndCloseDoNotDoubleRelease();
                MultiRoundOpenCloseReleasesIndependentAdapters();
                DisposeThrowDoesNotOverwriteCloseResult();
            }

            // ---- P0-4 lifecycle: ClosePort / Dispose ----

            private void CloseThenDisposeOrderAfterOpen()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter();
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var open = processor.Process(new BridgeRequest { RequestId = "p04-close-order", Command = "OpenConfiguredReadOnlySession" });
                Assert(open.Success, "Open should succeed before close.");

                var close = processor.Process(new BridgeRequest { RequestId = "p04-close-order", Command = "CloseConfiguredReadOnlySession" });
                Assert(close.Success, "Close should succeed.");

                Assert(fake.CloseCount == 1, "Close must be called exactly once.");
                Assert(fake.DisposeCount == 1, "Dispose must be called exactly once after close.");
                Assert(fake.Calls.Count >= 2, "Call trace must record Close and Dispose.");
                Assert(fake.Calls[fake.Calls.Count - 2] == "Close", "Close must precede Dispose.");
                Assert(fake.Calls[fake.Calls.Count - 1] == "Dispose", "Dispose must follow Close.");
            }

            private void AdapterReleasedWhenOpenFails()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter(false, false, true, false, false);
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var open = processor.Process(new BridgeRequest { RequestId = "p04-open-fail", Command = "OpenConfiguredReadOnlySession" });
                Assert(!open.Success, "Open failure must be reported.");
                Assert(open.Details.SessionState == "Blocked", "Session must block on open failure.");

                Assert(fake.OpenCount == 1, "Open must be attempted once.");
                Assert(fake.CloseCount == 1, "Adapter must be closed once after open failure.");
                Assert(fake.DisposeCount == 1, "Adapter must be disposed once after open failure.");
            }

            private void AdapterReleasedWhenOpenThrows()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter(true, true, true, false, false);
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var open = processor.Process(new BridgeRequest { RequestId = "p04-open-throw", Command = "OpenConfiguredReadOnlySession" });
                Assert(!open.Success, "Open throw must fail-closed.");
                Assert(open.Details.SessionState == "Blocked", "Session must block on open throw.");
                Assert(open.Details.BlockReason == "OpenException", "Block reason must be OpenException.");

                Assert(fake.OpenCount == 1, "Open must be attempted once.");
                Assert(fake.CloseCount == 1, "Adapter must be closed once after open threw.");
                Assert(fake.DisposeCount == 1, "Adapter must be disposed once after open threw.");
            }

            private void CloseFailedStillDisposedAndBlocked()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter(false);
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var open = processor.Process(new BridgeRequest { RequestId = "p04-close-fail", Command = "OpenConfiguredReadOnlySession" });
                Assert(open.Success, "Open should succeed before testing close failure.");

                var close = processor.Process(new BridgeRequest { RequestId = "p04-close-fail", Command = "CloseConfiguredReadOnlySession" });
                Assert(!close.Success, "ClosePort failure must block.");
                Assert(close.Details.SessionState == "Blocked", "Session must be blocked when ClosePort fails.");
                Assert(close.Details.BlockReason == "CloseFailed", "Block reason must be CloseFailed.");

                Assert(fake.CloseCount == 1, "Close must be attempted once.");
                Assert(fake.DisposeCount == 1, "Adapter must be disposed once even when ClosePort failed.");
            }

            private void CloseThrowsStillDisposedAndBlocked()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter(true, false, true, true, false);
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var open = processor.Process(new BridgeRequest { RequestId = "p04-close-throw", Command = "OpenConfiguredReadOnlySession" });
                Assert(open.Success, "Open should succeed before testing close throw.");

                var close = processor.Process(new BridgeRequest { RequestId = "p04-close-throw", Command = "CloseConfiguredReadOnlySession" });
                Assert(!close.Success, "Throwing ClosePort must block.");
                Assert(close.Details.SessionState == "Blocked", "Session must be blocked when ClosePort throws.");
                Assert(close.Details.BlockReason == "CloseFailed", "Block reason must be CloseFailed.");

                Assert(fake.CloseCount == 1, "Close must be attempted once.");
                Assert(fake.DisposeCount == 1, "Adapter must be disposed once even when ClosePort threw.");
            }

            private void ProcessorDisposeReleasesOpenAdapter()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter();
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var open = processor.Process(new BridgeRequest { RequestId = "p04-proc-dispose", Command = "OpenConfiguredReadOnlySession" });
                Assert(open.Success, "Open should succeed before processor dispose.");

                processor.Dispose();

                // Session was Open: processor.Dispose must execute Close -> Dispose once each.
                Assert(fake.CloseCount == 1, "Processor.Dispose must Close the active adapter once.");
                Assert(fake.DisposeCount == 1, "Processor.Dispose must Dispose the active adapter once.");
                Assert(fake.Calls[fake.Calls.Count - 2] == "Close", "Processor.Dispose must Close before Dispose.");
                Assert(fake.Calls[fake.Calls.Count - 1] == "Dispose", "Processor.Dispose must Dispose after Close.");
            }

            private void RepeatedDisposeAndCloseDoNotDoubleRelease()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter();
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var open = processor.Process(new BridgeRequest { RequestId = "p04-idempotent", Command = "OpenConfiguredReadOnlySession" });
                Assert(open.Success, "Open should succeed.");

                processor.Dispose();
                processor.Dispose();
                Assert(fake.CloseCount == 1, "Repeated processor.Dispose must not Close again.");
                Assert(fake.DisposeCount == 1, "Repeated processor.Dispose must not Dispose again.");

                // Closing after disposal must not re-invoke the (already released) adapter.
                processor.Process(new BridgeRequest { RequestId = "p04-idempotent", Command = "CloseConfiguredReadOnlySession" });
                Assert(fake.CloseCount == 1, "Close after dispose must not re-invoke adapter Close.");
                Assert(fake.DisposeCount == 1, "Close after dispose must not re-invoke adapter Dispose.");

                processor.Dispose();
            }

            private void MultiRoundOpenCloseReleasesIndependentAdapters()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var created = new List<FakeReadOnlyAdapter>();
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate
                    {
                        var fresh = new FakeReadOnlyAdapter();
                        created.Add(fresh);
                        return fresh;
                    });

                for (var i = 0; i < 2; i++)
                {
                    var open = processor.Process(new BridgeRequest { RequestId = "p04-multi-" + i, Command = "OpenConfiguredReadOnlySession" });
                    Assert(open.Success, "Each round should open.");
                    var close = processor.Process(new BridgeRequest { RequestId = "p04-multi-" + i, Command = "CloseConfiguredReadOnlySession" });
                    Assert(close.Success, "Each round should close.");
                }

                Assert(created.Count == 2, "Each round must create an independent adapter.");
                foreach (var fresh in created)
                {
                    Assert(fresh.OpenCount == 1, "Each adapter must be opened once.");
                    Assert(fresh.CloseCount == 1, "Each adapter must be closed once.");
                    Assert(fresh.DisposeCount == 1, "Each adapter must be disposed once.");
                }

                processor.Dispose();
            }

            private void DisposeThrowDoesNotOverwriteCloseResult()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var fake = new FakeReadOnlyAdapter(true, false, true, false, true);
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; });

                var open = processor.Process(new BridgeRequest { RequestId = "p04-dispose-throw", Command = "OpenConfiguredReadOnlySession" });
                Assert(open.Success, "Open should succeed.");

                var close = processor.Process(new BridgeRequest { RequestId = "p04-dispose-throw", Command = "CloseConfiguredReadOnlySession" });
                // Close succeeded; the subsequent Dispose threw but must NOT turn this into a failure.
                Assert(close.Success, "Dispose exception must not overwrite a successful Close result.");
                Assert(close.Details.SessionState == "Closed", "Session must close despite Dispose throwing.");
                Assert(close.Message == "SessionClosed", "Close message must reflect confirmed close.");
                Assert(fake.CloseCount == 1, "Close called once.");
                Assert(fake.DisposeCount == 1, "Dispose attempted once (and swallowed).");

                processor.Dispose();
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
                    Assert(result.Details.BlockingRuntimeDependenciesPresent == false, "A blocking runtime dependency is missing when both are absent.");
                });
            }

            private void DeploymentValidatedWhenOnlyScEventBusMissing()
            {
                WithTempDirectory(delegate(string sdkDirectory)
                {
                    WriteCoreFakeSdk(sdkDirectory);
                    WriteTextFile(Path.Combine(sdkDirectory, "C1.C1Zip.4.dll"), "fake");
                    // SOCON.ScEventBus.dll intentionally absent (advisory only).

                    var validator = CreateValidator(sdkDirectory, sdkDirectory, true);
                    var result = validator.Validate();

                    Assert(result.Status == BridgeStatus.DeploymentValidated, "Only ScEventBus missing should still validate.");
                    Assert(Contains(result.Warnings, BridgeWarningCodes.SdkRuntimeDependenciesWarning), "Advisory dependency warning should still be returned.");
                    Assert(Contains(result.Details.MissingFiles, "SOCON.ScEventBus.dll"), "Missing SOCON.ScEventBus.dll should be reported.");
                    Assert(!Contains(result.Details.MissingFiles, "C1.C1Zip.4.dll"), "C1.C1Zip.4.dll must not be reported when present.");
                    Assert(result.Details.BlockingRuntimeDependenciesPresent == true, "No blocking runtime dependency is missing when only ScEventBus is absent.");
                    Assert(result.Details.RuntimeDependenciesPresent == false, "An advisory dependency is still missing, so the union flag stays false.");
                });
            }

            private void BlockingRuntimeDependencyReportedWhenC1ZipMissing()
            {
                WithTempDirectory(delegate(string sdkDirectory)
                {
                    WriteCoreFakeSdk(sdkDirectory);
                    WriteTextFile(Path.Combine(sdkDirectory, "SOCON.ScEventBus.dll"), "fake");
                    // C1.C1Zip.4.dll intentionally absent (blocking).

                    var validator = CreateValidator(sdkDirectory, sdkDirectory, true);
                    var result = validator.Validate();

                    Assert(result.Status == BridgeStatus.DeploymentValidated, "C1Zip missing should not change core validation status.");
                    Assert(Contains(result.Warnings, BridgeWarningCodes.SdkRuntimeDependenciesWarning), "Runtime dependency warning should be returned.");
                    Assert(Contains(result.Details.MissingFiles, "C1.C1Zip.4.dll"), "Missing C1.C1Zip.4.dll should be reported.");
                    Assert(!Contains(result.Details.MissingFiles, "SOCON.ScEventBus.dll"), "SOCON.ScEventBus.dll must not be reported when present.");
                    Assert(result.Details.BlockingRuntimeDependenciesPresent == false, "A blocking runtime dependency is missing when C1Zip is absent.");
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
                    ValidatedDeployment(),
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

            private void ReadOnlySessionOpensWhenOnlyScEventBusMissing()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var factoryCalls = 0;
                var fake = new FakeReadOnlyAdapter();
                var processor = new BridgeRequestProcessor(
                    ValidatorWithRuntimeState(true, new List<string> { "SOCON.ScEventBus.dll" }),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate
                    {
                        factoryCalls++;
                        return fake;
                    });

                var response = processor.Process(new BridgeRequest
                {
                    RequestId = "self-test-only-sceventbus-open",
                    Command = "OpenConfiguredReadOnlySession"
                });

                Assert(response.Success, "Only ScEventBus missing must NOT block open.");
                Assert(factoryCalls == 1, "Adapter factory must be invoked (enters Open) when only ScEventBus is missing.");
                Assert(fake.OpenCount == 1, "Adapter Open must be called when only ScEventBus is missing.");
                Assert(response.Details.SessionState == "Open", "Session must be Open when only ScEventBus is missing.");
            }

            private void ReadOnlySessionBlockedWhenC1ZipMissing()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var factoryCalls = 0;
                var processor = new BridgeRequestProcessor(
                    ValidatorWithRuntimeState(false, new List<string> { "C1.C1Zip.4.dll" }),
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
                    RequestId = "self-test-c1zip-missing-block",
                    Command = "OpenConfiguredReadOnlySession"
                });

                Assert(!response.Success, "Missing C1.C1Zip.4.dll must block open.");
                Assert(factoryCalls == 0, "Adapter must not be constructed when C1.C1Zip.4.dll is missing.");
                Assert(response.Details.SessionState == "Blocked", "Session must be blocked when C1.C1Zip.4.dll is missing.");
                Assert(response.Details.BlockReason == "DeploymentNotValidated", "Block reason must be DeploymentNotValidated.");
            }

            private void ReadOnlySessionBlockedWhenBothRuntimeDepsMissing()
            {
                var config = SoconReadOnlyConfig.FromBridgeConfig(CreateReadOnlyConfig());
                var factoryCalls = 0;
                var processor = new BridgeRequestProcessor(
                    ValidatorWithRuntimeState(false, new List<string> { "C1.C1Zip.4.dll", "SOCON.ScEventBus.dll" }),
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
                    RequestId = "self-test-both-runtime-missing-block",
                    Command = "OpenConfiguredReadOnlySession"
                });

                Assert(!response.Success, "Missing both runtime dependencies must block open.");
                Assert(factoryCalls == 0, "Adapter must not be constructed when C1.C1Zip.4.dll is among the missing deps.");
                Assert(response.Details.SessionState == "Blocked", "Session must be blocked when both runtime deps are missing.");
                Assert(response.Details.BlockReason == "DeploymentNotValidated", "Block reason must be DeploymentNotValidated.");
            }

            private void ReadOnlySessionFailsClosedWhenBlockingPresenceNotAttested()
            {
                // A validator that returns DeploymentValidated with a runtime
                // warning but does NOT populate BlockingRuntimeDependenciesPresent
                // (null) must fail closed: the processor cannot prove no blocking
                // dependency is missing, so it blocks before OpenPort.
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
                    RequestId = "self-test-blocking-presence-unknown",
                    Command = "OpenConfiguredReadOnlySession"
                });

                Assert(!response.Success, "Unknown blocking-presence must fail closed.");
                Assert(factoryCalls == 0, "Adapter must not be constructed when blocking presence is not attested.");
                Assert(response.Details.SessionState == "Blocked", "Session must be blocked when blocking presence is not attested.");
                Assert(response.Details.BlockReason == "DeploymentNotValidated", "Block reason must be DeploymentNotValidated.");
            }

            private void ActionSessionDispatchesWhitelistedCommands()
            {
                var rawConfig = CreateReadOnlyConfig();
                rawConfig.RealActionsEnabled = true;
                rawConfig.PipetteApiMode = "Z-SOPA";
                rawConfig.ActionLimits = new ActionLimits
                {
                    MinimumXMm = 0,
                    MaximumXMm = 500,
                    MinimumYMm = 0,
                    MaximumYMm = 500,
                    MinimumZMm = 0,
                    MaximumZMm = 200,
                    MaximumSpeedMmPerSecond = 100,
                    MaximumVolumeUl = 1000,
                    ActionTimeoutMilliseconds = 10000
                };
                var config = SoconReadOnlyConfig.FromBridgeConfig(rawConfig);
                var fake = new FakeActionAdapter();
                var processor = new BridgeRequestProcessor(
                    ValidatedDeployment(),
                    BridgeStatus.Offline,
                    config,
                    new RealReadOnlySessionGate(config, true),
                    delegate { return fake; },
                    new RealActionSessionGate(config, true));

                Assert(processor.Process(new BridgeRequest
                {
                    RequestId = "action-open",
                    Command = "OpenConfiguredReadOnlySession"
                }).Success, "Action session should open.");
                Assert(processor.Process(new BridgeRequest
                {
                    RequestId = "action-move",
                    Command = "MoveConfiguredAxis",
                    Axis = "x",
                    PositionMm = 100,
                    SpeedMmPerSecond = 50
                }).Success, "Whitelisted move should succeed.");
                Assert(processor.Process(new BridgeRequest
                {
                    RequestId = "action-aspirate",
                    Command = "AspirateConfigured",
                    Axis = "z1",
                    VolumeUl = 100
                }).Success, "Whitelisted aspirate should succeed.");
                var invalid = processor.Process(new BridgeRequest
                {
                    RequestId = "action-range",
                    Command = "MoveConfiguredAxis",
                    Axis = "x",
                    PositionMm = 999,
                    SpeedMmPerSecond = 50
                });
                Assert(!invalid.Success, "Out-of-range move must fail.");
                Assert(fake.MoveCount == 1, "Only the valid move may reach the adapter.");
                Assert(fake.AspirateCount == 1, "Aspirate should reach the adapter once.");
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
                        ValidatedDeployment(),
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
                        ValidatedDeployment(),
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
                    ValidatedDeployment(),
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

            // A deployment-validated validator that positively attests no
            // blocking runtime dependency is missing (the normal validated
            // state). Used by happy-path Open tests so Gate 2 passes.
            private static ISdkDeploymentValidator ValidatedDeployment()
            {
                return new CountingValidator(
                    BridgeStatus.DeploymentValidated,
                    null,
                    new BridgeResponseDetails { BlockingRuntimeDependenciesPresent = true });
            }

            // A deployment-validated validator that simulates a specific runtime
            // dependency state: blockingPresent drives the structured gate
            // signal, missingFiles drives both the warning and the reported
            // MissingFiles list.
            private static ISdkDeploymentValidator ValidatorWithRuntimeState(bool blockingPresent, List<string> missingFiles)
            {
                var warnings = (missingFiles == null || missingFiles.Count == 0)
                    ? new List<string>()
                    : new List<string> { BridgeWarningCodes.SdkRuntimeDependenciesWarning };
                var details = new BridgeResponseDetails
                {
                    BlockingRuntimeDependenciesPresent = blockingPresent,
                    MissingFiles = missingFiles
                };
                return new CountingValidator(BridgeStatus.DeploymentValidated, warnings, details);
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

            private static void WriteCoreFakeSdk(string sdkDirectory)
            {
                WriteTextFile(Path.Combine(sdkDirectory, "SOCON.API.dll"), "fake");
                WriteTextFile(Path.Combine(sdkDirectory, "SOCON.Utility.dll"), "fake");
                WriteFakePe(Path.Combine(sdkDirectory, "can_bootloader.dll"), X86Machine);
            }

            private static void WriteCompleteFakeSdk(string sdkDirectory, bool includeRuntimeDependencies)
            {
                WriteCoreFakeSdk(sdkDirectory);

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
                },
                PipetteApiMode = "Z-SOPA"
            };
        }

        private sealed class FakeReadOnlyAdapter : ISoconReadOnlyAdapter
        {
            private readonly bool openSucceeds;
            private readonly bool openThrows;
            private readonly bool closeSucceeds;
            private readonly bool closeThrows;
            private readonly bool disposeThrows;

            public int OpenCount { get; private set; }
            public int PositionReadCount { get; private set; }
            public int CloseCount { get; private set; }
            public int DisposeCount { get; private set; }
            public List<string> Calls { get; private set; }

            public FakeReadOnlyAdapter()
                : this(true)
            {
            }

            public FakeReadOnlyAdapter(bool closeSucceeds)
                : this(true, false, closeSucceeds, false, false)
            {
            }

            // Full control: openSucceeds / openThrows / closeSucceeds / closeThrows / disposeThrows.
            public FakeReadOnlyAdapter(bool openSucceeds, bool openThrows, bool closeSucceeds, bool closeThrows, bool disposeThrows)
            {
                this.openSucceeds = openSucceeds;
                this.openThrows = openThrows;
                this.closeSucceeds = closeSucceeds;
                this.closeThrows = closeThrows;
                this.disposeThrows = disposeThrows;
                Calls = new List<string>();
            }

            public SoconAdapterResult Open(ReadOnlySessionParameters parameters)
            {
                OpenCount++;
                Calls.Add("Open");
                if (openThrows)
                {
                    throw new InvalidOperationException("simulated open failure");
                }

                return new SoconAdapterResult { Success = openSucceeds };
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
                Calls.Add("Close");
                if (closeThrows)
                {
                    throw new InvalidOperationException("simulated close failure");
                }

                return new SoconAdapterResult { Success = closeSucceeds };
            }

            public void Dispose()
            {
                DisposeCount++;
                Calls.Add("Dispose");
                if (disposeThrows)
                {
                    throw new InvalidOperationException("simulated dispose failure");
                }
            }
        }

        private sealed class FakeActionAdapter : ISoconActionAdapter
        {
            public int MoveCount { get; private set; }
            public int AspirateCount { get; private set; }

            public SoconAdapterResult Open(ReadOnlySessionParameters parameters)
            {
                return new SoconAdapterResult { Success = true };
            }

            public SoconBasicStatusResult ReadBasicStatus(ReadOnlySessionParameters parameters)
            {
                return new SoconBasicStatusResult { Confirmed = true, Initialized = true, Homed = true };
            }

            public SoconAxisPositionResult ReadAxisPosition(ReadOnlySessionParameters parameters)
            {
                return new SoconAxisPositionResult { Success = true, PositionMillimeters = 0 };
            }

            public SoconAdapterResult MoveAxis(ReadOnlySessionParameters parameters, double positionMm, double speedMmPerSecond, int timeoutMilliseconds)
            {
                MoveCount++;
                return new SoconAdapterResult { Success = true };
            }

            public SoconAdapterResult Aspirate(ReadOnlySessionParameters parameters, int volumeUl, int timeoutMilliseconds)
            {
                AspirateCount++;
                return new SoconAdapterResult { Success = true };
            }

            public SoconAdapterResult Dispense(ReadOnlySessionParameters parameters, int volumeUl, int timeoutMilliseconds)
            {
                return new SoconAdapterResult { Success = true };
            }

            public SoconAdapterResult DetectLiquid(ReadOnlySessionParameters parameters, double startMm, double maximumMm, int timeoutMilliseconds)
            {
                return new SoconAdapterResult { Success = true };
            }

            public SoconAdapterResult Stop(ReadOnlySessionParameters parameters)
            {
                return new SoconAdapterResult { Success = true };
            }

            public SoconAdapterResult Close()
            {
                return new SoconAdapterResult { Success = true };
            }

            public void Dispose()
            {
            }
        }

        private sealed class CountingValidator : ISdkDeploymentValidator
        {
            private readonly BridgeStatus status;
            private readonly List<string> warnings;
            private readonly BridgeResponseDetails details;

            public CountingValidator(BridgeStatus status)
                : this(status, null, null)
            {
            }

            public CountingValidator(BridgeStatus status, List<string> warnings)
                : this(status, warnings, null)
            {
            }

            public CountingValidator(BridgeStatus status, List<string> warnings, BridgeResponseDetails details)
            {
                this.status = status;
                this.warnings = warnings ?? new List<string>();
                this.details = details;
            }

            public int Count { get; private set; }

            public SdkDeploymentValidationResult Validate()
            {
                Count++;
                return new SdkDeploymentValidationResult(status, details ?? new BridgeResponseDetails(), warnings);
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
