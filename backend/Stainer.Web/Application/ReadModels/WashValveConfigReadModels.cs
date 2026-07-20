namespace Stainer.Web.Application.ReadModels;

public sealed record WashValveConfigResponse(
    string ScopeKey,
    decimal? WashTempC,
    bool SolenoidOpen,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record WashValveConfigMutationResponse(
    bool Ok,
    string CommandId,
    bool Replayed,
    string EntityType,
    string EntityId,
    string Message);
