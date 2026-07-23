using System.Text.Json.Serialization;
using Stainer.Web.Application.Devices.SoconBridge;

namespace Stainer.Web.Infrastructure.Devices.SoconBridge;

// Stainer.SoconBridge 命名管道线路协议的 System.Text.Json DTO。
//
// 设计约束（与 BridgeHost.Run / BridgeContracts 对齐）：
// - 线路键名严格对应服务端 DataMember Name（requestId / command / success / bridgeStatus /
//   message / details / warnings 及 details.* 子字段），使用 [JsonPropertyName] 逐一钉死，
//   不依赖任何命名策略；默认 JsonSerializerOptions 即可正确收发。
// - 请求体只发送 { requestId, command } 两个字段，绝不发送 axis/com/baud/sdkDirectory
//   等硬件配置（COM/波特率/sdkDirectory 由 Bridge 本机配置负责，客户端不复制、不发送）。
// - 这些类型仅供本 Infrastructure 实现内部使用，Application 层只面向 ISoconBridgeClient
//   与 SoconBridgeResponseResult / SoconBridgeResponseDetails。
internal sealed class SoconBridgeWireRequest
{
    [JsonPropertyName("requestId")]
    [JsonPropertyOrder(1)]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    [JsonPropertyOrder(2)]
    public string Command { get; set; } = string.Empty;
}

// 服务端 -> 客户端响应镜像。所有字段除 success 外均可选（服务端按命令按需填充）；
// 反序列化缺失即 null/default，由上层 Completed/ForProtocolError 统一解释。
internal sealed class SoconBridgeWireResponse
{
    [JsonPropertyName("requestId")]
    [JsonPropertyOrder(1)]
    public string? RequestId { get; set; }

    [JsonPropertyName("command")]
    [JsonPropertyOrder(2)]
    public string? Command { get; set; }

    [JsonPropertyName("success")]
    [JsonPropertyOrder(3)]
    public bool Success { get; set; }

    [JsonPropertyName("bridgeStatus")]
    [JsonPropertyOrder(4)]
    public string? BridgeStatus { get; set; }

    [JsonPropertyName("message")]
    [JsonPropertyOrder(5)]
    public string? Message { get; set; }

    [JsonPropertyName("details")]
    [JsonPropertyOrder(6)]
    public SoconBridgeWireDetails? Details { get; set; }

    [JsonPropertyName("warnings")]
    [JsonPropertyOrder(7)]
    public List<string>? Warnings { get; set; }
}

// BridgeResponseDetails 的线路投影。字段全部可选；Bridge 在响应前已清除绝对路径/COM/NodeID/
// 凭据，故此处只保留其安全子集，与公开的 SoconBridgeResponseDetails 一一对应。
internal sealed class SoconBridgeWireDetails
{
    [JsonPropertyName("isX86Process")]
    public bool? IsX86Process { get; set; }

    [JsonPropertyName("sdkPathConfigured")]
    public bool? SdkPathConfigured { get; set; }

    [JsonPropertyName("sdkDirectoryExists")]
    public bool? SdkDirectoryExists { get; set; }

    [JsonPropertyName("coreFilesPresent")]
    public bool? CoreFilesPresent { get; set; }

    [JsonPropertyName("runtimeDependenciesPresent")]
    public bool? RuntimeDependenciesPresent { get; set; }

    [JsonPropertyName("canBootloaderIsX86")]
    public bool? CanBootloaderIsX86 { get; set; }

    [JsonPropertyName("canBootloaderMachine")]
    public string? CanBootloaderMachine { get; set; }

    [JsonPropertyName("missingFiles")]
    public List<string>? MissingFiles { get; set; }

    [JsonPropertyName("warningCodes")]
    public List<string>? WarningCodes { get; set; }

    [JsonPropertyName("sdkVersionStatus")]
    public string? SdkVersionStatus { get; set; }

    [JsonPropertyName("sessionState")]
    public string? SessionState { get; set; }

    [JsonPropertyName("sessionOpen")]
    public bool? SessionOpen { get; set; }

    [JsonPropertyName("cacheValid")]
    public bool? CacheValid { get; set; }

    [JsonPropertyName("initialized")]
    public string? Initialized { get; set; }

    [JsonPropertyName("homed")]
    public string? Homed { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("blockReason")]
    public string? BlockReason { get; set; }
}

internal static class SoconBridgeWireMapper
{
    // 将线路层 details 投影为公开的 SoconBridgeResponseDetails；null 输入返回 null
    // （Completed 调用方据此把 details 视为“服务端未携带”）。
    // 缺失的列表字段统一规范化为空数组，避免向 Application 层泄漏 null。
    public static SoconBridgeResponseDetails? ToPublic(SoconBridgeWireDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        return new SoconBridgeResponseDetails
        {
            IsX86Process = details.IsX86Process,
            SdkPathConfigured = details.SdkPathConfigured,
            SdkDirectoryExists = details.SdkDirectoryExists,
            CoreFilesPresent = details.CoreFilesPresent,
            RuntimeDependenciesPresent = details.RuntimeDependenciesPresent,
            CanBootloaderIsX86 = details.CanBootloaderIsX86,
            CanBootloaderMachine = details.CanBootloaderMachine,
            MissingFiles = details.MissingFiles ?? [],
            WarningCodes = details.WarningCodes ?? [],
            SdkVersionStatus = details.SdkVersionStatus,
            SessionState = details.SessionState,
            SessionOpen = details.SessionOpen,
            CacheValid = details.CacheValid,
            Initialized = details.Initialized,
            Homed = details.Homed,
            Position = details.Position,
            BlockReason = details.BlockReason
        };
    }
}
