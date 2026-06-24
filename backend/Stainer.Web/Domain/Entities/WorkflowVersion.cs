namespace Stainer.Web.Domain.Entities;

public sealed class WorkflowVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkflowDefinitionId { get; set; } = string.Empty;
    public int VersionNo { get; set; }
    public string Status { get; set; } = WorkflowVersionStatus.Draft;
    public string ChangeNote { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset? RetiredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public WorkflowDefinition? WorkflowDefinition { get; set; }
    public ICollection<WorkflowStep> Steps { get; } = new List<WorkflowStep>();
    public ICollection<WorkflowReagentRequirement> ReagentRequirements { get; } = new List<WorkflowReagentRequirement>();
    public ICollection<PrimaryAntibodyWorkflowMapping> PrimaryAntibodyMappings { get; } = new List<PrimaryAntibodyWorkflowMapping>();
}
