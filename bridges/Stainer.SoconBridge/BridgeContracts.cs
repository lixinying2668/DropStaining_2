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
    }

    [DataContract]
    internal sealed class BridgeConfig
    {
        [DataMember(Name = "sdkDirectory", Order = 1)]
        public string SdkDirectory { get; set; }

        [DataMember(Name = "diagnosticsEnabled", Order = 2)]
        public bool DiagnosticsEnabled { get; set; }
    }
}
