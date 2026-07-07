# Stainer SOCON Bridge

本目录是 SOCON 独立 x86 Bridge 的最小骨架。当前阶段只建立离线进程、本地 IPC、SDK 文件部署前检查与自检，不连接设备，不扫描硬件，不加载 SOCON SDK。

## 项目边界

- Console Application，目标框架为 .NET Framework 4.5.2。
- 项目文件固定 `PlatformTarget` 为 `x86`。
- 不加入主 `Stainer.sln`。
- 不使用 NuGet，不引入第三方 JSON 库。
- 不引用、复制或加载任何厂商 SDK DLL、Demo、驱动或授权文件。
- 不创建 SOCON Real Adapter，也不提供任何真实动作接口。

## 本机配置

示例配置文件是 `SoconBridge.config.example.json`，仅包含：

```json
{
  "sdkDirectory": "",
  "diagnosticsEnabled": true
}
```

本机配置文件名为 `SoconBridge.config.local.json`，该文件只用于本机部署并被根 `.gitignore` 精确忽略。

SDK 路径解析优先级：

1. `SoconBridge.config.local.json` 的 `sdkDirectory`
2. 环境变量 `STAINER_SOCON_SDK_DIR`
3. 均不存在或为空时视为未配置

Bridge 不会把实际 SDK 绝对路径写入 Git 文件、README、协议文档、标准输出、日志或 IPC `details`。

## IPC

Bridge 仅创建 Windows Named Pipe，名称固定为 `Stainer.SoconBridge`。不配置 TCP、HTTP、WebSocket 或其他网络监听器。

Pipe 使用 `PipeDirection.InOut`，每次连接只处理一个请求，返回一个响应后关闭连接。消息格式为 4 字节小端长度前缀加 UTF-8 JSON，请求体最大 64 KB。

PipeSecurity 使用受保护 DACL，仅授予当前 Windows 用户 SID 和 LocalSystem SID。该 ACL 只作为本机进程边界，不替代未来生产环境需要的 SMB、防火墙或服务账户安全配置。若未来 Bridge 改为 Windows 服务运行，必须通过部署配置显式加入服务账户 SID，不得默认放宽 ACL。

## 命令

当前仅支持：

- `Ping`
- `GetBridgeStatus`
- `ValidateSdkDeployment`

任何其他命令返回 `success=false`、`message=NotSupported`，不会触发 SDK 校验或任何副作用。

## 部署前检查

`ValidateSdkDeployment` 只执行文件系统检查和原始 PE 头检查：

- 当前 Bridge 进程是否为 x86。
- SDK 目录是否已配置且存在。
- `SOCON.API.dll` 是否存在。
- `SOCON.Utility.dll` 是否存在。
- `can_bootloader.dll` 是否存在。
- `can_bootloader.dll` 的 COFF Machine 是否为 `0x014C`。
- `SOCON.ScEventBus.dll` 是否存在。
- `C1.C1Zip.4.dll` 是否存在。

缺少运行期依赖 `SOCON.ScEventBus.dll` 或 `C1.C1Zip.4.dll` 时，主状态仍可为 `DeploymentValidated`，并在 `warnings` 中返回 `SdkRuntimeDependenciesWarning`。

## 自检

构建后运行：

```powershell
Stainer.SoconBridge.exe --self-test
```

自检只使用临时目录、假文件、假配置和可注入的架构检测器。自检结束后会清理临时目录。
