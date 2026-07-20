# SOCON Bridge IPC Protocol

## Transport

- Windows Named Pipe only.
- Pipe name: `Stainer.SoconBridge`.
- Direction: `PipeDirection.InOut`.
- Single server instance. Each connection handles one request and one response, then closes.
- No TCP, HTTP, WebSocket or remote endpoint is configured in this phase.

## Pipe ACL

The server creates a protected DACL with exactly these allowed principals:

- Current Windows user SID.
- LocalSystem（通过 `WellKnownSidType.LocalSystemSid` 获取）.

The ACL is a local process boundary for this phase. It is not a replacement for future production SMB, firewall or service-account security configuration. If the Bridge later runs as a Windows service, deployment must explicitly add that service account SID and must not broaden the default ACL.

## Framing

Each message is:

```text
4-byte little-endian signed length + UTF-8 JSON payload
```

Request payload limit: 64 KB.

The server rejects the connection without processing when the request length is zero, negative, greater than 64 KB, not valid UTF-8, or not valid JSON.

## Request

```json
{
  "requestId": "string",
  "command": "string",
  "axis": "x|y|z1|z2 (only for GetConfiguredAxisPositions)"
}
```

`axis` is a role name, not a NodeID. The bridge resolves it against its local
allowlist; requests cannot provide a port, baud rate, SDK path, or NodeID.

## Response

```json
{
  "requestId": "string",
  "command": "string",
  "success": true,
  "bridgeStatus": "string",
  "message": "string",
  "details": {},
  "warnings": []
}
```

`details` may include only boolean check results, missing file names, the raw PE Machine value, and warning codes. It must not include the SDK root directory or any absolute path.

## Supported Commands

- `Ping`: returns current Bridge status. It does not trigger SDK checks.
- `GetBridgeStatus`: returns current Bridge status. It does not trigger SDK checks.
- `ValidateSdkDeployment`: performs deployment file and PE header checks.
- `OpenConfiguredReadOnlySession`: opens the locally configured USB2CAN session
  only after the real-read-only dual gate and deployment checks succeed.
- `GetConfiguredNodeBasicStatus`: reads initialized and homed state for the
  configured representative node. It does not initialize, home, or wait for an
  action.
- `GetConfiguredAxisPositions`: reads one configured axis role (`x`, `y`,
  `z1`, or `z2`). It does not move an axis or wait for an action.
- `CloseConfiguredReadOnlySession`: closes the read-only session and invalidates
  all cached read state. The `ClosePort()` return value is authoritative. If
  `ClosePort` returns false, the method is missing, or it throws, the command
  returns `success=false` and transitions to `SessionBlocked` (blockReason
  `CloseFailed`). It never reports a session closed that the device did not
  confirm. Only a confirmed `ClosePort` success returns `sessionState=Closed`,
  `sessionOpen=false`, `cacheValid=false` (message `SessionClosed`). No
  port/path/NodeID or underlying exception detail is leaked.

Unknown commands return:

```json
{
  "success": false,
  "bridgeStatus": "current status",
  "message": "NotSupported"
}
```

The three session read commands are intentionally narrow. They never accept
connection details from IPC and never expose them in a response. No motion,
pump, liquid detection, initialization, homing, generic port-opening, or wait
command is supported.

## Real Read-only Gate

The configured read-only session is disabled by default. `OpenConfiguredReadOnlySession`
enforces three gates in order, fail-closed. If any gate fails, no adapter is
constructed and no COM port is opened.

### Gate 1 — Dual-enable

Both must be true:

- Bridge launched with `--enable-real-read-only`.
- Local `SoconBridge.config.local.json` has `realReadOnlyEnabled: true`.

Failure: status `RealReadOnlyNotEnabled`. No adapter, no COM.

The local configuration file is deployment-specific and must not be committed.

### Gate 2 — Fresh deployment validation

`OpenConfiguredReadOnlySession` calls `ValidateSdkDeployment` internally. It does
NOT rely on a prior `ValidateSdkDeployment` (BRG-01) result. The fresh validation
must return status `DeploymentValidated` (`Success==true`) **and** must NOT carry
`SdkRuntimeDependenciesWarning`. The runtime dependencies are `SOCON.ScEventBus.dll`
and `C1.C1Zip.4.dll`.

Note: `ValidateSdkDeployment` keeps its own diagnostic semantics — when those DLLs
are missing it still returns `DeploymentValidated` **with**
`SdkRuntimeDependenciesWarning`. However, `OpenConfiguredReadOnlySession` treats that
warning as a hard block (blockReason `DeploymentNotValidated`). This prevents SDK
files changing after a prior validation.

### Gate 3 — Connection-parameter fail-closed validation

Before constructing the adapter, the following are checked:

- `connectionType == CONN_USB`.
- `portNumber > 0`.
- `baudRate > 0`.
- Whitelist non-empty.
- A representative node exists and is whitelisted.
- Every configured axis mapping resolves to a physical axis (X/Y/Z) with its
  NodeID in the whitelist.

Failure codes (none carry a path, COM, or NodeID): `OpenConnectionTypeNotUsb`,
`OpenPortNumberInvalid`, `OpenBaudRateInvalid`, `OpenWhitelistEmpty`,
`OpenNoRepresentativeNode`, `OpenAxisNotWhitelisted`, `OpenAxisMappingInvalid`.

## Status Values

- `Starting`
- `Offline`
- `SdkPathMissing`
- `SdkFilesMissing`
- `ArchitectureInvalid`
- `DeploymentValidated`

Bridge startup default status is `Offline`.

`DeploymentValidated` only means:

- The Bridge process is x86.
- The SDK path is configured and the directory exists.
- Core files are present.
- `can_bootloader.dll` has raw PE Machine `0x014C`.

It does not mean a device is connected, the SDK is loaded, NodeID is configured, safety IO is confirmed, coordinates are calibrated or any real action is allowed.

## Warning Codes

- `SdkRuntimeDependenciesWarning`: core checks passed, but `SOCON.ScEventBus.dll` or `C1.C1Zip.4.dll` is missing. `ValidateSdkDeployment` still returns `DeploymentValidated` when this warning is present, but `OpenConfiguredReadOnlySession` treats it as a hard block (Gate 2, above).
