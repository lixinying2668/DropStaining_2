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
  "command": "string"
}
```

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

Unknown commands return:

```json
{
  "success": false,
  "bridgeStatus": "current status",
  "message": "NotSupported"
}
```

No device action command, reserved action DTO, adapter placeholder, port opening command, motion command, pump command, liquid detection command or wait command is defined in this phase.

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

- `SdkRuntimeDependenciesWarning`: core checks passed, but `SOCON.ScEventBus.dll` or `C1.C1Zip.4.dll` is missing.
