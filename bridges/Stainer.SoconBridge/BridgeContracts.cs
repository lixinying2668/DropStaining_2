using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Stainer.SoconBridge
{
    [DataContract]
    internal sealed class BridgeRequest
    {
        [DataMember(Name = "requestId", Order = 1)]
        public string RequestId { get; set; }

        [DataMember(Name = "command", Order = 2)]
        public string Command { get; set; }

        // Optional axis role payload for GetConfiguredAxisPositions.
        // Accepts only x|y|z1|z2 (case-insensitive). Never accepts COM/NodeID/path.
        [DataMember(Name = "axis", EmitDefaultValue = false, Order = 3)]
        public string Axis { get; set; }
    }

    [DataContract]
    internal sealed class BridgeResponse
    {
        public BridgeResponse()
        {
            Details = new BridgeResponseDetails();
            Warnings = new List<string>();
        }

        [DataMember(Name = "requestId", Order = 1)]
        public string RequestId { get; set; }

        [DataMember(Name = "command", Order = 2)]
        public string Command { get; set; }

        [DataMember(Name = "success", Order = 3)]
        public bool Success { get; set; }

        [DataMember(Name = "bridgeStatus", Order = 4)]
        public string BridgeStatus { get; set; }

        [DataMember(Name = "message", Order = 5)]
        public string Message { get; set; }

        [DataMember(Name = "details", Order = 6)]
        public BridgeResponseDetails Details { get; set; }

        [DataMember(Name = "warnings", Order = 7)]
        public List<string> Warnings { get; set; }
    }

    [DataContract]
    internal sealed class BridgeResponseDetails
    {
        [DataMember(Name = "isX86Process", EmitDefaultValue = false, Order = 1)]
        public bool? IsX86Process { get; set; }

        [DataMember(Name = "sdkPathConfigured", EmitDefaultValue = false, Order = 2)]
        public bool? SdkPathConfigured { get; set; }

        [DataMember(Name = "sdkDirectoryExists", EmitDefaultValue = false, Order = 3)]
        public bool? SdkDirectoryExists { get; set; }

        [DataMember(Name = "coreFilesPresent", EmitDefaultValue = false, Order = 4)]
        public bool? CoreFilesPresent { get; set; }

        [DataMember(Name = "runtimeDependenciesPresent", EmitDefaultValue = false, Order = 5)]
        public bool? RuntimeDependenciesPresent { get; set; }

        [DataMember(Name = "canBootloaderIsX86", EmitDefaultValue = false, Order = 6)]
        public bool? CanBootloaderIsX86 { get; set; }

        [DataMember(Name = "canBootloaderMachine", EmitDefaultValue = false, Order = 7)]
        public string CanBootloaderMachine { get; set; }

        [DataMember(Name = "missingFiles", EmitDefaultValue = false, Order = 8)]
        public List<string> MissingFiles { get; set; }

        [DataMember(Name = "warningCodes", EmitDefaultValue = false, Order = 9)]
        public List<string> WarningCodes { get; set; }

        // ---- Session-related fields (all optional, omitted unless populated) ----

        [DataMember(Name = "sdkVersionStatus", EmitDefaultValue = false, Order = 10)]
        public string SdkVersionStatus { get; set; }

        [DataMember(Name = "sessionOpen", EmitDefaultValue = false, Order = 11)]
        public bool? SessionOpen { get; set; }

        [DataMember(Name = "sessionState", EmitDefaultValue = false, Order = 12)]
        public string SessionState { get; set; }

        [DataMember(Name = "cacheValid", EmitDefaultValue = false, Order = 13)]
        public bool? CacheValid { get; set; }

        [DataMember(Name = "initialized", EmitDefaultValue = false, Order = 14)]
        public string Initialized { get; set; }

        [DataMember(Name = "homed", EmitDefaultValue = false, Order = 15)]
        public string Homed { get; set; }

        [DataMember(Name = "position", EmitDefaultValue = false, Order = 16)]
        public string Position { get; set; }

        [DataMember(Name = "blockReason", EmitDefaultValue = false, Order = 17)]
        public string BlockReason { get; set; }
    }

    [DataContract]
    internal sealed class BridgeConfig
    {
        [DataMember(Name = "sdkDirectory", Order = 1)]
        public string SdkDirectory { get; set; }

        [DataMember(Name = "diagnosticsEnabled", Order = 2)]
        public bool DiagnosticsEnabled { get; set; }

        // ---- Real read-only extension fields (all optional) ----

        [DataMember(Name = "realReadOnlyEnabled", EmitDefaultValue = false, Order = 3)]
        public bool RealReadOnlyEnabled { get; set; }

        [DataMember(Name = "usb2can", EmitDefaultValue = false, Order = 4)]
        public Usb2CanConfig Usb2Can { get; set; }

        [DataMember(Name = "whitelistNodes", EmitDefaultValue = false, Order = 5)]
        public List<int> WhitelistNodes { get; set; }

        [DataMember(Name = "axisMappings", EmitDefaultValue = false, Order = 6)]
        public AxisMappings AxisMappings { get; set; }

        [DataMember(Name = "axisCalibration", EmitDefaultValue = false, Order = 7)]
        public AxisCalibration AxisCalibration { get; set; }
    }

    [DataContract]
    internal sealed class Usb2CanConfig
    {
        [DataMember(Name = "connectionType", EmitDefaultValue = false, Order = 1)]
        public string ConnectionType { get; set; }

        [DataMember(Name = "portNumber", EmitDefaultValue = false, Order = 2)]
        public int PortNumber { get; set; }

        [DataMember(Name = "baudRate", EmitDefaultValue = false, Order = 3)]
        public int BaudRate { get; set; }
    }

    [DataContract]
    internal sealed class AxisMapping
    {
        [DataMember(Name = "nodeId", EmitDefaultValue = false, Order = 1)]
        public int NodeId { get; set; }

        [DataMember(Name = "axis", EmitDefaultValue = false, Order = 2)]
        public string Axis { get; set; }
    }

    [DataContract]
    internal sealed class AxisMappings
    {
        [DataMember(Name = "x", EmitDefaultValue = false, Order = 1)]
        public AxisMapping X { get; set; }

        [DataMember(Name = "y", EmitDefaultValue = false, Order = 2)]
        public AxisMapping Y { get; set; }

        [DataMember(Name = "z1", EmitDefaultValue = false, Order = 3)]
        public AxisMapping Z1 { get; set; }

        [DataMember(Name = "z2", EmitDefaultValue = false, Order = 4)]
        public AxisMapping Z2 { get; set; }
    }

    [DataContract]
    internal sealed class AxisCalibration
    {
        [DataMember(Name = "x", EmitDefaultValue = false, Order = 1)]
        public bool X { get; set; }

        [DataMember(Name = "y", EmitDefaultValue = false, Order = 2)]
        public bool Y { get; set; }

        [DataMember(Name = "z1", EmitDefaultValue = false, Order = 3)]
        public bool Z1 { get; set; }

        [DataMember(Name = "z2", EmitDefaultValue = false, Order = 4)]
        public bool Z2 { get; set; }
    }
}
