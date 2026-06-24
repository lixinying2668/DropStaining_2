using Stainer.Web.Domain.Entities;

namespace Stainer.Web.Application.Repositories;

public interface IWorkflowRepository
{
    Task AddDefinitionAsync(WorkflowDefinition workflowDefinition, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowVersion>> GetPublishedVersionsForPrimaryAntibodyAsync(string primaryAntibodyCode, CancellationToken cancellationToken = default);
}
