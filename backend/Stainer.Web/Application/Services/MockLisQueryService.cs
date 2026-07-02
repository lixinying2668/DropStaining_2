using System.Text.Json;
using Stainer.Web.Application.Devices;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Requests;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class MockLisQueryService(
    StainerDbContext dbContext,
    CommandIdempotencyService idempotencyService,
    HospitalBarcodeNormalizer normalizer,
    IDeviceAdapter deviceAdapter,
    DeviceCommunicationPersistenceService communicationPersistence,
    IMockLisAdapter lisAdapter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<MockLisQueryResponse> QueryAsync(
        MockLisQueryRequest request,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        return idempotencyService.RunAsync(
            request.CommandId,
            "lis.mock.query",
            request,
            actor,
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var rawCode = request.RawCode ?? string.Empty;
                var normalizedCode = normalizer.Normalize(rawCode);
                var log = new LisQueryLog
                {
                    Source = "MockLIS",
                    Status = LisQueryStatus.Running,
                    RawCode = rawCode,
                    NormalizedCode = normalizedCode,
                    StartedAtUtc = now,
                    CreatedAtUtc = now
                };
                dbContext.LisQueryLogs.Add(log);

                var operationRequest = new DeviceOperationRequest(
                    new DeviceCommandContext(request.CommandId, null, actor.Username, nameof(MockLisQueryService)),
                    DeviceModules.Lis,
                    "query",
                    new Dictionary<string, object?> { ["normalizedCode"] = normalizedCode });
                var communicationRecord = communicationPersistence.Begin(operationRequest);
                var deviceResult = await deviceAdapter.QueryLisAsync(operationRequest, cancellationToken);
                communicationPersistence.Complete(communicationRecord, deviceResult);

                if (!deviceResult.Ok)
                {
                    var status = deviceResult.Status == DeviceCommandStatuses.TimedOut
                        ? LisQueryStatus.TimedOut
                        : LisQueryStatus.Failed;
                    CompleteLog(log, status, [], deviceResult.ErrorCode ?? "lis_device_error", deviceResult.Message, null);
                    AddAudit(actor, log, "lis.mock.query_failed", new
                    {
                        request.CommandId,
                        log.RawCode,
                        log.NormalizedCode,
                        log.Status,
                        log.ErrorCode,
                        log.ErrorMessage,
                        deviceResult.ModuleCode,
                        deviceStatus = deviceResult.Status
                    });
                    return new CommandExecutionResult<MockLisQueryResponse>(
                        ToResponse(request.CommandId, false, log, "Mock LIS query failed before lookup."),
                        "LisQueryLog",
                        log.Id);
                }

                try
                {
                    var result = await lisAdapter.QueryAsync(normalizedCode, cancellationToken);
                    CompleteLog(log, result.Status, result.CandidatePrimaryAntibodyCodes, result.ErrorCode, result.ErrorMessage, null);
                    AddAudit(actor, log, "lis.mock.query", new
                    {
                        request.CommandId,
                        log.RawCode,
                        log.NormalizedCode,
                        log.Status,
                        candidates = result.CandidatePrimaryAntibodyCodes,
                        log.ErrorCode,
                        log.ErrorMessage
                    });
                    return new CommandExecutionResult<MockLisQueryResponse>(
                        ToResponse(request.CommandId, false, log, StatusMessage(log.Status)),
                        "LisQueryLog",
                        log.Id);
                }
                catch (MockLisException ex)
                {
                    CompleteLog(log, LisQueryStatus.Failed, [], ex.Code, ex.Message, ex);
                    AddAudit(actor, log, "lis.mock.query_exception", new
                    {
                        request.CommandId,
                        log.RawCode,
                        log.NormalizedCode,
                        log.Status,
                        log.ErrorCode,
                        log.ErrorMessage
                    });
                    return new CommandExecutionResult<MockLisQueryResponse>(
                        ToResponse(request.CommandId, false, log, "Mock LIS query raised a configured exception."),
                        "LisQueryLog",
                        log.Id);
                }
            },
            cancellationToken);
    }

    private static void CompleteLog(
        LisQueryLog log,
        string status,
        IReadOnlyList<string> candidates,
        string? errorCode,
        string? errorMessage,
        Exception? exception)
    {
        log.Status = status;
        log.CandidatePrimaryAntibodyCodesJson = JsonSerializer.Serialize(candidates, JsonOptions);
        log.ErrorCode = errorCode;
        log.ErrorMessage = errorMessage;
        log.ExceptionJson = exception is null
            ? "{}"
            : JsonSerializer.Serialize(new { exception.GetType().Name, exception.Message }, JsonOptions);
        log.CompletedAtUtc = DateTimeOffset.UtcNow;
        log.UpdatedAtUtc = log.CompletedAtUtc;
    }

    private void AddAudit(AuthenticatedUser actor, LisQueryLog log, string action, object payload)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = string.IsNullOrWhiteSpace(actor.UserId) ? null : actor.UserId,
            Action = action,
            EntityType = "LisQueryLog",
            EntityId = log.Id,
            Message = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static MockLisQueryResponse ToResponse(string commandId, bool replayed, LisQueryLog log, string message)
    {
        return new MockLisQueryResponse(
            log.Status is LisQueryStatus.SingleCandidate or LisQueryStatus.MultipleCandidates,
            commandId,
            replayed,
            log.Id,
            log.Status,
            log.RawCode,
            log.NormalizedCode,
            DeserializeCandidates(log.CandidatePrimaryAntibodyCodesJson),
            log.ErrorCode,
            log.ErrorMessage,
            message);
    }

    private static IReadOnlyList<string> DeserializeCandidates(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string StatusMessage(string status)
    {
        return status switch
        {
            LisQueryStatus.SingleCandidate => "Mock LIS returned one primary antibody candidate.",
            LisQueryStatus.MultipleCandidates => "Mock LIS returned multiple primary antibody candidates. Operator selection is required.",
            LisQueryStatus.NoResult => "Mock LIS returned no primary antibody candidate.",
            LisQueryStatus.TimedOut => "Mock LIS query timed out.",
            LisQueryStatus.Failed => "Mock LIS query failed.",
            _ => "Mock LIS query completed."
        };
    }
}
