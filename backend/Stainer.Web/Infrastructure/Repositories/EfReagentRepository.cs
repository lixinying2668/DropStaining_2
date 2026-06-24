using Microsoft.EntityFrameworkCore;
using Stainer.Web.Application.Repositories;
using Stainer.Web.Domain.Entities;
using Stainer.Web.Infrastructure.Data;

namespace Stainer.Web.Infrastructure.Repositories;

public sealed class EfReagentRepository(StainerDbContext dbContext) : IReagentRepository
{
    public async Task AddDefinitionAsync(ReagentDefinition reagentDefinition, CancellationToken cancellationToken = default)
    {
        dbContext.ReagentDefinitions.Add(reagentDefinition);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddBottleAsync(ReagentBottle reagentBottle, CancellationToken cancellationToken = default)
    {
        dbContext.ReagentBottles.Add(reagentBottle);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddScanSessionAsync(ReagentScanSession scanSession, CancellationToken cancellationToken = default)
    {
        dbContext.ReagentScanSessions.Add(scanSession);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReagentRackPlacement>> GetBottlePlacementHistoryAsync(string reagentBottleId, CancellationToken cancellationToken = default)
    {
        var placements = await dbContext.ReagentRackPlacements
            .AsNoTracking()
            .Include(x => x.ReagentRackPosition)
            .Where(x => x.ReagentBottleId == reagentBottleId)
            .ToListAsync(cancellationToken);

        return placements
            .OrderBy(x => x.PlacedAtUtc)
            .ToList();
    }
}
