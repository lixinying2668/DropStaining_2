using Stainer.Web.Domain.Entities;

namespace Stainer.Web.Application.Repositories;

public interface IReagentRepository
{
    Task AddDefinitionAsync(ReagentDefinition reagentDefinition, CancellationToken cancellationToken = default);
    Task AddBottleAsync(ReagentBottle reagentBottle, CancellationToken cancellationToken = default);
    Task AddScanSessionAsync(ReagentScanSession scanSession, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReagentRackPlacement>> GetBottlePlacementHistoryAsync(string reagentBottleId, CancellationToken cancellationToken = default);
}
