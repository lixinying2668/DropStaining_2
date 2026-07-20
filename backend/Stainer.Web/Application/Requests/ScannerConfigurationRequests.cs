using System.Text.Json;

namespace Stainer.Web.Application.Requests;

public sealed record ScannerDeviceParametersRequest(
    int? RoiX = null,
    int? RoiY = null,
    int? RoiWidth = null,
    int? RoiHeight = null,
    bool? CheckLightEnabled = null,
    JsonElement? SpecialParameters = null);

public sealed record SaveScannerProfileRequest(
    string CommandId,
    string Name,
    string ScannerType,
    bool Enabled,
    string? Port,
    int? BaudRate,
    int? TimeoutMilliseconds,
    string TriggerMode,
    ScannerDeviceParametersRequest? DeviceParameters,
    string Reason);

// 扫描触发控制：single=单次扫描 RDCMXEV1,P11,P20；continuous=连续扫描 RDCMXEV1,P11,P21；stop=停止 RDCMXEV1,P10
public sealed record ScannerTriggerRequest(
    string CommandId,
    string Mode,
    string Reason);

// 读取条码：触发一次单次扫描(RDCMXEV1,P11,P20)，返回 DCR55 回传的条码文本（在 ScannerControlResponse.Steps[0].ResponseText）
public sealed record ScannerBarcodeRequest(
    string CommandId,
    string Reason);

public sealed record SaveScannerRegionRequest(
    string CommandId,
    int RegionNo,
    string Name,
    string RegionType,
    string ScannerProfileId,
    int ScanOrder,
    JsonElement? ScanPath,
    string? CoordinateProfileId,
    string? CoordinateProfileVersionId,
    IReadOnlyList<string>? CoordinatePointCodes,
    string Reason);

public sealed record ScannerRestartRequest(
    string CommandId,
    string Reason);

public sealed record ScannerCalibrationLightRequest(
    string CommandId,
    string Reason);

public sealed record ScannerRoiRequest(
    string CommandId,
    int Left,
    int Top,
    int Width,
    int Height,
    string Reason);

public sealed record SaveReagentCoordinateAnchorRequest(
    string CommandId,
    string CoordinateProfileId,
    string? CoordinateProfileVersionId,
    int ColumnNo,
    string? ColumnCode,
    int SlotCount,
    double? StartXUm,
    double? StartYUm,
    double? StartZUm,
    double? EndXUm,
    double? EndYUm,
    double? EndZUm,
    bool IsEnabled,
    string Reason);

public sealed record GenerateReagentCoordinatesRequest(
    string CommandId,
    string AnchorId,
    string? CoordinateProfileVersionId,
    string? PointCodePrefix,
    string Reason);
