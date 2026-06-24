namespace Stainer.Web.Domain.Entities;

public sealed class PrimaryAntibodyWorkflowMapping
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PrimaryAntibodyCode { get; set; } = string.Empty;
    public string WorkflowVersionId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public WorkflowVersion? WorkflowVersion { get; set; }
}
