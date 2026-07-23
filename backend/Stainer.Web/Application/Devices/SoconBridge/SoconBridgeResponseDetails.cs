namespace Stainer.Web.Application.Devices.SoconBridge;

// BridgeResponseDetails 的非敏感投影。Bridge 在响应前已清除绝对路径、COM、NodeID 与凭据；
// 本模型只暴露其安全子集。字段全部可选：Bridge 仅在相关命令中按需填充，缺失即 null。
public sealed class SoconBridgeResponseDetails
{
    // 部署校验相关布尔结果（GetBridgeStatus / ValidateSdkDeployment 可能携带）。
    public bool? IsX86Process { get; set; }
    public bool? SdkPathConfigured { get; set; }
    public bool? SdkDirectoryExists { get; set; }
    public bool? CoreFilesPresent { get; set; }
    public bool? RuntimeDependenciesPresent { get; set; }
    public bool? CanBootloaderIsX86 { get; set; }
    public string? CanBootloaderMachine { get; set; }
    public IReadOnlyList<string> MissingFiles { get; set; } = [];
    public IReadOnlyList<string> WarningCodes { get; set; } = [];

    // 会话与只读状态相关字段（Open/Close/GetConfiguredNodeBasicStatus/GetConfiguredAxisPositions）。
    public string? SdkVersionStatus { get; set; }
    public string? SessionState { get; set; }
    public bool? SessionOpen { get; set; }
    public bool? CacheValid { get; set; }
    public string? Initialized { get; set; }
    public string? Homed { get; set; }
    public string? Position { get; set; }

    // BLOCKED 时的具体原因码（RealReadOnlyNotEnabled / DeploymentNotValidated / OpenFailed /
    // CloseFailed / SessionBlocked 等），从不携带路径/COM/NodeID。
    public string? BlockReason { get; set; }
}
