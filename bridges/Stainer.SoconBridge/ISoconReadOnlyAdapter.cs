using System;

namespace Stainer.SoconBridge
{
    /// <summary>
    /// Axis role accepted by the controlled read-only bridge IPC. The bridge
    /// NEVER accepts an arbitrary COM port, NodeID or path from IPC. Only these
    /// four logical axis roles are exposed; their physical mapping is resolved
    /// from PRIVATE local config under the whitelist constraint.
    /// </summary>
    internal enum AxisRole
    {
        X,
        Y,
        Z1,
        Z2
    }

    /// <summary>
    /// Resolved session parameters consumed by the read-only adapter. Built
    /// from PRIVATE config only (never from IPC). PhysicalAxis is "X"|"Y"|"Z"
    /// because the underlying SDK exposes GetXPos/GetYPos/GetZ only.
    /// </summary>
    internal sealed class ReadOnlySessionParameters
    {
        public string ConnectionType;   // must be "CONN_USB"
        public int PortNumber;
        public int BaudRate;
        public int NodeId;              // resolved per-axis from axisMappings + whitelist check
        public string PhysicalAxis;     // "X" | "Y" | "Z"
    }

    /// <summary>
    /// Result of Open / Close operations. ErrorCode is a safe short code, never
    /// a path/port/NodeID/coordinate.
    /// </summary>
    internal sealed class SoconAdapterResult
    {
        public bool Success;
        public string ErrorCode;
    }

    /// <summary>
    /// Result of CheckIsInited + CheckHome reads. Confirmed=false means the
    /// underlying SDK reported an error state and the init/home booleans are
    /// NOT authoritative. Callers MUST treat Confirmed=false as BLOCKED and
    /// must not fabricate init/home values.
    /// </summary>
    internal sealed class SoconBasicStatusResult
    {
        public bool Initialized;
        public bool Homed;
        public bool Confirmed;
        public string ErrorCode;
    }

    /// <summary>
    /// Result of GetXPos/GetYPos/GetZ. PositionMillimeters may be null when the
    /// read failed; callers report BLOCKED and never fabricate a coordinate.
    /// </summary>
    internal sealed class SoconAxisPositionResult
    {
        public bool Success;
        public double? PositionMillimeters;
        public string ErrorCode;
    }

    /// <summary>
    /// Encapsulates the ONLY eight SDK operations the bridge may ever invoke:
    /// CONN_USB, OpenPort, CheckIsInited, CheckHome, GetXPos, GetYPos, GetZ,
    /// ClosePort. No Init/Move/Wait/LiqDet/Aspirate/Dispense/IO/Scan/Register/
    /// Reconnect/SetPerMM/SetMaxTrip members are exposed. Prohibited operations
    /// remain NotSupported at every layer of the bridge.
    /// </summary>
    /// <remarks>
    /// Implementations MUST be defensive. Exceptions propagate so the processor
    /// can fail-closed (transition the session to BLOCKED). No auto retry, no
    /// auto reconnect.
    /// </remarks>
    internal interface ISoconReadOnlyAdapter : IDisposable
    {
        /// <summary>
        /// Performs CONN_USB then OpenPort using the resolved parameters.
        /// </summary>
        SoconAdapterResult Open(ReadOnlySessionParameters parameters);

        /// <summary>
        /// Reads CheckIsInited + CheckHome only. Never triggers init or homing.
        /// </summary>
        SoconBasicStatusResult ReadBasicStatus(ReadOnlySessionParameters parameters);

        /// <summary>
        /// Reads GetXPos / GetYPos / GetZ based on PhysicalAxis. Never moves.
        /// </summary>
        SoconAxisPositionResult ReadAxisPosition(ReadOnlySessionParameters parameters);

        /// <summary>
        /// Calls ClosePort (USB2CAN close). Best-effort; never throws.
        /// </summary>
        SoconAdapterResult Close();
    }
}
