namespace Stainer.Web.Domain.Entities;

public sealed class WorkflowStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkflowVersionId { get; set; } = string.Empty;
    public int StepNo { get; set; }
    public string MajorStepCode { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? ReagentCode { get; set; }
    public int? VolumeUl { get; set; }
    public int? DurationSeconds { get; set; }
    public int? TargetTemperatureDeciC { get; set; }
    public string MixParametersJson { get; set; } = "{}";
    public string WashParametersJson { get; set; } = "{}";
    public string FailureStrategy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public WorkflowVersion? WorkflowVersion { get; set; }
}
