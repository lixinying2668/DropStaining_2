namespace Stainer.Web.Infrastructure.Web;

using Microsoft.Extensions.Hosting;
using Stainer.Web.Application.ReadModels;
using Stainer.Web.Application.Services;
using Stainer.Web.Application.Requests;

public static partial class WebHostEndpointExtensions
{
    public static void MapStainerWebHostEndpoints(this WebApplication app)
    {
        app.MapHub<MachineHub>(MachineHub.Route);
        var legacyRuntimeCompatibilityEnabled = app.Environment.IsDevelopment()
            || app.Environment.IsEnvironment("Testing");

        MapDeviceAndOperationsEndpoints(app);
        MapIdentityEndpoints(app);
        MapWorkflowEndpoints(app);
        MapEngineeringEndpoints(app);
        MapDabEndpoints(app);
        MapScannerConfigurationEndpoints(app);
        MapSerialConnectionEndpoints(app);
        MapPrecisionCalibrationEndpoints(app);
        MapMixerParameterEndpoints(app);
        MapWashValveConfigEndpoints(app);
        MapAppSettingsEndpoints(app);
        MapReagentPositionConfigEndpoints(app);
        MapCoordinatePointEndpoints(app);
        MapRobotArmEndpoints(app);
        MapTraceabilityEndpoints(app);
        MapTaskAndReagentEndpoints(app);
        MapRunEndpoints(app);
        MapCompatibilityEndpoints(app, legacyRuntimeCompatibilityEnabled);
        MapTwinEndpoints(app);
    }

    private static async Task<IResult> ExecuteBusinessAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BusinessSelectionRequiredException ex)
        {
            return Results.Json(ex.Response, statusCode: StatusCodes.Status409Conflict);
        }
        catch (BusinessRuleException ex)
        {
            return Results.Json(new { code = ex.Code, detail = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static TraceAlarmResponse ToNonTechnicalAlarm(TraceAlarmResponse alarm)
    {
        return alarm with
        {
            Code = OperatorAlarmPresentation.Category(alarm.Code),
            Message = OperatorAlarmPresentation.Summary(alarm.Code, alarm.Severity),
            AckReason = null,
            Actions = alarm.Actions.Select(x => x with { Message = OperatorAlarmPresentation.ActionSummary(x.Action) }).ToList()
        };
    }

    private static MachineRunDetailResponse ToOperatorRun(MachineRunDetailResponse run)
    {
        return run with
        {
            CoordinateSnapshotJson = string.Empty,
            LiquidClassSnapshotJson = string.Empty,
            ChannelBatches = run.ChannelBatches
                .Select(x => x with { LiquidClassSnapshotJson = string.Empty })
                .ToList()
        };
    }

    private static MachineRunDetailResponse ToNonTechnicalRun(MachineRunDetailResponse run)
    {
        return run with
        {
            Alarms = run.Alarms.Select(x => x with
            {
                Code = OperatorAlarmPresentation.Category(x.Code),
                Message = OperatorAlarmPresentation.Summary(x.Code, x.Severity)
            }).ToList()
        };
    }

    private static async Task<T?> ReadOptionalJsonAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is null or 0)
        {
            return default;
        }

        if (context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return default;
        }

        return await context.Request.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private static IResult ToCsvFile(CsvExportResult export)
    {
        return Results.File(export.Content, export.ContentType, export.FileName);
    }
}
