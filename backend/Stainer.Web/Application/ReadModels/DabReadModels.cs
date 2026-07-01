namespace Stainer.Web.Application.ReadModels;

public sealed record DabMixPositionResponse(
    string Id,
    string Code,
    int PositionNo,
    bool IsEnabled,
    string Status,
    string? ActiveDabBatchId,
    DateTimeOffset? UpdatedAtUtc);

public sealed record DabBatchResponse(
    bool Ok,
    string? CommandId,
    bool Replayed,
    string BatchId,
    string PositionId,
    string PositionCode,
    string Status,
    string CleaningStatus,
    string? DabAReagentBottleId,
    string? DabAReagentBottleBarcode,
    string? DabBReagentBottleId,
    string? DabBReagentBottleBarcode,
    IReadOnlyList<string> TaskIds,
    int SlideCount,
    int VolumePerSlideUl,
    int LineReserveVolumeUl,
    int DabARatioParts,
    int DabBRatioParts,
    int WaterRatioParts,
    int TotalRequiredVolumeUl,
    int ActualPreparedVolumeUl,
    int DabAVolumeUl,
    int DabBVolumeUl,
    int WaterVolumeUl,
    int UsedVolumeUl,
    int RemainingVolumeUl,
    DateTimeOffset? PreparedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? CleaningConfirmedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<DabReservationResponse> Reservations,
    string Message);

public sealed record DabReservationResponse(
    string ReservationId,
    string ReagentCode,
    string SourceRole,
    string Status,
    string? ReagentBottleId,
    string? ReagentBottleBarcode,
    int ReservedVolumeUl);
