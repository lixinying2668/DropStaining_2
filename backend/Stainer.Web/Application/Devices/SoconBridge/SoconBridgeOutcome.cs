namespace Stainer.Web.Application.Devices.SoconBridge;

// 一次完整 Bridge 交换的业务结果分类（收到了合法响应并完成解析后）。
//
// 分类规则严格对应 BridgeRequestProcessor 的响应构造约定：
//   success == true                            -> Success
//   success == false && message == "BLOCKED"   -> Blocked （服务端 CreateBlockedResponse）
//   success == false 其余                       -> Failure  （NotSupported / RealReadOnlyNotEnabled /
//                                              SessionAlreadyOpen / SessionClosed / DeploymentNotValidated 等非 BLOCKED 失败）
//
// Blocked 与业务 Failure 均不会被报告为 Success。
public enum SoconBridgeOutcome
{
    Success = 0,
    Failure = 1,
    Blocked = 2
}
