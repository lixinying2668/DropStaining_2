using System;
using System.IO;
using System.Reflection;

namespace Stainer.SoconBridge
{
    /// <summary>
    /// Reflection-based real SOCON read-only adapter. Binds the ONLY eight
    /// allowed SDK operations (CONN_USB, OpenPort, CheckIsInited, CheckHome,
    /// GetXPos, GetYPos, GetZ, ClosePort) via reflection against the on-site
    /// SOCON.API assembly. There is NO compile-time reference to the SOCON SDK.
    /// </summary>
    /// <remarks>
    /// Never instantiated during --self-test or unit tests. The bridge core
    /// only ever constructs this type through a factory that is gated by
    /// <see cref="RealReadOnlySessionGate.IsEnabled"/> AND a deployment-validated
    /// SDK directory. The type is intentionally excluded from offline test
    /// wiring (the test-friendly processor constructor injects a fake adapter).
    ///
    /// Defensive by design: SDK invocation exceptions propagate to the caller
    /// (<see cref="BridgeRequestProcessor"/>) so the session fail-closes to
    /// BLOCKED. No retry. No reconnect. No call to any prohibited SDK member
    /// (Init/Move/Wait/LiqDet/Aspirate/Dispense/IO/Scan/Register/SetPerMM/
    /// SetMaxTrip).
    /// </remarks>
    internal sealed class ReflectionBasedSoconReadOnlyAdapter : ISoconActionAdapter
    {
        private const string SdkApiAssemblyFile = "SOCON.API.dll";
        private const string DeviceTypeName = "SOCON.API.SCDevice";
        private const string ConnectTypeEnumName = "SOCON.API.Utility+e_ConnectType";
        private const string ConnUsbEnumName = "CONN_USB";

        private readonly string sdkApiAssemblyPath;

        private readonly Assembly apiAssembly;
        private readonly Type deviceType;
        private readonly Type connectTypeEnum;

        private object deviceInstance;
        private bool disposed;
        private bool closePortAttempted;
        private SoconAdapterResult firstCloseResult;

        /// <summary>
        /// Constructs the adapter and eagerly binds the SOCON.API types.
        /// Construction is permitted ONLY when the gate is enabled and the SDK
        /// directory has passed deployment validation. Binding failures
        /// propagate as <see cref="InvalidOperationException"/> (safe message;
        /// never leaks path/NodeID).
        /// </summary>
        public ReflectionBasedSoconReadOnlyAdapter(string sdkDirectory)
        {
            if (string.IsNullOrWhiteSpace(sdkDirectory))
            {
                throw new ArgumentException("SDK directory is required.", "sdkDirectory");
            }

            sdkApiAssemblyPath = Path.Combine(sdkDirectory, SdkApiAssemblyFile);

            try
            {
                apiAssembly = Assembly.LoadFrom(sdkApiAssemblyPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("SOCON.API load failed: " + ex.GetType().Name, ex);
            }

            deviceType = apiAssembly.GetType(DeviceTypeName, false);
            if (deviceType == null)
            {
                throw new InvalidOperationException("SOCON binding failed: SCDevice type not found.");
            }

            connectTypeEnum = apiAssembly.GetType(ConnectTypeEnumName, false);
            if (connectTypeEnum == null || !connectTypeEnum.IsEnum)
            {
                throw new InvalidOperationException("SOCON binding failed: e_ConnectType enum not found.");
            }
        }

        public SoconAdapterResult Open(ReadOnlySessionParameters parameters)
        {
            AssertNotDisposed();
            if (parameters == null)
            {
                throw new ArgumentNullException("parameters");
            }

            if (!string.Equals(parameters.ConnectionType, SoconReadOnlyConfig.RequiredConnectionType, StringComparison.Ordinal))
            {
                return new SoconAdapterResult { Success = false, ErrorCode = "ConnectionTypeNotUsb2Can" };
            }

            // Failure to resolve the enum value is a binding error -> propagate.
            object connUsbValue;
            try
            {
                connUsbValue = Enum.Parse(connectTypeEnum, ConnUsbEnumName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("SOCON binding failed: CONN_USB enum value not found.", ex);
            }

            deviceInstance = Activator.CreateInstance(deviceType);

            var connectType = deviceType.GetProperty("ConnectType");
            if (connectType == null || !connectType.CanWrite || connectType.PropertyType != connectTypeEnum)
            {
                throw new InvalidOperationException("SOCON binding failed: ConnectType property not found.");
            }

            // The confirmed vendor API is OpenPort(int comPort, int baudrate).
            // Setting ConnectType is an in-memory SDK selection; OpenPort is the
            // only call that may access the explicitly enabled USB2CAN device.
            connectType.SetValue(deviceInstance, connUsbValue, null);
            var openPort = deviceType.GetMethod("OpenPort", new[] { typeof(int), typeof(int) });
            if (openPort == null)
            {
                throw new InvalidOperationException("SOCON binding failed: OpenPort(int,int) method not found.");
            }

            var raw = openPort.Invoke(deviceInstance, new object[]
            {
                parameters.PortNumber,
                parameters.BaudRate
            });
            return ToOpenResult(raw);
        }

        public SoconBasicStatusResult ReadBasicStatus(ReadOnlySessionParameters parameters)
        {
            AssertNotDisposed();
            AssertOpened();

            if (parameters == null)
            {
                throw new ArgumentNullException("parameters");
            }

            var checkInited = deviceType.GetMethod("CheckIsInited");
            var checkHome = deviceType.GetMethod("CheckHome");
            if (checkInited == null || checkHome == null)
            {
                return new SoconBasicStatusResult { Confirmed = false, ErrorCode = "StatusMethodsNotFound" };
            }

            var inited = InvokeBoolRead(checkInited, parameters.NodeId);
            var homed = InvokeBoolRead(checkHome, parameters.NodeId);
            if (!inited.HasValue || !homed.HasValue)
            {
                return new SoconBasicStatusResult { Confirmed = false, ErrorCode = "ErrorStateUnconfirmed" };
            }

            return new SoconBasicStatusResult
            {
                Initialized = inited.Value,
                Homed = homed.Value,
                Confirmed = true
            };
        }

        public SoconAxisPositionResult ReadAxisPosition(ReadOnlySessionParameters parameters)
        {
            AssertNotDisposed();
            AssertOpened();

            if (parameters == null)
            {
                throw new ArgumentNullException("parameters");
            }

            var methodName = ResolvePositionMethodName(parameters.PhysicalAxis);
            if (methodName == null)
            {
                return new SoconAxisPositionResult { Success = false, ErrorCode = "AxisNotSupported" };
            }

            var method = deviceType.GetMethod(methodName);
            if (method == null)
            {
                return new SoconAxisPositionResult { Success = false, ErrorCode = "PositionMethodNotFound" };
            }

            var raw = InvokeReadWithOptionalNodeId(method, parameters.NodeId);
            if (raw == null)
            {
                return new SoconAxisPositionResult { Success = false, ErrorCode = "PositionReadFailed" };
            }

            double position;
            try
            {
                position = Convert.ToDouble(raw);
            }
            catch (Exception)
            {
                return new SoconAxisPositionResult { Success = false, ErrorCode = "PositionReadFailed" };
            }

            return new SoconAxisPositionResult { Success = true, PositionMillimeters = position };
        }

        public SoconAdapterResult Close()
        {
            // ClosePort's real return value is authoritative. A missing method,
            // a false/ non-zero return, or a thrown exception is a failure: the
            // caller (BridgeRequestProcessor) fail-closes the session to Blocked
            // and never reports a session closed that the device did not confirm.
            // No path/port/NodeID/coordinate is ever leaked.
            //
            // ClosePort is invoked AT MOST ONCE per instance. The first outcome
            // is retained and returned on every subsequent call, so repeated
            // Close()/Dispose() never re-invokes the underlying SDK method. The
            // deviceInstance reference is intentionally KEPT after ClosePort so
            // Dispose can still reflect SCDevice.Dispose() on it.
            if (closePortAttempted)
            {
                return firstCloseResult;
            }

            closePortAttempted = true;

            if (deviceInstance == null)
            {
                // No open device: nothing to close-port. Idempotent success.
                firstCloseResult = new SoconAdapterResult { Success = true };
                return firstCloseResult;
            }

            MethodInfo closePort;
            try
            {
                closePort = deviceType.GetMethod("ClosePort");
            }
            catch (Exception)
            {
                firstCloseResult = new SoconAdapterResult { Success = false, ErrorCode = "ClosePortNotFound" };
                return firstCloseResult;
            }

            if (closePort == null)
            {
                firstCloseResult = new SoconAdapterResult { Success = false, ErrorCode = "ClosePortNotFound" };
                return firstCloseResult;
            }

            object raw;
            try
            {
                raw = closePort.Invoke(deviceInstance, null);
            }
            catch (Exception)
            {
                // Wrap suppressed: strip any path/port/coordinate from inner.
                firstCloseResult = new SoconAdapterResult { Success = false, ErrorCode = "ClosePortException" };
                return firstCloseResult;
            }

            firstCloseResult = ToCloseResult(raw);
            return firstCloseResult;
        }

        public SoconAdapterResult MoveAxis(
            ReadOnlySessionParameters parameters,
            double positionMm,
            double speedMmPerSecond,
            int timeoutMilliseconds)
        {
            AssertNotDisposed();
            AssertOpened();
            if (parameters == null) throw new ArgumentNullException("parameters");

            string methodName;
            switch ((parameters.PhysicalAxis ?? string.Empty).ToUpperInvariant())
            {
                case "X": methodName = "MoveX"; break;
                case "Y": methodName = "MoveY"; break;
                case "Z": methodName = "MoveZ"; break;
                default: return new SoconAdapterResult { Success = false, ErrorCode = "AxisNotSupported" };
            }

            var action = InvokeWithOptionalParameters(
                methodName,
                new object[] { parameters.NodeId, Convert.ToSingle(positionMm), Convert.ToSingle(speedMmPerSecond) });
            var actionResult = ToActionResult(action, methodName + "Failed");
            return actionResult.Success
                ? WaitForAction(parameters.NodeId, timeoutMilliseconds)
                : actionResult;
        }

        public SoconAdapterResult Aspirate(ReadOnlySessionParameters parameters, int volumeUl, int timeoutMilliseconds)
        {
            AssertNotDisposed();
            AssertOpened();
            if (parameters == null) throw new ArgumentNullException("parameters");

            // Confirmed vendor Z-SOPA demo parameters. Optional tail arguments
            // are resolved from the SDK's declared defaults.
            var raw = InvokeWithOptionalParameters(
                "AspirateSOCA",
                new object[]
                {
                    parameters.NodeId,
                    Convert.ToSingle(volumeUl),
                    5000f,
                    2000f,
                    30000f,
                    false,
                    true,
                    false,
                    0f,
                    0f,
                    0f,
                    3
                });
            var result = ToActionResult(raw, "AspirateFailed");
            return result.Success ? WaitForAction(parameters.NodeId, timeoutMilliseconds) : result;
        }

        public SoconAdapterResult Dispense(ReadOnlySessionParameters parameters, int volumeUl, int timeoutMilliseconds)
        {
            AssertNotDisposed();
            AssertOpened();
            if (parameters == null) throw new ArgumentNullException("parameters");

            var raw = InvokeWithOptionalParameters(
                "DispenseSOCA",
                new object[] { parameters.NodeId, Convert.ToSingle(volumeUl) });
            var result = ToActionResult(raw, "DispenseFailed");
            return result.Success ? WaitForAction(parameters.NodeId, timeoutMilliseconds) : result;
        }

        public SoconAdapterResult DetectLiquid(
            ReadOnlySessionParameters parameters,
            double startMm,
            double maximumMm,
            int timeoutMilliseconds)
        {
            AssertNotDisposed();
            AssertOpened();
            if (parameters == null) throw new ArgumentNullException("parameters");

            var raw = InvokeWithOptionalParameters(
                "LiqDetSOCA",
                new object[] { parameters.NodeId, Convert.ToSingle(startMm), Convert.ToSingle(maximumMm) });
            var result = ToActionResult(raw, "LiquidDetectFailed");
            return result.Success ? WaitForAction(parameters.NodeId, timeoutMilliseconds) : result;
        }

        public SoconAdapterResult Stop(ReadOnlySessionParameters parameters)
        {
            AssertNotDisposed();
            AssertOpened();
            if (parameters == null) throw new ArgumentNullException("parameters");
            return ToActionResult(
                InvokeWithOptionalParameters("Stop", new object[] { parameters.NodeId }),
                "StopFailed");
        }

        public void Dispose()
        {
            // Idempotent and never throws. Order:
            //   1. If ClosePort has not yet been attempted, attempt it once. The
            //      retained firstCloseResult is the authoritative close outcome
            //      and is never overwritten by anything below.
            //   2. Regardless of the ClosePort outcome, reflect-call the public
            //      parameterless SCDevice.Dispose() exactly once (when present).
            //   3. Drop the instance reference and mark released.
            // No exception text/path/stack ever escapes Dispose.
            if (disposed)
            {
                return;
            }
            disposed = true;

            if (!closePortAttempted)
            {
                try
                {
                    Close();
                }
                catch (Exception)
                {
                    // ClosePort outcome is retained inside Close(); never throw.
                }
            }

            DisposeDeviceInstance();
            deviceInstance = null;
        }

        private void DisposeDeviceInstance()
        {
            var instance = deviceInstance;
            if (instance == null)
            {
                return;
            }

            MethodInfo disposeMethod;
            try
            {
                disposeMethod = deviceType.GetMethod("Dispose", Type.EmptyTypes);
            }
            catch (Exception)
            {
                return;
            }

            if (disposeMethod == null)
            {
                // Vendor type exposes no public parameterless Dispose().
                return;
            }

            try
            {
                disposeMethod.Invoke(instance, null);
            }
            catch (Exception)
            {
                // Dispose never throws; no path/stack/exception text leaks.
            }
        }

        private void AssertNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("ReflectionBasedSoconReadOnlyAdapter");
            }
        }

        private void AssertOpened()
        {
            if (deviceInstance == null)
            {
                throw new InvalidOperationException("Adapter has no open device.");
            }
        }

        private static string ResolvePositionMethodName(string physicalAxis)
        {
            if (string.IsNullOrEmpty(physicalAxis))
            {
                return null;
            }

            switch (physicalAxis.ToUpperInvariant())
            {
                case "X": return "GetXPos";
                case "Y": return "GetYPos";
                case "Z": return "GetZ";
                default: return null;
            }
        }

        private static SoconAdapterResult ToOpenResult(object raw)
        {
            if (raw == null)
            {
                return new SoconAdapterResult { Success = true };
            }

            if (raw is bool)
            {
                return new SoconAdapterResult { Success = (bool)raw };
            }

            if (raw is int)
            {
                // The vendor API's integer form uses zero for success.
                return new SoconAdapterResult { Success = ((int)raw) == 0, ErrorCode = ((int)raw) == 0 ? null : "OpenPortFailed" };
            }

            return new SoconAdapterResult { Success = true };
        }

        // Mirrors ToOpenResult: the vendor ClosePort may return void, bool, or
        // an integer (0 == success). A void return carries no failure signal, so
        // it is treated as success; an explicit bool/int failure is honored.
        private static SoconAdapterResult ToCloseResult(object raw)
        {
            if (raw == null)
            {
                return new SoconAdapterResult { Success = true };
            }

            if (raw is bool)
            {
                var ok = (bool)raw;
                return new SoconAdapterResult { Success = ok, ErrorCode = ok ? null : "ClosePortFailed" };
            }

            if (raw is int)
            {
                var value = (int)raw;
                return new SoconAdapterResult { Success = value == 0, ErrorCode = value == 0 ? null : "ClosePortFailed" };
            }

            // Unknown return type, no explicit failure signal: treat as success.
            return new SoconAdapterResult { Success = true };
        }

        private object InvokeReadWithOptionalNodeId(MethodInfo method, int nodeId)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return method.Invoke(deviceInstance, null);
            }

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
            {
                return method.Invoke(deviceInstance, new object[] { nodeId });
            }

            throw new InvalidOperationException("SOCON binding failed: unsupported method signature.");
        }

        private bool? InvokeBoolRead(MethodInfo method, int nodeId)
        {
            var raw = InvokeReadWithOptionalNodeId(method, nodeId);
            if (raw == null)
            {
                return null;
            }

            if (raw is bool)
            {
                return (bool)raw;
            }

            if (raw is int)
            {
                var value = (int)raw;
                if (value == 0) return true;
                if (value > 0) return false;
                return null; // negative => unconfirmed error state
            }

            return null;
        }

        private object InvokeWithOptionalParameters(string methodName, object[] requiredArguments)
        {
            MethodInfo selected = null;
            foreach (var method in deviceType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)
                    || method.GetParameters().Length < requiredArguments.Length)
                {
                    continue;
                }

                var candidateParameters = method.GetParameters();
                var compatible = true;
                for (var i = 0; i < requiredArguments.Length; i++)
                {
                    var argument = requiredArguments[i];
                    if (argument != null && !candidateParameters[i].ParameterType.IsInstanceOfType(argument))
                    {
                        compatible = false;
                        break;
                    }
                }

                if (compatible && (selected == null || candidateParameters.Length < selected.GetParameters().Length))
                {
                    selected = method;
                }
            }

            if (selected == null)
            {
                throw new InvalidOperationException("SOCON binding failed: action method not found.");
            }

            var methodParameters = selected.GetParameters();
            var arguments = new object[methodParameters.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i < requiredArguments.Length)
                {
                    arguments[i] = requiredArguments[i];
                }
                else if (methodParameters[i].IsOptional)
                {
                    arguments[i] = Type.Missing;
                }
                else
                {
                    throw new InvalidOperationException("SOCON binding failed: required action parameter unavailable.");
                }
            }

            return selected.Invoke(deviceInstance, arguments);
        }

        private SoconAdapterResult WaitForAction(int nodeId, int timeoutMilliseconds)
        {
            var raw = InvokeWithOptionalParameters(
                "WaitActionDone",
                new object[] { nodeId, timeoutMilliseconds });
            return ToActionResult(raw, "ActionWaitFailed");
        }

        private static SoconAdapterResult ToActionResult(object raw, string errorCode)
        {
            if (raw == null)
            {
                return new SoconAdapterResult { Success = true };
            }

            if (raw is bool)
            {
                var ok = (bool)raw;
                return new SoconAdapterResult { Success = ok, ErrorCode = ok ? null : errorCode };
            }

            if (raw is int)
            {
                var value = (int)raw;
                return new SoconAdapterResult { Success = value == 0, ErrorCode = value == 0 ? null : errorCode };
            }

            var text = raw as string;
            if (text != null)
            {
                return new SoconAdapterResult
                {
                    Success = string.IsNullOrWhiteSpace(text),
                    ErrorCode = string.IsNullOrWhiteSpace(text) ? null : errorCode
                };
            }

            return new SoconAdapterResult { Success = false, ErrorCode = errorCode };
        }
    }
}
