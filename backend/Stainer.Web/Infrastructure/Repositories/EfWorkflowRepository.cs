using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Repositories;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Infrastructure.Repositories;

public sealed class EfWorkflowRepository(StainerDbContext dbContext) : IWorkflowRepository
{
    public async Task AddDefinitionAsync(WorkflowDefinition workflowDefinition, CancellationToken cancellationToken = default)
    {
        dbContext.WorkflowDefinitions.Add(workflowDefinition);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowVersion>> GetPublishedVersionsForPrimaryAntibodyAsync(string primaryAntibodyCode, CancellationToken cancellationToken = default)
    {
        return await dbContext.WorkflowVersions
            .AsNoTracking()
            .Include(x => x.WorkflowDefinition)
            .Where(x => x.Status == WorkflowVersionStatus.Published)
            .Where(x => x.PrimaryAntibodyMappings.Any(mapping => mapping.PrimaryAntibodyCode == primaryAntibodyCode && mapping.IsEnabled))
            .OrderBy(x => x.WorkflowDefinition!.Code)
            .ThenBy(x => x.VersionNo)
            .ToListAsync(cancellationToken);
    }
}
