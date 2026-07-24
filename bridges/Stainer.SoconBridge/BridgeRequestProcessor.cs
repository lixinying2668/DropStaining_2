using System;
using System.Collections.Generic;
using System.Globalization;

namespace Stainer.SoconBridge
{
    internal sealed class BridgeRequestProcessor : IDisposable
    {
        private const string PingCommand = "Ping";
        private const string GetBridgeStatusCommand = "GetBridgeStatus";
        private const string ValidateSdkDeploymentCommand = "ValidateSdkDeployment";
        private const string OpenConfiguredReadOnlySessionCommand = "OpenConfiguredReadOnlySession";
        private const string GetConfiguredNodeBasicStatusCommand = "GetConfiguredNodeBasicStatus";
        private const string GetConfiguredAxisPositionsCommand = "GetConfiguredAxisPositions";
        private const string CloseConfiguredReadOnlySessionCommand = "CloseConfiguredReadOnlySession";
        private const string MoveConfiguredAxisCommand = "MoveConfiguredAxis";
        private const string AspirateConfiguredCommand = "AspirateConfigured";
        private const string DispenseConfiguredCommand = "DispenseConfigured";
        private const string DetectLiquidConfiguredCommand = "DetectLiquidConfigured";
        private const string StopConfiguredAxisCommand = "StopConfiguredAxis";

        private readonly ISdkDeploymentValidator validator;
        private readonly RealReadOnlySessionGate gate;
        private readonly SoconReadOnlyConfig config;
        private readonly Func<ISoconReadOnlyAdapter> adapterFactory;
        private readonly RealActionSessionGate actionGate;
        private readonly object sessionSync = new object();

        private BridgeStatus currentStatus;
        private SessionState sessionState;
        private ISoconReadOnlyAdapter currentAdapter;
        private bool cacheValid;
        private bool disposed;

        /// <summary>
        /// Backward-compatible 2-arg constructor. Session commands return
        /// <c>RealReadOnlyNotEnabled</c> because no gate/adapter is wired.
        /// </summary>
        public BridgeRequestProcessor(ISdkDeploymentValidator validator, BridgeStatus initialStatus)
            : this(validator, initialStatus, null, null, null, null)
        {
        }

        /// <summary>
        /// Test-friendly constructor (spec §6). Injects a fake adapter (or real
        /// adapter in non-test wiring), the gate, the parsed config, and the
        /// validator. Initial status defaults to <c>Offline</c>. Used by
        /// self-tests; never constructs the reflection adapter.
        /// </summary>
        public BridgeRequestProcessor(
            ISoconReadOnlyAdapter adapter,
            RealReadOnlySessionGate gate,
            SoconReadOnlyConfig config,
            ISdkDeploymentValidator validator)
            : this(validator, BridgeStatus.Offline, config, gate, WrapAsFactory(adapter), null)
        {
        }

        /// <summary>
        /// Full-injection constructor. The adapter factory is the only path
        /// through which the real reflection adapter may be constructed.
        /// </summary>
        internal BridgeRequestProcessor(
            ISdkDeploymentValidator validator,
            BridgeStatus initialStatus,
            SoconReadOnlyConfig config,
            RealReadOnlySessionGate gate,
            Func<ISoconReadOnlyAdapter> adapterFactory)
            : this(validator, initialStatus, config, gate, adapterFactory, null)
        {
        }

        internal BridgeRequestProcessor(
            ISdkDeploymentValidator validator,
            BridgeStatus initialStatus,
            SoconReadOnlyConfig config,
            RealReadOnlySessionGate gate,
            Func<ISoconReadOnlyAdapter> adapterFactory,
            RealActionSessionGate actionGate)
        {
            if (validator == null)
            {
                throw new ArgumentNullException("validator");
            }

            this.validator = validator;
            currentStatus = initialStatus;
            this.config = config;
            this.gate = gate;
            this.adapterFactory = adapterFactory;
            this.actionGate = actionGate;
            sessionState = SessionState.Closed;
            cacheValid = false;
        }

        private static Func<ISoconReadOnlyAdapter> WrapAsFactory(ISoconReadOnlyAdapter adapter)
        {
            if (adapter == null)
            {
                return null;
            }

            // Return the same instance on each call so tests can accumulate
            // call counters across session open/close cycles. The processor
            // never disposes the injected adapter between sessions.
            return delegate { return adapter; };
        }

        public BridgeStatus CurrentStatus
        {
            get { return currentStatus; }
        }

        public static BridgeRequestProcessor CreateDefault(string baseDirectory)
        {
            return CreateDefault(baseDirectory, false);
        }

        /// <summary>
        /// Wires real components. The reflection-based adapter is constructed
        /// lazily by the factory, ONLY when the gate is enabled and the SDK
        /// deployment has been validated. Never constructed during self-test
        /// or unit tests (those use the test friendly constructor).
        /// </summary>
        public static BridgeRequestProcessor CreateDefault(string baseDirectory, bool enableRealReadOnlyArg)
        {
            return CreateDefault(baseDirectory, enableRealReadOnlyArg, false);
        }

        public static BridgeRequestProcessor CreateDefault(
            string baseDirectory,
            bool enableRealReadOnlyArg,
            bool enableRealActionsArg)
        {
            var options = new SdkDeploymentValidatorOptions(baseDirectory);
            var deploymentValidator = new SdkDeploymentValidator(
                options,
                new DefaultProcessArchitectureProbe(),
                new PeArchitectureInspector(),
                new ReflectionOnlyManagedAssemblyLoadProbe());

            var loadedConfig = SoconReadOnlyConfig.Load(baseDirectory);
            var sessionGate = new RealReadOnlySessionGate(loadedConfig, enableRealReadOnlyArg || enableRealActionsArg);
            var realActionGate = new RealActionSessionGate(loadedConfig, enableRealActionsArg);

            var sdkDirectory = ResolveSdkDirectoryForFactory(loadedConfig, options);
            Func<ISoconReadOnlyAdapter> factory = delegate
            {
                // Defense-in-depth: the processor never calls this factory
                // unless the gate is enabled and currentStatus is
                // DeploymentValidated. Re-check here so a programming mistake
                // can never construct the real adapter offline.
                if (sessionGate == null || !sessionGate.IsEnabled)
                {
                    throw new InvalidOperationException("Real read-only gate is not enabled.");
                }

                if (string.IsNullOrWhiteSpace(sdkDirectory))
                {
                    throw new InvalidOperationException("SDK directory is not resolved.");
                }

                return new ReflectionBasedSoconReadOnlyAdapter(sdkDirectory);
            };

            return new BridgeRequestProcessor(
                deploymentValidator,
                BridgeStatus.Offline,
                loadedConfig,
                sessionGate,
                factory,
                realActionGate);
        }

        private static string ResolveSdkDirectoryForFactory(SoconReadOnlyConfig loadedConfig, SdkDeploymentValidatorOptions options)
        {
            if (loadedConfig != null)
            {
                var fromConfig = loadedConfig.SdkDirectory;
                if (!string.IsNullOrWhiteSpace(fromConfig))
                {
                    return fromConfig.Trim();
                }
            }

            if (options != null && options.EnvironmentSdkDirectoryProvider != null)
            {
                var fromEnv = options.EnvironmentSdkDirectoryProvider();
                if (!string.IsNullOrWhiteSpace(fromEnv))
                {
                    return fromEnv.Trim();
                }
            }

            return null;
        }

        public BridgeResponse Process(BridgeRequest request)
        {
            if (request == null)
            {
                request = new BridgeRequest();
            }

            var requestId = request.RequestId ?? string.Empty;
            var command = request.Command ?? string.Empty;

            Console.WriteLine(
                "IPC request command={0} requestId={1}",
                SanitizeForLog(command),
                SanitizeForLog(requestId));

            if (string.Equals(command, PingCommand, StringComparison.Ordinal))
            {
                return CreateResponse(requestId, command, true, currentStatus, "Pong", new BridgeResponseDetails(), new List<string>());
            }

            if (string.Equals(command, GetBridgeStatusCommand, StringComparison.Ordinal))
            {
                return CreateResponse(requestId, command, true, currentStatus, "OK", new BridgeResponseDetails(), new List<string>());
            }

            if (string.Equals(command, ValidateSdkDeploymentCommand, StringComparison.Ordinal))
            {
                return ProcessValidateSdkDeployment(requestId, command);
            }

            if (string.Equals(command, OpenConfiguredReadOnlySessionCommand, StringComparison.Ordinal))
            {
                lock (sessionSync)
                {
                    return ProcessOpenSession(requestId, command);
                }
            }

            if (string.Equals(command, GetConfiguredNodeBasicStatusCommand, StringComparison.Ordinal))
            {
                lock (sessionSync)
                {
                    return ProcessBasicStatus(requestId, command);
                }
            }

            if (string.Equals(command, GetConfiguredAxisPositionsCommand, StringComparison.Ordinal))
            {
                lock (sessionSync)
                {
                    return ProcessAxisPositions(requestId, command, request.Axis);
                }
            }

            if (string.Equals(command, CloseConfiguredReadOnlySessionCommand, StringComparison.Ordinal))
            {
                lock (sessionSync)
                {
                    return ProcessCloseSession(requestId, command);
                }
            }

            if (string.Equals(command, MoveConfiguredAxisCommand, StringComparison.Ordinal)
                || string.Equals(command, AspirateConfiguredCommand, StringComparison.Ordinal)
                || string.Equals(command, DispenseConfiguredCommand, StringComparison.Ordinal)
                || string.Equals(command, DetectLiquidConfiguredCommand, StringComparison.Ordinal)
                || string.Equals(command, StopConfiguredAxisCommand, StringComparison.Ordinal))
            {
                lock (sessionSync)
                {
                    return ProcessAction(requestId, command, request);
                }
            }

            return CreateResponse(requestId, command, false, currentStatus, "NotSupported", new BridgeResponseDetails(), new List<string>());
        }

        private BridgeResponse ProcessValidateSdkDeployment(string requestId, string command)
        {
            var result = validator.Validate();
            currentStatus = result.Status;

            Console.WriteLine(
                "SDK deployment validation status={0} warnings={1}",
                result.Status,
                result.Warnings.Count);

            return CreateResponse(
                requestId,
                command,
                result.Success,
                result.Status,
                result.Success ? "DeploymentValidated" : "DeploymentNotReady",
                result.Details,
                result.Warnings);
        }

        // ---- Session commands (gated, fail-closed) ----

        /// <summary>
        /// Gate + deployment precheck shared by every session command except
        /// Close (which is always permitted). Returns null to continue, or a
        /// fully-built failure response.
        /// </summary>
        private BridgeResponse CheckGateAndDeployment(string requestId, string command)
        {
            if (gate == null || !gate.IsEnabled)
            {
                var details = new BridgeResponseDetails();
                details.BlockReason = "RealReadOnlyNotEnabled";
                return CreateResponse(
                    requestId,
                    command,
                    false,
                    BridgeStatus.RealReadOnlyNotEnabled,
                    "RealReadOnlyNotEnabled",
                    details,
                    new List<string>());
            }

            if (currentStatus != BridgeStatus.DeploymentValidated)
            {
                var blockReason = currentStatus == BridgeStatus.SdkVersionInconsistent
                    ? "SdkVersionInconsistent"
                    : "DeploymentNotValidated";
                var details = new BridgeResponseDetails();
                details.BlockReason = blockReason;
                return CreateResponse(
                    requestId,
                    command,
                    false,
                    currentStatus,
                    "DeploymentNotValidated",
                    details,
                    new List<string>());
            }

            return null;
        }

        private BridgeResponse ProcessOpenSession(string requestId, string command)
        {
            // Gate 1: dual-enable gate (launch flag --enable-real-read-only AND
            // local config realReadOnlyEnabled=true). The real adapter is never
            // constructed and no COM port is ever opened when this gate is off.
            if (gate == null || !gate.IsEnabled)
            {
                var gateDetails = new BridgeResponseDetails();
                gateDetails.BlockReason = "RealReadOnlyNotEnabled";
                return CreateResponse(
                    requestId,
                    command,
                    false,
                    BridgeStatus.RealReadOnlyNotEnabled,
                    "RealReadOnlyNotEnabled",
                    gateDetails,
                    new List<string>());
            }

            if (sessionState == SessionState.Open)
            {
                var details = SessionDetails("Open", true, cacheValid);
                return CreateResponse(requestId, command, false, currentStatus, "SessionAlreadyOpen", details, new List<string>());
            }

            if (sessionState == SessionState.Blocked)
            {
                return CreateBlockedResponse(requestId, command, "SessionBlocked");
            }

            // Gate 2: FRESH deployment validation. Never rely on a historical
            // currentStatus -- the SDK files may have changed since the last
            // check. Requires Success==true AND no BLOCKING runtime dependency
            // missing. The blocking subset (C1.C1Zip.4.dll) is reported via the
            // structured Details.BlockingRuntimeDependenciesPresent flag, which
            // the validator always populates on this success path. An advisory
            // dependency (SOCON.ScEventBus.dll) still surfaces as
            // SdkRuntimeDependenciesWarning but does NOT block -- it is only
            // reachable via SOCON.Utility.Common.CheckRet, never on the OpenPort
            // path. != true fail-closes when the flag is null (validator did not
            // attest blocking deps present).
            var deployment = validator.Validate();
            currentStatus = deployment.Status;
            var blockingRuntimeDependencyMissing = deployment.Details.BlockingRuntimeDependenciesPresent != true;
            Console.WriteLine(
                "OpenConfiguredReadOnlySession deployment status={0} blockingRuntimeDependencyMissing={1} runtimeWarning={2}",
                deployment.Status,
                blockingRuntimeDependencyMissing,
                ContainsRuntimeDependencyWarning(deployment.Warnings));

            if (!deployment.Success || blockingRuntimeDependencyMissing)
            {
                TransitionTo(SessionState.Blocked, "DeploymentNotValidated");
                return CreateBlockedResponse(requestId, command, "DeploymentNotValidated");
            }

            // Gate 3: connection-parameter fail-closed validation. Performed
            // BEFORE the adapter is constructed so OpenPort is never called with
            // an invalid CONN type, port, baud, empty whitelist, missing
            // representative node, or an unwhitelisted/invalid axis mapping. The
            // returned code never carries a path/COM/NodeID.
            if (config == null)
            {
                TransitionTo(SessionState.Blocked, "OpenConfigInvalid");
                return CreateBlockedResponse(requestId, command, "OpenConfigInvalid");
            }

            var preconditionError = config.ValidateSessionPreconditions();
            if (preconditionError != null)
            {
                TransitionTo(SessionState.Blocked, preconditionError);
                return CreateBlockedResponse(requestId, command, preconditionError);
            }

            if (adapterFactory == null)
            {
                TransitionTo(SessionState.Blocked, "NoAdapterFactory");
                return CreateBlockedResponse(requestId, command, "NoAdapterFactory");
            }

            // ValidateSessionPreconditions guaranteed a representative node,
            // so BuildRepresentativeParameters is non-null here.
            var parameters = config.BuildRepresentativeParameters();

            ISoconReadOnlyAdapter adapter = null;
            try
            {
                adapter = adapterFactory();
                if (adapter == null)
                {
                    throw new InvalidOperationException("Adapter factory returned null.");
                }

                var openResult = adapter.Open(parameters);
                if (openResult == null || !openResult.Success)
                {
                    var code = openResult == null ? "OpenFailed" : (openResult.ErrorCode ?? "OpenFailed");
                    // Open failed: release the adapter (Close + Dispose) before blocking.
                    ReleaseAdapter(adapter);
                    TransitionTo(SessionState.Blocked, code);
                    return CreateBlockedResponse(requestId, command, code);
                }

                currentAdapter = adapter;
                cacheValid = false;
                TransitionTo(SessionState.Open, null);

                var details = SessionDetails("Open", true, false);
                return CreateResponse(requestId, command, true, currentStatus, "SessionOpen", details, new List<string>());
            }
            catch (Exception ex)
            {
                // Open threw: release the adapter (Close + Dispose) before blocking.
                ReleaseAdapter(adapter);
                Console.WriteLine("OpenConfiguredReadOnlySession exception type={0}", ex.GetType().Name);
                TransitionTo(SessionState.Blocked, "OpenException");
                return CreateBlockedResponse(requestId, command, "OpenException");
            }
        }

        private static bool ContainsRuntimeDependencyWarning(List<string> warnings)
        {
            if (warnings == null)
            {
                return false;
            }

            foreach (var warning in warnings)
            {
                if (string.Equals(warning, BridgeWarningCodes.SdkRuntimeDependenciesWarning, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private BridgeResponse ProcessBasicStatus(string requestId, string command)
        {
            var gateCheck = CheckGateAndDeployment(requestId, command);
            if (gateCheck != null)
            {
                return gateCheck;
            }

            if (sessionState != SessionState.Open)
            {
                return StateNotOpenResponse(requestId, command);
            }

            var parameters = config.BuildRepresentativeParameters();
            if (parameters == null)
            {
                TransitionTo(SessionState.Blocked, "NoRepresentativeNode");
                return CreateBlockedResponse(requestId, command, "NoRepresentativeNode");
            }

            try
            {
                var status = currentAdapter.ReadBasicStatus(parameters);
                if (status == null || !status.Confirmed)
                {
                    // Never fabricate init/home values when unconfirmed.
                    TransitionTo(SessionState.Blocked, "ErrorStateUnconfirmed");
                    return CreateBlockedResponse(requestId, command, "ErrorStateUnconfirmed");
                }

                cacheValid = true;
                var details = SessionDetails("Open", true, true);
                details.Initialized = status.Initialized ? "true" : "false";
                details.Homed = status.Homed ? "true" : "false";
                return CreateResponse(requestId, command, true, currentStatus, "OK", details, new List<string>());
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetConfiguredNodeBasicStatus exception type={0}", ex.GetType().Name);
                TransitionTo(SessionState.Blocked, "BasicStatusException");
                return CreateBlockedResponse(requestId, command, "BasicStatusException");
            }
        }

        private BridgeResponse ProcessAxisPositions(string requestId, string command, string axisPayload)
        {
            var gateCheck = CheckGateAndDeployment(requestId, command);
            if (gateCheck != null)
            {
                return gateCheck;
            }

            var role = SoconReadOnlyConfig.ParseAxisRole(axisPayload);
            if (!role.HasValue)
            {
                // Only x|y|z1|z2 accepted. Any other value (including any
                // COM/NodeID/path-like payload) is rejected as NotSupported.
                return CreateResponse(requestId, command, false, currentStatus, "NotSupported", new BridgeResponseDetails(), new List<string>());
            }

            if (sessionState != SessionState.Open)
            {
                return StateNotOpenResponse(requestId, command);
            }

            if (!config.IsAxisCalibrated(role.Value))
            {
                // Adapter NOT called. No SetPerMM/SetMaxTrip. No Demo value.
                var details = SessionDetails("Open", true, cacheValid);
                details.BlockReason = "AxisCalibrationNotCompleted";
                return CreateResponse(requestId, command, false, currentStatus, "BLOCKED", details, new List<string>());
            }

            var parameters = config.BuildParameters(role.Value);
            if (parameters == null)
            {
                TransitionTo(SessionState.Blocked, "AxisNotWhitelisted");
                return CreateBlockedResponse(requestId, command, "AxisNotWhitelisted");
            }

            try
            {
                var position = currentAdapter.ReadAxisPosition(parameters);
                if (position == null || !position.Success || !position.PositionMillimeters.HasValue)
                {
                    var code = position == null ? "PositionReadFailed" : (position.ErrorCode ?? "PositionReadFailed");
                    TransitionTo(SessionState.Blocked, code);
                    return CreateBlockedResponse(requestId, command, code);
                }

                var details = SessionDetails("Open", true, cacheValid);
                details.Position = FormatPosition(role.Value, position.PositionMillimeters.Value);
                return CreateResponse(requestId, command, true, currentStatus, "OK", details, new List<string>());
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetConfiguredAxisPositions exception type={0}", ex.GetType().Name);
                TransitionTo(SessionState.Blocked, "AxisPositionException");
                return CreateBlockedResponse(requestId, command, "AxisPositionException");
            }
        }

        private BridgeResponse ProcessCloseSession(string requestId, string command)
        {
            // Close is permitted from ANY state (Closed, Open, Blocked). When an
            // adapter is active, ClosePort's real return value is authoritative:
            // only a confirmed success transitions to Closed. A false/missing/
            // throwing ClosePort fails closed to SessionBlocked -- the bridge
            // never reports a session closed that the device did not confirm. No
            // port/path/NodeID or underlying exception detail is leaked.
            //
            // Lifecycle: Close() is called first; the adapter is Dispose()d in
            // finally REGARDLESS of the close outcome, so a failed or throwing
            // ClosePort still releases the underlying SCDevice. The active
            // adapter reference and cache are cleared after release.
            SoconAdapterResult closeResult = null;
            var adapter = currentAdapter;
            if (adapter != null)
            {
                try
                {
                    closeResult = adapter.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("CloseConfiguredReadOnlySession exception type={0}", ex.GetType().Name);
                    closeResult = null;
                }
                finally
                {
                    // Always dispose, even when ClosePort failed or threw.
                    DisposeAdapterSafely(adapter);
                    currentAdapter = null;
                    cacheValid = false;
                }

                if (closeResult == null || !closeResult.Success)
                {
                    TransitionTo(SessionState.Blocked, "CloseFailed");
                    return CreateBlockedResponse(requestId, command, "CloseFailed");
                }
            }

            cacheValid = false;
            var previousState = sessionState;
            sessionState = SessionState.Closed;
            Console.WriteLine("Session state transition {0}->Closed reason=close", previousState);

            var details = SessionDetails("Closed", false, false);
            return CreateResponse(requestId, command, true, currentStatus, "SessionClosed", details, new List<string>());
        }

        private BridgeResponse ProcessAction(string requestId, string command, BridgeRequest request)
        {
            if (actionGate == null || !actionGate.IsEnabled)
            {
                var disabled = SessionDetails(sessionState.ToString(), sessionState == SessionState.Open, cacheValid);
                disabled.BlockReason = "RealActionsNotEnabled";
                return CreateResponse(requestId, command, false, currentStatus, "BLOCKED", disabled, new List<string>());
            }

            if (sessionState != SessionState.Open || currentAdapter == null)
            {
                return StateNotOpenResponse(requestId, command);
            }

            var preconditionError = config == null ? "ActionConfigInvalid" : config.ValidateActionPreconditions();
            if (preconditionError != null)
            {
                return ActionBlocked(requestId, command, preconditionError);
            }

            var role = SoconReadOnlyConfig.ParseAxisRole(request.Axis);
            if (!role.HasValue || !config.IsAxisWhitelisted(role.Value) || !config.IsAxisCalibrated(role.Value))
            {
                return ActionBlocked(requestId, command, "ActionAxisNotAuthorized");
            }

            var parameters = config.BuildParameters(role.Value);
            var actionAdapter = currentAdapter as ISoconActionAdapter;
            if (parameters == null || actionAdapter == null)
            {
                return ActionBlocked(requestId, command, "ActionAdapterUnavailable");
            }

            SoconAdapterResult result;
            try
            {
                if (string.Equals(command, MoveConfiguredAxisCommand, StringComparison.Ordinal))
                {
                    if (!request.PositionMm.HasValue
                        || !request.SpeedMmPerSecond.HasValue
                        || !config.IsPositionAllowed(role.Value, request.PositionMm.Value)
                        || !config.IsSpeedAllowed(request.SpeedMmPerSecond.Value))
                    {
                        return ActionBlocked(requestId, command, "ActionMoveRangeInvalid");
                    }

                    result = actionAdapter.MoveAxis(
                        parameters,
                        request.PositionMm.Value,
                        request.SpeedMmPerSecond.Value,
                        config.ActionTimeoutMilliseconds);
                }
                else if (string.Equals(command, StopConfiguredAxisCommand, StringComparison.Ordinal))
                {
                    result = actionAdapter.Stop(parameters);
                }
                else
                {
                    if (role.Value != AxisRole.Z1 && role.Value != AxisRole.Z2)
                    {
                        return ActionBlocked(requestId, command, "ActionPipetteAxisInvalid");
                    }

                    if (string.Equals(command, AspirateConfiguredCommand, StringComparison.Ordinal)
                        || string.Equals(command, DispenseConfiguredCommand, StringComparison.Ordinal))
                    {
                        if (!request.VolumeUl.HasValue || !config.IsVolumeAllowed(request.VolumeUl.Value))
                        {
                            return ActionBlocked(requestId, command, "ActionVolumeInvalid");
                        }

                        result = string.Equals(command, AspirateConfiguredCommand, StringComparison.Ordinal)
                            ? actionAdapter.Aspirate(parameters, request.VolumeUl.Value, config.ActionTimeoutMilliseconds)
                            : actionAdapter.Dispense(parameters, request.VolumeUl.Value, config.ActionTimeoutMilliseconds);
                    }
                    else
                    {
                        if (!request.StartMm.HasValue
                            || !request.MaximumMm.HasValue
                            || request.StartMm.Value > request.MaximumMm.Value
                            || !config.IsPositionAllowed(role.Value, request.StartMm.Value)
                            || !config.IsPositionAllowed(role.Value, request.MaximumMm.Value))
                        {
                            return ActionBlocked(requestId, command, "ActionLiquidDetectRangeInvalid");
                        }

                        result = actionAdapter.DetectLiquid(
                            parameters,
                            request.StartMm.Value,
                            request.MaximumMm.Value,
                            config.ActionTimeoutMilliseconds);
                    }
                }
            }
            catch (Exception ex)
            {
                BestEffortStop(actionAdapter, parameters);
                Console.WriteLine("SOCON action exception command={0} type={1}", SanitizeForLog(command), ex.GetType().Name);
                TransitionTo(SessionState.Blocked, "ActionException");
                return CreateBlockedResponse(requestId, command, "ActionException");
            }

            if (result == null || !result.Success)
            {
                BestEffortStop(actionAdapter, parameters);
                var code = result == null ? "ActionFailed" : (result.ErrorCode ?? "ActionFailed");
                TransitionTo(SessionState.Blocked, code);
                return CreateBlockedResponse(requestId, command, code);
            }

            var details = SessionDetails("Open", true, false);
            details.Action = command;
            return CreateResponse(requestId, command, true, currentStatus, "ActionCompleted", details, new List<string>());
        }

        private BridgeResponse ActionBlocked(string requestId, string command, string reason)
        {
            var details = SessionDetails(sessionState.ToString(), sessionState == SessionState.Open, cacheValid);
            details.BlockReason = reason;
            return CreateResponse(requestId, command, false, currentStatus, "BLOCKED", details, new List<string>());
        }

        private static void BestEffortStop(ISoconActionAdapter adapter, ReadOnlySessionParameters parameters)
        {
            try
            {
                if (adapter != null && parameters != null)
                {
                    adapter.Stop(parameters);
                }
            }
            catch
            {
                // Original action failure remains authoritative.
            }
        }

        // ---- Helpers ----

        private BridgeResponse StateNotOpenResponse(string requestId, string command)
        {
            if (sessionState == SessionState.Blocked)
            {
                return CreateBlockedResponse(requestId, command, "SessionBlocked");
            }

            var details = SessionDetails("Closed", false, false);
            details.BlockReason = "SessionClosed";
            return CreateResponse(requestId, command, false, currentStatus, "SessionClosed", details, new List<string>());
        }

        private BridgeResponse CreateBlockedResponse(string requestId, string command, string blockReason)
        {
            var details = SessionDetails(sessionState.ToString(), sessionState == SessionState.Open, cacheValid);
            details.BlockReason = blockReason;
            return CreateResponse(requestId, command, false, currentStatus, "BLOCKED", details, new List<string>());
        }

        private static BridgeResponseDetails SessionDetails(string state, bool isOpen, bool isCacheValid)
        {
            return new BridgeResponseDetails
            {
                SessionState = state,
                SessionOpen = isOpen,
                CacheValid = isCacheValid
            };
        }

        private void TransitionTo(SessionState newState, string reason)
        {
            if (sessionState == newState)
            {
                return;
            }

            var previous = sessionState;
            sessionState = newState;
            Console.WriteLine(
                "Session state transition {0}->{1} reason={2}",
                previous,
                newState,
                string.IsNullOrEmpty(reason) ? "-" : SanitizeForLog(reason));
        }

        private static void BestEffortClose(ISoconReadOnlyAdapter adapter)
        {
            if (adapter == null)
            {
                return;
            }

            try
            {
                adapter.Close();
            }
            catch (Exception)
            {
                // Best effort: ClosePort must not throw out of Close.
            }
        }

        // Best-effort Close then Dispose. Used on the Open failure/exception
        // paths and on processor shutdown, where the close outcome is not
        // captured. Exactly one Close and one Dispose per adapter.
        private static void ReleaseAdapter(ISoconReadOnlyAdapter adapter)
        {
            if (adapter == null)
            {
                return;
            }

            BestEffortClose(adapter);
            DisposeAdapterSafely(adapter);
        }

        private static void DisposeAdapterSafely(ISoconReadOnlyAdapter adapter)
        {
            if (adapter == null)
            {
                return;
            }

            try
            {
                adapter.Dispose();
            }
            catch (Exception)
            {
                // Best effort: Dispose must not throw out of session teardown.
            }
        }

        /// <summary>
        /// Releases the active session adapter (Close -&gt; Dispose) under the
        /// session lock. Idempotent; used at Bridge shutdown. Never throws and a
        /// disposal exception never overwrites the last session outcome.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;

            lock (sessionSync)
            {
                var adapter = currentAdapter;
                currentAdapter = null;
                cacheValid = false;
                if (adapter != null)
                {
                    // Session was Open: execute Close -> Dispose on the adapter.
                    ReleaseAdapter(adapter);
                }
            }
        }

        private static string FormatPosition(AxisRole role, double position)
        {
            // Label by axis ROLE only; never by NodeID. Use invariant culture
            // so no locale-specific separators leak.
            return RoleLabel(role) + "=" + position.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string RoleLabel(AxisRole role)
        {
            switch (role)
            {
                case AxisRole.X: return "x";
                case AxisRole.Y: return "y";
                case AxisRole.Z1: return "z1";
                case AxisRole.Z2: return "z2";
                default: return "axis";
            }
        }

        private static BridgeResponse CreateResponse(
            string requestId,
            string command,
            bool success,
            BridgeStatus status,
            string message,
            BridgeResponseDetails details,
            List<string> warnings)
        {
            return new BridgeResponse
            {
                RequestId = requestId ?? string.Empty,
                Command = command ?? string.Empty,
                Success = success,
                BridgeStatus = status.ToString(),
                Message = message ?? string.Empty,
                Details = details ?? new BridgeResponseDetails(),
                Warnings = warnings ?? new List<string>()
            };
        }

        private static string SanitizeForLog(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "-";
            }

            var chars = value.ToCharArray();
            var limit = Math.Min(chars.Length, 80);
            var sanitized = new char[limit];
            for (var i = 0; i < limit; i++)
            {
                var c = chars[i];
                sanitized[i] = char.IsControl(c) ? '_' : c;
            }

            return new string(sanitized);
        }

        private enum SessionState
        {
            Closed,
            Open,
            Blocked
        }
    }
}
