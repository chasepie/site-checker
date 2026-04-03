using Microsoft.EntityFrameworkCore;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Database.Repositories;

public class SiteRepository(SiteCheckerDbContext dbContext) : ISiteRepository
{
    public async Task<Site?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await dbContext.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Site>> GetAllAsync(CancellationToken cancellationToken = default)
        => await dbContext.Sites.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(Site Site, SiteCheck? LatestCheck)>> GetAllWithLatestCheckAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await dbContext.Sites.AsNoTracking()
            .Select(s => new
            {
                Site = s,
                LatestCheck = dbContext.SiteChecks
                    .Where(sc => sc.SiteId == s.Id)
                    .OrderByDescending(sc => sc.StartDate)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return result.Select(x => (x.Site, x.LatestCheck)).ToList();
    }

    public async Task AddAsync(Site site, CancellationToken cancellationToken = default)
    {
        dbContext.Sites.Add(site);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Site site, CancellationToken cancellationToken = default)
    {
        dbContext.Sites.Update(site);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Site site, CancellationToken cancellationToken = default)
    {
        dbContext.Sites.Remove(site);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
