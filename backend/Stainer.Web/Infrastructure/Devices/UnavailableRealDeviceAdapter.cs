using Stainer.Web.Application.Devices;
using Stainer.Web.Application.Services;

namespace Stainer.Web.Infrastructure.Devices;

public sealed class UnavailableRealDeviceAdapter(IDeviceByteTransport? transport = null) : IDeviceAdapter, IRealDeviceReadAdapter
{
    private const string ErrorCode = "real_adapter_not_implemented";
    private const string Message = "Real control actions are disabled in the offline adapter boundary. No hardware command was sent.";
    private const string TransportNotConfiguredErrorCode = "real_transport_not_configured";

    public string Mode => DeviceModes.Real;

    public string Name => nameof(UnavailableRealDeviceAdapter);

    public Task<DeviceStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (transport is MainControllerSerialTransport)
        {
            return Task.FromResult(new DeviceStatusSnapshot(
                Mode,
                Name,
                false,
                0,
                now,
                [new DeviceModuleStatusSnapshot(
                    "main-controller",
                    DeviceConnectionStatuses.Disconnected,
                    "Idle",
                    null,
                    null,
                    null,
                    "Main-controller serial transport is not connected.",
                    now,
                    0)],
                []));
        }

        var fakeTransportEnabled = transport is { IsConfigured: true };
        var connectionStatus = fakeTransportEnabled
            ? DeviceConnectionStatuses.Offline
            : DeviceConnectionStatuses.NotConfigured;
        var statusMessage = fakeTransportEnabled
            ? $"Offline byte transport '{transport!.Name}' is enabled for isolated reads only."
            : "No physical or offline byte transport is configured.";
        return Task.FromResult(new DeviceStatusSnapshot(
            Mode,
            Name,
            false,
            0,
            now,
            [new DeviceModuleStatusSnapshot(
                "real-adapter",
                connectionStatus,
                "ReadOnlyOffline",
                null,
                null,
                fakeTransportEnabled ? null : TransportNotConfiguredErrorCode,
                statusMessage,
                now,
                0)],
            []));
    }

    public Task<RealDeviceReadResult<MainControllerWorkStatus>> ReadControllerWorkStatusAsync(CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-work-status",
            MainControllerProtocol.BuildWorkStatusRequest(),
            MainControllerProtocol.SystemClass,
            0x08,
            MainControllerProtocol.ParseWorkStatus,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerNodeStatuses>> ReadControllerNodeStatusesAsync(CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-node-statuses",
            MainControllerProtocol.BuildNodeStatusRequest(),
            MainControllerProtocol.SystemClass,
            0x09,
            MainControllerProtocol.ParseNodeStatuses,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerRunTime>> ReadControllerRunTimeAsync(CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-run-time",
            MainControllerProtocol.BuildRunTimeRequest(),
            MainControllerProtocol.SystemClass,
            0x05,
            MainControllerProtocol.ParseRunTime,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerTemperatureBoard>> ReadTemperaturesAsync(
        byte boardId,
        CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-current-temperatures",
            MainControllerProtocol.BuildBoardTemperaturesRequest(boardId),
            MainControllerProtocol.HeatingClass,
            0x09,
            frame => MainControllerProtocol.ParseBoardTemperatures(frame, target: false),
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerTemperatureBoard>> ReadTargetTemperaturesAsync(
        byte boardId,
        CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-target-temperatures",
            MainControllerProtocol.BuildBoardTargetTemperaturesRequest(boardId),
            MainControllerProtocol.HeatingClass,
            0x0A,
            frame => MainControllerProtocol.ParseBoardTemperatures(frame, target: true),
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerSwitchBoard>> ReadTemperatureSwitchesAsync(
        byte boardId,
        CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-temperature-switches",
            MainControllerProtocol.BuildBoardSwitchStatesRequest(boardId),
            MainControllerProtocol.HeatingClass,
            0x0B,
            MainControllerProtocol.ParseBoardSwitchStates,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerOptocouplerStatus>> ReceiveLiquidLevelStatusAsync(
        CancellationToken cancellationToken = default) =>
        ReceiveMainControllerPutAsync(
            "receive-liquid-level-status",
            MainControllerProtocol.OptocouplerClass,
            0x04,
            MainControllerProtocol.ParseOptocouplerPut,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerPwmSpeeds>> ReadPwmSpeedsAsync(CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-pwm-speeds",
            MainControllerProtocol.BuildPwmSpeedsRequest(),
            MainControllerProtocol.PwmClass,
            0x06,
            MainControllerProtocol.ParsePwmSpeeds,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerMixerValue>> ReadMixerOriginAsync(
        byte boardId,
        CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-mixer-origin",
            MainControllerProtocol.BuildMixerOriginRequest(boardId),
            MainControllerProtocol.MixerClass,
            0x02,
            MainControllerProtocol.ParseMixerOrigin,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerMixerValue>> ReadMixerRemainingCountAsync(
        byte boardId,
        CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-mixer-remaining-count",
            MainControllerProtocol.BuildMixerRemainingCountRequest(boardId),
            MainControllerProtocol.MixerClass,
            0x03,
            MainControllerProtocol.ParseMixerRemainingCount,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerQrScanStatus>> ReadQrScanStatusAsync(CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-qr-scan-status",
            MainControllerProtocol.BuildQrScanStatusRequest(),
            MainControllerProtocol.QrClass,
            0x06,
            MainControllerProtocol.ParseQrScanStatus,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerQrText>> ReadQrTextAsync(CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-qr-text",
            MainControllerProtocol.BuildQrTextRequest(),
            MainControllerProtocol.QrClass,
            0x01,
            MainControllerProtocol.ParseQrText,
            cancellationToken,
            frame => frame.ParentClass == MainControllerProtocol.QrClass
                && frame.SubClass == 0x03
                && frame.MessageType == IceImmunoSerialProtocol.RequestType);

    public Dcr55TriggerPreparation PrepareDcr55Trigger(Dcr55TriggerMode mode, byte[]? configuredTerminator)
    {
        if (configuredTerminator is null || configuredTerminator.Length == 0)
        {
            return new Dcr55TriggerPreparation(
                false,
                DeviceCommandStatuses.NotConfigured,
                "dcr55_terminator_not_configured",
                "DCR55 command terminator must be configured explicitly.",
                [],
                false);
        }

        return new Dcr55TriggerPreparation(
            true,
            DeviceCommandStatuses.Succeeded,
            null,
            "DCR55 trigger bytes were prepared but not sent.",
            Dcr55Protocol.EncodeCommand(mode, configuredTerminator),
            false);
    }

    public async Task<RealDeviceReadResult<Dcr55ScanResult>> ReceiveDcr55ResultAsync(CancellationToken cancellationToken = default)
    {
        var unavailable = TransportUnavailable<Dcr55ScanResult>([]);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var exchange = await transport!.ReceiveAsync(DeviceByteTransportEndpoints.Dcr55, cancellationToken);
        var responseBytes = Flatten(exchange.ResponseChunks);
        if (string.Equals(exchange.Status, DeviceByteTransportStatuses.TimedOut, StringComparison.OrdinalIgnoreCase))
        {
            return Failure<Dcr55ScanResult>(
                DeviceCommandStatuses.TimedOut,
                exchange.ErrorCode ?? "dcr55_no_barcode_timeout",
                exchange.Message ?? "DCR55 returned no barcode before the offline transport timed out.",
                [],
                responseBytes,
                Dcr55Protocol.FromTransportStatus(
                    Dcr55ScanStatus.Timeout,
                    System.Text.Encoding.ASCII.GetString(responseBytes)));
        }

        var transportFailure = TransportFailure<Dcr55ScanResult>(exchange, [], responseBytes);
        if (transportFailure is not null)
        {
            return transportFailure;
        }

        var value = Dcr55Protocol.ParseBarcodeResult(System.Text.Encoding.ASCII.GetString(responseBytes));
        return value.Status == Dcr55ScanStatus.Success
            ? Success(value, [], responseBytes)
            : Failure(
                DeviceCommandStatuses.Unknown,
                "dcr55_incomplete_result",
                "DCR55 response did not contain a complete CRLF-terminated barcode.",
                [],
                responseBytes,
                value);
    }

    // 制冷读取统一走主控 0x03（父类 CoolingClass）。不再使用 StandaloneCooling 通道。
    public Task<RealDeviceReadResult<MainControllerCoolingConnectionStatus>> ReadCoolingConnectionStatusAsync(
        CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-cooling-connection",
            MainControllerProtocol.BuildCoolingConnectionStatusRequest(),
            MainControllerProtocol.CoolingClass,
            MainControllerProtocol.CoolingConnectionStatusSub,
            MainControllerProtocol.ParseCoolingConnectionStatus,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerCoolingTemperature>> ReadCoolingCurrentTemperatureAsync(
        CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-cooling-current-temperature",
            MainControllerProtocol.BuildCoolingCurrentTemperatureRequest(),
            MainControllerProtocol.CoolingClass,
            MainControllerProtocol.CoolingCurrentTemperatureSub,
            MainControllerProtocol.ParseCoolingCurrentTemperature,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerCoolingTemperature>> ReadCoolingTargetTemperatureAsync(
        CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-cooling-target-temperature",
            MainControllerProtocol.BuildCoolingTargetTemperatureRequest(),
            MainControllerProtocol.CoolingClass,
            MainControllerProtocol.CoolingTargetTemperatureSub,
            MainControllerProtocol.ParseCoolingTargetTemperature,
            cancellationToken);

    public Task<RealDeviceReadResult<MainControllerCoolingSwitchState>> ReadCoolingSwitchStateAsync(
        CancellationToken cancellationToken = default) =>
        ExchangeMainControllerAsync(
            "read-cooling-switch-state",
            MainControllerProtocol.BuildCoolingSwitchStateRequest(),
            MainControllerProtocol.CoolingClass,
            MainControllerProtocol.CoolingSwitchStateSub,
            MainControllerProtocol.ParseCoolingSwitchState,
            cancellationToken);

    public async Task<RealDeviceReadResult<MainControllerCoolingSnapshot>> ReadCoolingSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        // 顺序读取连接/当前/目标/开关四项；任一失败即 fail closed，绝不拼凑半截快照。
        var connection = await ReadCoolingConnectionStatusAsync(cancellationToken);
        if (!connection.Ok)
        {
            return SnapshotFailure(connection);
        }

        var current = await ReadCoolingCurrentTemperatureAsync(cancellationToken);
        if (!current.Ok)
        {
            return SnapshotFailure(current);
        }

        var target = await ReadCoolingTargetTemperatureAsync(cancellationToken);
        if (!target.Ok)
        {
            return SnapshotFailure(target);
        }

        var sw = await ReadCoolingSwitchStateAsync(cancellationToken);
        if (!sw.Ok)
        {
            return SnapshotFailure(sw);
        }

        var snapshot = new MainControllerCoolingSnapshot(
            connection.Value!.IsConnected,
            current.Value!.Celsius * 10,   // 协议整摄氏度 → deci-C
            target.Value!.Celsius * 10,
            sw.Value!.IsEnabled);

        var requests = Flatten([connection.RequestBytes, current.RequestBytes, target.RequestBytes, sw.RequestBytes]);
        var responses = Flatten([connection.ResponseBytes, current.ResponseBytes, target.ResponseBytes, sw.ResponseBytes]);
        return Success(snapshot, requests, responses);
    }

    private static RealDeviceReadResult<MainControllerCoolingSnapshot> SnapshotFailure<T>(RealDeviceReadResult<T> failed) =>
        Failure<MainControllerCoolingSnapshot>(
            failed.Status,
            string.IsNullOrEmpty(failed.ErrorCode) ? "cooling_snapshot_failed" : failed.ErrorCode!,
            failed.Message,
            failed.RequestBytes,
            failed.ResponseBytes);

    public Task<DeviceCommandResult> GetHealthAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    public Task<DeviceCommandResult> InitializeModuleAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    // Real 模式样本扫码下发：复用本类已实现的 ReceiveDcr55ResultAsync（走 DCR55 独立串口 endpoint，
    // 触发 RDCMXEV1,P11,P20 + 读 CRLF 条码帧），把 Dcr55ScanResult 包装成 DeviceCommandResult。
    // 与试剂扫码（主控 0x08）不同——样本/玻片条码由机械臂末端 DCR55 扫码枪读取。
    // DCR55 不经主控白名单；transport 未配置时 ReceiveDcr55ResultAsync 自身 fail-closed 不发字节。
    public async Task<DeviceCommandResult> ScanSampleAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var scan = await ReceiveDcr55ResultAsync(cancellationToken);
        var value = scan.Value;
        var data = new Dictionary<string, object?>
        {
            ["scanSource"] = "Dcr55",
            ["barcode"] = value?.Barcode,
            ["rawText"] = value?.RawText,
            ["scanStatus"] = value?.Status.ToString(),
            ["responseHex"] = Convert.ToHexString(scan.ResponseBytes ?? [])
        };

        return new DeviceCommandResult(
            scan.Ok,
            scan.Status,
            request.ModuleCode,
            request.Action,
            scan.ErrorCode,
            scan.Ok ? "Sample barcode was read from the DCR55 scanner." : scan.Message,
            startedAt,
            DateTimeOffset.UtcNow,
            scan.Ok,
            data);
    }
    // Real 模式试剂扫码下发：仅试剂二维码的启动/复位写真发字节到主控 0x08（TL_QR_START_SCAN 0x08/0x04、
    // TL_QR_RESET_SCAN 0x08/0x05），照 SetCoolingAsync 范本（构造帧 → ExchangeAsync → 解析 ACK → 脱敏摘要）。
    // 其余试剂 action（含试剂状态通知 reagent.stateChanged 等语义化动作）仍 RejectAsync，保持 fail-closed，不误触发扫码。
    // 试剂瓶条码由主控 0x08 扫码模块处理（不随机械臂、不用 DCR55）。
    public async Task<DeviceCommandResult> ScanReagentAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        var (requestBytes, expectedSub, operation) = request.Action switch
        {
            ReagentQrCommands.StartScan => (MainControllerProtocol.BuildQrStartScanRequest(), MainControllerProtocol.QrStartScanSub, "reagent-qr-start-scan"),
            ReagentQrCommands.ResetScan => (MainControllerProtocol.BuildQrResetScanRequest(), MainControllerProtocol.QrResetScanSub, "reagent-qr-reset-scan"),
            _ => (Array.Empty<byte>(), (byte)0, string.Empty)
        };

        if (operation.Length == 0)
        {
            // 非试剂扫码的 action（试剂状态通知等）仍 fail-closed，不发字节。
            return await RejectAsync(request);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var steps = new List<CoolingStep>();

        var exchange = await ExchangeMainControllerAsync(
            operation,
            requestBytes,
            MainControllerProtocol.QrClass,
            expectedSub,
            frame =>
            {
                if (expectedSub == MainControllerProtocol.QrStartScanSub)
                {
                    MainControllerProtocol.ParseQrStartScanAck(frame);
                }
                else
                {
                    MainControllerProtocol.ParseQrResetScanAck(frame);
                }
                return true;
            },
            cancellationToken);
        steps.Add(Step(operation, exchange));

        if (!exchange.Ok)
        {
            return CoolingFail(request, startedAt, exchange.ErrorCode ?? "reagent_qr_write_failed", exchange.Message, steps);
        }

        return new DeviceCommandResult(
            true,
            DeviceCommandStatuses.Succeeded,
            request.ModuleCode,
            request.Action,
            null,
            "Reagent QR scan command was accepted by the main controller.",
            startedAt,
            DateTimeOffset.UtcNow,
            true,
            new Dictionary<string, object?>
            {
                ["communication"] = BuildCoolingCommunication(steps)
            });
    }
    public Task<DeviceCommandResult> QueryLisAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    public Task<DeviceCommandResult> SetTemperatureAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);

    // Real 模式制冷写入：通过主控 0x03 下发。先写目标温度（0x03/0x04），再写开关状态（0x03/0x06），
    // 然后回读快照确认。任一步失败即 fail closed，绝不回退 Mock、绝不假成功。
    // 协议温度为整摄氏度 UINT16（0..40），所以 deci-C 必须能被 10 整除且落在 0..400。
    public async Task<DeviceCommandResult> SetCoolingAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var steps = new List<CoolingStep>();

        if (!TryConvertToInt32(request.Parameters.GetValueOrDefault("targetTemperatureDeciC"), out var targetDeciC))
        {
            return CoolingFail(request, startedAt, "cooling_target_missing",
                "Cooling target temperature (targetTemperatureDeciC) is required as an integer deci-Celsius.", steps);
        }

        if (!TryConvertToBoolean(request.Parameters.GetValueOrDefault("isEnabled"), out var isEnabled))
        {
            return CoolingFail(request, startedAt, "cooling_switch_missing",
                "Cooling switch flag (isEnabled) is required.", steps);
        }

        if (targetDeciC < 0 || targetDeciC > 400 || targetDeciC % 10 != 0)
        {
            // 真实协议不支持该温度：未发送任何字节即拒绝。
            return CoolingFail(request, startedAt, "cooling_target_out_of_range",
                $"Cooling target {targetDeciC} deci-C is outside the main-controller protocol range (0..40 °C in whole degrees → deci-C 0..400, multiple of 10).", steps);
        }

        var targetCelsius = (ushort)(targetDeciC / 10);

        var setTarget = await SendCoolingWriteAsync(
            "set-cooling-target-temperature",
            MainControllerProtocol.BuildSetCoolingTargetTemperatureRequest(targetCelsius),
            MainControllerProtocol.CoolingSetTargetTemperatureSub,
            cancellationToken);
        steps.Add(Step("set-target-temperature", setTarget));
        if (!setTarget.Ok)
        {
            return CoolingFail(request, startedAt, setTarget.ErrorCode ?? "cooling_set_target_failed", setTarget.Message, steps);
        }

        var setSwitch = await SendCoolingWriteAsync(
            "set-cooling-switch-status",
            MainControllerProtocol.BuildSetCoolingSwitchStateRequest(isEnabled),
            MainControllerProtocol.CoolingSetSwitchStateSub,
            cancellationToken);
        steps.Add(Step("set-switch-status", setSwitch));
        if (!setSwitch.Ok)
        {
            return CoolingFail(request, startedAt, setSwitch.ErrorCode ?? "cooling_set_switch_failed", setSwitch.Message, steps);
        }

        var snapshot = await ReadCoolingSnapshotAsync(cancellationToken);
        steps.Add(Step("read-back-snapshot", snapshot));
        if (!snapshot.Ok)
        {
            return CoolingFail(request, startedAt, snapshot.ErrorCode ?? "cooling_readback_failed", snapshot.Message, steps);
        }

        return new DeviceCommandResult(
            true,
            DeviceCommandStatuses.Succeeded,
            request.ModuleCode,
            request.Action,
            null,
            "Cooling target and switch were applied and confirmed by the main controller.",
            startedAt,
            DateTimeOffset.UtcNow,
            true,
            new Dictionary<string, object?>
            {
                ["currentTemperatureDeciC"] = snapshot.Value!.CurrentTemperatureDeciC,
                ["targetTemperatureDeciC"] = snapshot.Value.TargetTemperatureDeciC,
                ["isEnabled"] = snapshot.Value.IsEnabled,
                ["isConnected"] = snapshot.Value.IsConnected,
                ["commandedTargetDeciC"] = targetDeciC,
                ["commandedEnabled"] = isEnabled,
                ["communication"] = BuildCoolingCommunication(steps)
            });
    }

    // 制冷写命令复用主控读交换：parse 委托给 ParseCoolingAck，ack 非法时它会抛 IceImmunoProtocolException，
    // ParseMainControllerExchange 会把异常转成结构化 Failure。返回 bool 仅占位。
    private Task<RealDeviceReadResult<bool>> SendCoolingWriteAsync(
        string operation,
        byte[] requestBytes,
        byte expectedSubClass,
        CancellationToken cancellationToken) =>
        ExchangeMainControllerAsync(
            operation,
            requestBytes,
            MainControllerProtocol.CoolingClass,
            expectedSubClass,
            frame =>
            {
                MainControllerProtocol.ParseCoolingAck(frame, expectedSubClass);
                return true;
            },
            cancellationToken);

    private static DeviceCommandResult CoolingFail(
        DeviceOperationRequest request,
        DateTimeOffset startedAt,
        string errorCode,
        string message,
        List<CoolingStep> steps)
    {
        var acknowledged = steps.Count > 0 && steps.Any(s => s.Ok);
        var data = new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["communication"] = BuildCoolingCommunication(steps)
        };
        return new DeviceCommandResult(
            false,
            DeviceCommandStatuses.Failed,
            request.ModuleCode,
            request.Action,
            errorCode,
            message,
            startedAt,
            DateTimeOffset.UtcNow,
            acknowledged,
            data);
    }

    // 已脱敏通信摘要：仅协议帧 hex + 状态码，不含用户名/路径/序列号等敏感信息（与现有 DevicePrecheckService 风格一致）。
    private static IReadOnlyList<object> BuildCoolingCommunication(List<CoolingStep> steps) =>
        steps.Select(step => (object)new Dictionary<string, object?>
        {
            ["name"] = step.Name,
            ["ok"] = step.Ok,
            ["status"] = step.Status,
            ["errorCode"] = step.ErrorCode,
            ["requestHex"] = Convert.ToHexString(step.RequestBytes),
            ["responseHex"] = Convert.ToHexString(step.ResponseBytes)
        }).ToList();

    private static CoolingStep Step<T>(string name, RealDeviceReadResult<T> result) =>
        new(name, result.Ok, result.Status, result.ErrorCode, result.Message, result.RequestBytes, result.ResponseBytes);

    private static bool TryConvertToInt32(object? value, out int result)
    {
        try
        {
            result = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private static bool TryConvertToBoolean(object? value, out bool result)
    {
        try
        {
            result = Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = false;
            return false;
        }
    }

    private sealed record CoolingStep(string Name, bool Ok, string Status, string? ErrorCode, string Message, byte[] RequestBytes, byte[] ResponseBytes);

    // Real 模式清洗泵 PWM 写：照 SetCoolingAsync 范本下发主控 0x07（TL_PWM_SET_ID_SET_VALUE 0x07/0x02 单通道、
    // TL_PWM_SET_ALL_SET_VALUE 0x07/0x04 全通道）。pwm0~3 对应通道0~3 清洗泵；值 -100~100（INT16 小端）。
    // 其余泵 action 仍 RejectAsync fail-closed。
    public async Task<DeviceCommandResult> RunPumpAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var steps = new List<CoolingStep>();

        byte[] requestBytes;
        byte expectedSub;
        string operation;
        if (TryBuildBusinessWashPwm(request, out var businessPwmId, out var businessValue, out var businessErrorCode, out var businessErrorMessage))
        {
            requestBytes = MainControllerProtocol.BuildSetPwmValueRequest((byte)businessPwmId, (short)businessValue);
            expectedSub = MainControllerProtocol.PwmSetIdValueSub;
            operation = "set-wash-pwm-business";
        }
        else if (businessErrorCode is not null)
        {
            return CoolingFail(request, startedAt, businessErrorCode, businessErrorMessage!, steps);
        }
        else if (string.Equals(request.Action, "set-pwm", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryConvertToInt32(request.Parameters.GetValueOrDefault("pwmId"), out var pwmId) || pwmId < 0 || pwmId > 3)
            {
                return CoolingFail(request, startedAt, "pwm_id_missing", "PWM id (pwmId 0..3) is required for set-pwm.", steps);
            }
            if (!TryConvertToInt32(request.Parameters.GetValueOrDefault("value"), out var value) || value < -100 || value > 100)
            {
                return CoolingFail(request, startedAt, "pwm_value_out_of_range", "PWM value (value -100..100) is required for set-pwm.", steps);
            }
            requestBytes = MainControllerProtocol.BuildSetPwmValueRequest((byte)pwmId, (short)value);
            expectedSub = MainControllerProtocol.PwmSetIdValueSub;
            operation = "set-wash-pwm-single";
        }
        else if (string.Equals(request.Action, "set-all-pwm", StringComparison.OrdinalIgnoreCase))
        {
            var values = new short[4];
            for (var index = 0; index < 4; index++)
            {
                if (!TryConvertToInt32(request.Parameters.GetValueOrDefault($"pwm{index}"), out var value) || value < -100 || value > 100)
                {
                    return CoolingFail(request, startedAt, "pwm_value_out_of_range", $"pwm{index} (-100..100) is required for set-all-pwm.", steps);
                }
                values[index] = (short)value;
            }
            requestBytes = MainControllerProtocol.BuildSetAllPwmValuesRequest(values);
            expectedSub = MainControllerProtocol.PwmSetAllValueSub;
            operation = "set-wash-pwm-all";
        }
        else
        {
            return await RejectAsync(request);
        }

        var exchange = await ExchangeMainControllerAsync(
            operation,
            requestBytes,
            MainControllerProtocol.PwmClass,
            expectedSub,
            frame =>
            {
                if (expectedSub == MainControllerProtocol.PwmSetIdValueSub)
                {
                    MainControllerProtocol.ParsePwmSetIdValueAck(frame);
                }
                else
                {
                    MainControllerProtocol.ParsePwmSetAllAck(frame);
                }
                return true;
            },
            cancellationToken);
        steps.Add(Step(operation, exchange));

        if (!exchange.Ok)
        {
            return CoolingFail(request, startedAt, exchange.ErrorCode ?? "wash_pwm_write_failed", exchange.Message, steps);
        }

        return new DeviceCommandResult(
            true,
            DeviceCommandStatuses.Succeeded,
            request.ModuleCode,
            request.Action,
            null,
            "Wash PWM command was accepted by the main controller.",
            startedAt,
            DateTimeOffset.UtcNow,
            true,
            new Dictionary<string, object?>
            {
                ["communication"] = BuildCoolingCommunication(steps)
            });
    }

    private static bool TryBuildBusinessWashPwm(
        DeviceOperationRequest request,
        out int pwmId,
        out int value,
        out string? errorCode,
        out string? errorMessage)
    {
        pwmId = 0;
        value = 0;
        errorCode = null;
        errorMessage = null;

        if (!request.Action.Contains("wash", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryResolvePwmId(request.Parameters, out pwmId))
        {
            errorCode = "pwm_id_missing";
            errorMessage = "PWM id, pwmChannelCode, or drawerCode is required for wash PWM control.";
            return false;
        }

        if (request.Action.Contains("stop", StringComparison.OrdinalIgnoreCase))
        {
            value = 0;
            return true;
        }

        if (!TryConvertToInt32(request.Parameters.GetValueOrDefault("value")
            ?? request.Parameters.GetValueOrDefault("speedPercent")
            ?? request.Parameters.GetValueOrDefault("pwm"), out value) || value < -100 || value > 100)
        {
            errorCode = "pwm_value_out_of_range";
            errorMessage = "PWM value (value/speedPercent -100..100) is required for wash PWM control.";
            return false;
        }

        return true;
    }

    private static bool TryResolvePwmId(IReadOnlyDictionary<string, object?> parameters, out int pwmId)
    {
        foreach (var key in new[] { "pwmId", "pwmChannelNo", "channelNo" })
        {
            if (parameters.TryGetValue(key, out var value)
                && value is not null
                && TryConvertToInt32(value, out pwmId)
                && pwmId is >= 0 and <= 3)
            {
                return true;
            }
        }

        var pwmChannelCode = Convert.ToString(parameters.GetValueOrDefault("pwmChannelCode"));
        if (!string.IsNullOrWhiteSpace(pwmChannelCode)
            && pwmChannelCode.Trim().StartsWith("PWM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(pwmChannelCode.Trim()[3..], out pwmId)
            && pwmId is >= 0 and <= 3)
        {
            return true;
        }

        var drawerCode = Convert.ToString(parameters.GetValueOrDefault("drawerCode"))?.Trim().ToUpperInvariant();
        pwmId = drawerCode switch
        {
            "A" => 0,
            "B" => 1,
            "C" => 2,
            "D" => 3,
            _ => -1
        };

        return pwmId is >= 0 and <= 3;
    }

    public Task<DeviceCommandResult> ReadLiquidLevelsAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    public Task<DeviceCommandResult> MixAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    public Task<DeviceCommandResult> MoveRobotAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    public Task<DeviceCommandResult> OperateNeedlesAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    public Task<DeviceCommandResult> PipetteAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    public Task<DeviceCommandResult> WashNeedlesAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    public Task<DeviceCommandResult> PrepareDabAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);
    public Task<DeviceCommandResult> ExecuteWorkflowActionAsync(DeviceOperationRequest request, CancellationToken cancellationToken = default) => RejectAsync(request);

    public async Task<DeviceFaultControlResult> ConfigureFaultAsync(DeviceFaultCommand command, CancellationToken cancellationToken = default)
    {
        return new DeviceFaultControlResult(false, Message, await GetStatusAsync(cancellationToken));
    }

    public async Task<DeviceFaultControlResult> ClearFaultsAsync(DeviceFaultClearCommand command, CancellationToken cancellationToken = default)
    {
        return new DeviceFaultControlResult(false, Message, await GetStatusAsync(cancellationToken));
    }

    private static Task<DeviceCommandResult> RejectAsync(DeviceOperationRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new DeviceCommandResult(
            false,
            DeviceCommandStatuses.NotSupported,
            request.ModuleCode,
            request.Action,
            ErrorCode,
            Message,
            now,
            now,
            false,
            new Dictionary<string, object?>()));
    }

    private async Task<RealDeviceReadResult<T>> ExchangeMainControllerAsync<T>(
        string operation,
        byte[] requestBytes,
        byte expectedParentClass,
        byte expectedSubClass,
        Func<IceImmunoFrame, T> parse,
        CancellationToken cancellationToken,
        Func<IceImmunoFrame, bool>? alternativeResponse = null)
    {
        var unavailable = TransportUnavailable<T>(requestBytes);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var exchange = await transport!.ExchangeAsync(
            new DeviceByteTransportRequest(
                DeviceByteTransportEndpoints.MainController,
                operation,
                requestBytes),
            cancellationToken);
        return ParseMainControllerExchange(
            exchange,
            requestBytes,
            frame => frame.ParentClass == expectedParentClass
                && frame.SubClass == expectedSubClass
                && frame.MessageType == IceImmunoSerialProtocol.ResponseType,
            parse,
            alternativeResponse);
    }

    private async Task<RealDeviceReadResult<T>> ReceiveMainControllerPutAsync<T>(
        string operation,
        byte parentClass,
        byte subClass,
        Func<IceImmunoFrame, T> parse,
        CancellationToken cancellationToken)
    {
        var unavailable = TransportUnavailable<T>([]);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var exchange = await transport!.ReceiveAsync(DeviceByteTransportEndpoints.MainController, cancellationToken);
        return ParseMainControllerExchange(
            exchange,
            [],
            frame => frame.ParentClass == parentClass
                && frame.SubClass == subClass
                && frame.MessageType == IceImmunoSerialProtocol.RequestType,
            parse);
    }

    private static RealDeviceReadResult<T> ParseMainControllerExchange<T>(
        DeviceByteTransportResult exchange,
        byte[] requestBytes,
        Func<IceImmunoFrame, bool> expectedResponse,
        Func<IceImmunoFrame, T> parse,
        Func<IceImmunoFrame, bool>? alternativeResponse = null)
    {
        var responseBytes = Flatten(exchange.ResponseChunks);
        var transportFailure = TransportFailure<T>(exchange, requestBytes, responseBytes);
        if (transportFailure is not null)
        {
            return transportFailure;
        }

        var decoder = new IceImmunoFrameStreamDecoder();
        var decoded = exchange.ResponseChunks.SelectMany(chunk => decoder.Feed(chunk)).ToList();
        var malformed = decoded.FirstOrDefault(result => !result.Ok);
        if (malformed is not null)
        {
            return Failure<T>(
                DeviceCommandStatuses.Failed,
                $"main_controller_{malformed.Error}",
                malformed.ErrorMessage ?? "Main-controller response frame is invalid.",
                requestBytes,
                responseBytes);
        }

        if (decoder.BufferedByteCount != 0)
        {
            return Failure<T>(
                DeviceCommandStatuses.Failed,
                "main_controller_truncated_frame",
                "Main-controller response ended with an incomplete frame.",
                requestBytes,
                responseBytes);
        }

        var frames = decoded.Select(result => result.Frame!).ToList();
        var acknowledgements = frames
            .Where(frame => frame.MessageType == IceImmunoSerialProtocol.ResponseType && !expectedResponse(frame))
            .Select(TryParseAck)
            .Where(ack => ack is not null)
            .Cast<MainControllerAck>()
            .ToList();
        List<MainControllerPutReport> putReports;
        try
        {
            putReports = frames
                .Where(frame => frame.MessageType == IceImmunoSerialProtocol.RequestType)
                .Select(ParsePutReport)
                .ToList();
        }
        catch (IceImmunoProtocolException exception)
        {
            return Failure<T>(
                DeviceCommandStatuses.Failed,
                $"main_controller_{exception.Error}",
                exception.Message,
                requestBytes,
                responseBytes,
                default,
                acknowledgements,
                []);
        }

        var expectedFrames = frames.Where(expectedResponse).ToList();
        if (expectedFrames.Count == 0 && alternativeResponse is not null)
        {
            expectedFrames = frames.Where(alternativeResponse).ToList();
        }

        if (expectedFrames.Count != 1)
        {
            return Failure<T>(
                DeviceCommandStatuses.Failed,
                expectedFrames.Count == 0 ? "main_controller_response_missing" : "main_controller_response_ambiguous",
                expectedFrames.Count == 0
                    ? "The expected main-controller response was not received."
                    : "Multiple matching main-controller responses were received.",
                requestBytes,
                responseBytes,
                default,
                acknowledgements,
                putReports);
        }

        try
        {
            return Success(parse(expectedFrames[0]), requestBytes, responseBytes, acknowledgements, putReports);
        }
        catch (IceImmunoProtocolException exception)
        {
            return Failure<T>(
                DeviceCommandStatuses.Failed,
                $"main_controller_{exception.Error}",
                exception.Message,
                requestBytes,
                responseBytes,
                default,
                acknowledgements,
                putReports);
        }
    }

    private RealDeviceReadResult<T>? TransportUnavailable<T>(byte[] requestBytes)
    {
        if (transport is { IsConfigured: true })
        {
            return null;
        }

        return Failure<T>(
            DeviceCommandStatuses.NotConfigured,
            TransportNotConfiguredErrorCode,
            "No physical transport is implemented and no offline Fake Transport is enabled.",
            requestBytes,
            []);
    }

    private static RealDeviceReadResult<T>? TransportFailure<T>(
        DeviceByteTransportResult exchange,
        byte[] requestBytes,
        byte[] responseBytes)
    {
        if (string.Equals(exchange.Status, DeviceByteTransportStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var status = string.Equals(exchange.Status, DeviceByteTransportStatuses.TimedOut, StringComparison.OrdinalIgnoreCase)
            ? DeviceCommandStatuses.TimedOut
            : string.Equals(exchange.Status, DeviceByteTransportStatuses.Disconnected, StringComparison.OrdinalIgnoreCase)
                ? DeviceCommandStatuses.Offline
                : DeviceCommandStatuses.Failed;
        return Failure<T>(
            status,
            exchange.ErrorCode ?? "device_transport_failed",
            exchange.Message ?? $"Byte transport ended with status {exchange.Status}.",
            requestBytes,
            responseBytes);
    }

    private static MainControllerAck? TryParseAck(IceImmunoFrame frame)
    {
        try
        {
            return MainControllerProtocol.ParseAck(frame);
        }
        catch (IceImmunoProtocolException)
        {
            return null;
        }
    }

    private static MainControllerPutReport ParsePutReport(IceImmunoFrame frame)
    {
        object value = (frame.ParentClass, frame.SubClass) switch
        {
            (MainControllerProtocol.OptocouplerClass, 0x04) => MainControllerProtocol.ParseOptocouplerPut(frame),
            (MainControllerProtocol.QrClass, 0x03) => MainControllerProtocol.ParseQrText(frame),
            _ => frame.Payload.ToArray()
        };
        return new MainControllerPutReport(frame.ParentClass, frame.SubClass, value);
    }

    private static byte[] Flatten(IReadOnlyList<byte[]> chunks) => chunks.SelectMany(chunk => chunk).ToArray();

    private static RealDeviceReadResult<T> Success<T>(
        T value,
        byte[] requestBytes,
        byte[] responseBytes,
        IReadOnlyList<MainControllerAck>? acknowledgements = null,
        IReadOnlyList<MainControllerPutReport>? putReports = null) =>
        new(
            true,
            DeviceCommandStatuses.Succeeded,
            null,
            "Offline device read completed.",
            value,
            requestBytes,
            responseBytes,
            acknowledgements ?? [],
            putReports ?? []);

    private static RealDeviceReadResult<T> Failure<T>(
        string status,
        string errorCode,
        string message,
        byte[] requestBytes,
        byte[] responseBytes,
        T? value = default,
        IReadOnlyList<MainControllerAck>? acknowledgements = null,
        IReadOnlyList<MainControllerPutReport>? putReports = null) =>
        new(
            false,
            status,
            errorCode,
            message,
            value,
            requestBytes,
            responseBytes,
            acknowledgements ?? [],
            putReports ?? []);
}
