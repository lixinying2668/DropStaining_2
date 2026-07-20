namespace Stainer.Web.Application.ReadModels;

public sealed record MixerParameterResponse(
    string DrawerCode,
    string? Origin,
    int? StartStroke,
    int? TotalStroke,
    int? TopDwellMs,
    int? BottomDwellMs,
    int? ForwardSpeed,
    int? ReverseSpeed,
    int? TargetCycles,
    int? RemainingCycles,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record MixerParameterMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string EntityType,
    string EntityId,
    string Message);
