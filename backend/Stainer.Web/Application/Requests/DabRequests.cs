namespace Stainer.Web.Application.Requests;

public sealed record CreateDabBatchRequest(
    string CommandId,
    IReadOnlyList<string> TaskIds,
    string DabAReagentBottleId,
    string DabBReagentBottleId,
    string? PositionCode = null);

public sealed record DabBatchCommandRequest(string CommandId);

public sealed record CompleteDabPreparationRequest(
    string CommandId,
    int ActualPreparedVolumeUl);

public sealed record ConsumeDabBatchRequest(
    string CommandId,
    int VolumeUl,
    string? StainingTaskId = null);

public sealed record FailDabBatchRequest(
    string CommandId,
    string Reason);
