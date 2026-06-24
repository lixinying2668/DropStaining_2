namespace Stainer.Web.Domain.Entities;

public sealed class WorkflowReagentRequirement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkflowVersionId { get; set; } = string.Empty;
    public string ReagentCode { get; set; } = string.Empty;
    public int? RequiredVolumeUl { get; set; }
    public bool IsRequired { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public WorkflowVersion? WorkflowVersion { get; set; }
}
