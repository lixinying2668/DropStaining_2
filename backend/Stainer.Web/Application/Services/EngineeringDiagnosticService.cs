using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Application.Services;

public sealed class EngineeringDiagnosticService(
    StainerDbContext dbContext,
    ThermalControlService thermalControlService,
    FluidicsControlService fluidicsControlService,
    MotionControlService motionControlService)
{
    private const int MaxPageSize = 200;
    private const int MaxExportRows = 5000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EngineeringDeviceDiagnosticsResponse> GetDeviceStateAsync(CancellationToken cancellationToken = default)
    {
        var thermal = await thermalControlService.GetStateAsync(false, cancellationToken);
        var fluidics = await fluidicsControlService.GetStateAsync(cancellationToken);
        var motion = await motionControlService.GetDeviceModuleStatesAsync(cancellationToken);
        return new EngineeringDeviceDiagnosticsResponse(
            thermal,
            fluidics,
            motion.Select(x => (object)new
            {
                x.ModuleCode,
                x.ConnectionStatus,
                x.CurrentAction,
                x.CurrentParametersJson,
                x.ErrorCode,
                x.ErrorMessage
            }).ToList(),
            DateTimeOffset.UtcNow);
    }

    public async Task<TraceabilityListResponse<EngineeringCommandLogResponse>> ListCommandLogAsync(IQueryCollection query, CancellationToken cancellationToken = default)
    {
        var filters = DiagnosticFilters.From(query);
        var candidates = await ApplyCommandFilters(dbContext.DeviceCommandExecutions.AsNoTracking(), filters)
            .ToListAsync(cancellationToken);
        var rows = candidates
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .Select(ToCommandResponse)
            .ToList();
        return new TraceabilityListResponse<EngineeringCommandLogResponse>(candidates.Count, filters.Page, filters.PageSize, rows);
    }

    public async Task<TraceabilityListResponse<EngineeringErrorCodeResponse>> ListErrorsAsync(IQueryCollection query, CancellationToken cancellationToken = default)
    {
        var filters = DiagnosticFilters.From(query);
        var errors = new List<EngineeringErrorCodeResponse>();

        var commandErrors = await dbContext.DeviceCommandExecutions
            .AsNoTracking()
            .Where(x => x.Status == DeviceCommandStatus.Failed || x.Status == DeviceCommandStatus.Unknown)
            .Where(x => filters.Status == null || x.Status == filters.Status)
            .Where(x => filters.MachineRunId == null || x.MachineRunId == filters.MachineRunId)
            .Where(x => filters.CommandType == null || x.CommandType == filters.CommandType)
            .Where(x => filters.FromUtc == null || x.CreatedAtUtc >= filters.FromUtc)
            .Where(x => filters.ToUtc == null || x.CreatedAtUtc <= filters.ToUtc)
            .ToListAsync(cancellationToken);
        errors.AddRange(commandErrors.Select(x => new EngineeringErrorCodeResponse(
                "DeviceCommand",
                x.Id,
                ExtractJsonValue(x.ResultJson, "errorCode") ?? x.Status,
                x.Status,
                ExtractJsonValue(x.ResultJson, "message") ?? x.ResultJson,
                x.CommandType,
                x.MachineRunId,
                x.CreatedAtUtc)));

        errors.AddRange(await dbContext.DeviceInitializationChecks
            .AsNoTracking()
            .Where(x => x.Status == DeviceInitializationCheckStatus.Failed
                || x.Status == DeviceInitializationCheckStatus.TimedOut
                || x.Status == DeviceInitializationCheckStatus.Unknown)
            .Where(x => filters.Status == null || x.Status == filters.Status)
            .Where(x => filters.ModuleCode == null || x.ModuleCode == filters.ModuleCode)
            .Where(x => filters.FromUtc == null || (x.CompletedAtUtc ?? x.StartedAtUtc ?? DateTimeOffset.MinValue) >= filters.FromUtc)
            .Where(x => filters.ToUtc == null || (x.CompletedAtUtc ?? x.StartedAtUtc ?? DateTimeOffset.MinValue) <= filters.ToUtc)
            .Select(x => new EngineeringErrorCodeResponse(
                "DeviceInitializationCheck",
                x.Id,
                x.ErrorCode ?? x.Status,
                x.Status,
                x.Message,
                x.ModuleCode,
                null,
                x.CompletedAtUtc ?? x.StartedAtUtc ?? DateTimeOffset.MinValue))
            .ToListAsync(cancellationToken));

        errors.AddRange(await dbContext.DeviceCommunicationRecords
            .AsNoTracking()
            .Where(x => x.PersistenceStatus != DeviceCommunicationPersistenceStatus.Complete)
            .Where(x => filters.PersistenceStatus == null || x.PersistenceStatus == filters.PersistenceStatus)
            .Where(x => filters.ModuleCode == null || x.ModuleCode == filters.ModuleCode)
            .Where(x => filters.CommandId == null || x.CommandId == filters.CommandId)
            .Where(x => filters.FromUtc == null || x.CreatedAtUtc >= filters.FromUtc)
            .Where(x => filters.ToUtc == null || x.CreatedAtUtc <= filters.ToUtc)
            .Select(x => new EngineeringErrorCodeResponse(
                "DeviceCommunication",
                x.Id,
                "communication_record_" + x.PersistenceStatus.ToLower(),
                x.PersistenceStatus,
                x.PersistenceFailureReason ?? x.Message,
                x.ModuleCode,
                null,
                x.PersistenceLastAttemptAtUtc))
            .ToListAsync(cancellationToken));

        errors.AddRange(await dbContext.Alarms
            .AsNoTracking()
            .Where(x => filters.Status == null || x.Status == filters.Status)
            .Where(x => filters.MachineRunId == null || x.MachineRunId == filters.MachineRunId)
            .Where(x => filters.FromUtc == null || x.CreatedAtUtc >= filters.FromUtc)
            .Where(x => filters.ToUtc == null || x.CreatedAtUtc <= filters.ToUtc)
            .Select(x => new EngineeringErrorCodeResponse(
                "Alarm",
                x.Id,
                x.Code,
                x.Status,
                x.Message,
                null,
                x.MachineRunId,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken));

        var ordered = errors
            .Where(x => filters.Code == null || x.Code.Contains(filters.Code, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
        var page = ordered
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .ToList();
        return new TraceabilityListResponse<EngineeringErrorCodeResponse>(ordered.Count, filters.Page, filters.PageSize, page);
    }

    public async Task<TraceabilityListResponse<EngineeringMockCommunicationResponse>> ListMockCommunicationsAsync(IQueryCollection query, CancellationToken cancellationToken = default)
    {
        var filters = DiagnosticFilters.From(query);
        var baseQuery = ApplyCommunicationFilters(dbContext.DeviceCommunicationRecords.AsNoTracking(), filters);
        var candidates = await baseQuery.ToListAsync(cancellationToken);
        var rows = candidates
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .Select(ToCommunicationResponse)
            .ToList();
        return new TraceabilityListResponse<EngineeringMockCommunicationResponse>(candidates.Count, filters.Page, filters.PageSize, rows);
    }

    public async Task<CsvExportResult> ExportCommandLogAsync(IQueryCollection query, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        var filters = DiagnosticFilters.From(query) with { Page = 1, PageSize = MaxExportRows };
        var rows = (await ApplyCommandFilters(dbContext.DeviceCommandExecutions.AsNoTracking(), filters)
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(MaxExportRows)
            .Select(ToCommandResponse)
            .ToList();
        var csv = BuildCsv(
            ["CommandId", "RunId", "StepId", "CommandType", "Status", "LiquidClassVersionId", "CreatedAtUtc", "SentAtUtc", "AckAtUtc", "CompletedAtUtc"],
            rows.Select(x => new[]
            {
                x.DeviceCommandExecutionId,
                x.MachineRunId,
                x.WorkflowStepExecutionId ?? string.Empty,
                x.CommandType,
                x.Status,
                x.LiquidClassVersionId ?? string.Empty,
                Format(x.CreatedAtUtc),
                Format(x.CommandSentAtUtc),
                Format(x.AcknowledgedAtUtc),
                Format(x.CompletedAtUtc)
            }));
        await AddExportAuditAsync(actor, "engineering_command_log", rows.Count, filters, cancellationToken);
        return CsvExport("engineering-command-log", csv, rows.Count);
    }

    public async Task<CsvExportResult> ExportMockCommunicationsAsync(IQueryCollection query, AuthenticatedUser actor, CancellationToken cancellationToken = default)
    {
        var filters = DiagnosticFilters.From(query) with { Page = 1, PageSize = MaxExportRows };
        var rows = (await ApplyCommunicationFilters(dbContext.DeviceCommunicationRecords.AsNoTracking(), filters)
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(MaxExportRows)
            .Select(ToCommunicationResponse)
            .ToList();
        var csv = BuildCsv(
            ["RecordId", "Adapter", "Module", "Action", "CommandId", "CorrelationId", "DeviceStatus", "PersistenceStatus", "PersistenceFailureReason", "PersistenceAttemptCount", "PersistenceLastAttemptAtUtc", "Ok", "Acknowledged", "ErrorCode", "Message", "StartedAtUtc", "CompletedAtUtc"],
            rows.Select(x => new[]
            {
                x.Id,
                x.AdapterName,
                x.ModuleCode,
                x.Action,
                x.CommandId,
                x.CorrelationId ?? string.Empty,
                x.Status,
                x.PersistenceStatus,
                x.PersistenceFailureReason ?? string.Empty,
                x.PersistenceAttemptCount.ToString(),
                Format(x.PersistenceLastAttemptAtUtc),
                x.Ok.ToString(),
                x.Acknowledged.ToString(),
                x.ErrorCode ?? string.Empty,
                x.Message,
                Format(x.StartedAtUtc),
                Format(x.CompletedAtUtc)
            }));
        await AddExportAuditAsync(
            actor,
            "engineering_mock_communications",
            rows.Count,
            filters,
            cancellationToken,
            rows.Count(x => x.PersistenceStatus != DeviceCommunicationPersistenceStatus.Complete));
        return CsvExport("engineering-mock-communications", csv, rows.Count);
    }

    private static IQueryable<DeviceCommandExecution> ApplyCommandFilters(IQueryable<DeviceCommandExecution> query, DiagnosticFilters filters)
    {
        if (filters.Status is not null) query = query.Where(x => x.Status == filters.Status);
        if (filters.CommandType is not null) query = query.Where(x => x.CommandType == filters.CommandType);
        if (filters.CommandId is not null) query = query.Where(x => x.Id == filters.CommandId);
        if (filters.MachineRunId is not null) query = query.Where(x => x.MachineRunId == filters.MachineRunId);
        if (filters.FromUtc is not null) query = query.Where(x => x.CreatedAtUtc >= filters.FromUtc);
        if (filters.ToUtc is not null) query = query.Where(x => x.CreatedAtUtc <= filters.ToUtc);
        return query;
    }

    private static IQueryable<DeviceCommunicationRecord> ApplyCommunicationFilters(IQueryable<DeviceCommunicationRecord> query, DiagnosticFilters filters)
    {
        if (filters.Status is not null) query = query.Where(x => x.Status == filters.Status);
        if (filters.PersistenceStatus is not null) query = query.Where(x => x.PersistenceStatus == filters.PersistenceStatus);
        if (filters.ModuleCode is not null) query = query.Where(x => x.ModuleCode == filters.ModuleCode);
        if (filters.Action is not null) query = query.Where(x => x.Action == filters.Action);
        if (filters.CommandId is not null) query = query.Where(x => x.CommandId == filters.CommandId);
        if (filters.FromUtc is not null) query = query.Where(x => x.CreatedAtUtc >= filters.FromUtc);
        if (filters.ToUtc is not null) query = query.Where(x => x.CreatedAtUtc <= filters.ToUtc);
        return query;
    }

    private static EngineeringCommandLogResponse ToCommandResponse(DeviceCommandExecution x)
    {
        return new EngineeringCommandLogResponse(
            x.Id,
            x.MachineRunId,
            x.WorkflowStepExecutionId,
            x.CommandType,
            x.Status,
            x.PayloadJson,
            x.ResultJson,
            x.LiquidClassVersionId,
            x.LiquidClassVersionNo,
            x.CreatedAtUtc,
            x.CommandSentAtUtc,
            x.AcknowledgedAtUtc,
            x.CompletedAtUtc);
    }

    private static EngineeringMockCommunicationResponse ToCommunicationResponse(DeviceCommunicationRecord x)
    {
        return new EngineeringMockCommunicationResponse(
            x.Id,
            x.DeviceMode,
            x.AdapterName,
            x.ModuleCode,
            x.Action,
            x.CommandId,
            x.CorrelationId,
            x.Actor,
            x.Source,
            x.Status,
            x.Ok,
            x.Acknowledged,
            x.ErrorCode,
            x.Message,
            x.RequestJson,
            x.ResponseJson,
            x.PersistenceStatus,
            x.PersistenceFailureReason,
            x.PersistenceAttemptCount,
            x.PersistenceLastAttemptAtUtc,
            x.PersistenceCompletedAtUtc,
            x.StartedAtUtc,
            x.CompletedAtUtc,
            x.CreatedAtUtc);
    }

    private async Task AddExportAuditAsync(
        AuthenticatedUser actor,
        string exportType,
        int rowCount,
        DiagnosticFilters filters,
        CancellationToken cancellationToken,
        int incompleteCommunicationCount = 0)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor.UserId,
            Action = "engineering.diagnostics.export",
            EntityType = "EngineeringDiagnostics",
            EntityId = exportType,
            Message = JsonSerializer.Serialize(new { exportType, rowCount, incompleteCommunicationCount, filters = filters.ToAuditObject() }, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CsvExportResult CsvExport(string exportType, string csv, int rowCount)
    {
        return new CsvExportResult(
            $"{exportType}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}Z.csv",
            "text/csv; charset=utf-8",
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),
            rowCount);
    }

    private static string BuildCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(x => EscapeCsv(ScrubSensitive(x)))));
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains('"') || text.Contains(',') || text.Contains('\r') || text.Contains('\n')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    private static string Format(DateTimeOffset? value)
    {
        return value?.UtcDateTime.ToString("O") ?? string.Empty;
    }

    private static string ScrubSensitive(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Contains("password", StringComparison.OrdinalIgnoreCase)
            || text.Contains("token", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connectionString", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connection_string", StringComparison.OrdinalIgnoreCase))
        {
            return "[redacted sensitive details]";
        }

        return text;
    }

    private static string? ExtractJsonValue(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                ? property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record DiagnosticFilters(
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        string? Status,
        string? PersistenceStatus,
        string? ModuleCode,
        string? Action,
        string? CommandType,
        string? CommandId,
        string? MachineRunId,
        string? Code,
        int Page,
        int PageSize)
    {
        public static DiagnosticFilters From(IQueryCollection query)
        {
            var page = Math.Max(1, ParseInt(query, "page") ?? 1);
            var pageSize = Math.Clamp(ParseInt(query, "pageSize") ?? 50, 1, MaxPageSize);
            return new DiagnosticFilters(
                ParseDate(query, "fromUtc") ?? ParseDate(query, "from"),
                ParseDate(query, "toUtc") ?? ParseDate(query, "to"),
                Text(query, "status"),
                Text(query, "persistenceStatus"),
                Text(query, "moduleCode") ?? Text(query, "module"),
                Text(query, "action"),
                Text(query, "commandType"),
                Text(query, "commandId"),
                Text(query, "machineRunId"),
                Text(query, "code"),
                page,
                pageSize);
        }

        public object ToAuditObject()
        {
            return new
            {
                FromUtc,
                ToUtc,
                Status,
                PersistenceStatus,
                ModuleCode,
                Action,
                CommandType,
                CommandId,
                MachineRunId,
                Code
            };
        }

        private static string? Text(IQueryCollection query, string key)
        {
            return query.TryGetValue(key, out var value) && !StringValues.IsNullOrEmpty(value)
                ? value.ToString().Trim()
                : null;
        }

        private static DateTimeOffset? ParseDate(IQueryCollection query, string key)
        {
            var value = Text(query, key);
            return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
        }

        private static int? ParseInt(IQueryCollection query, string key)
        {
            var value = Text(query, key);
            return int.TryParse(value, out var parsed) ? parsed : null;
        }
    }
}
