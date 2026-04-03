using SiteChecker.Domain.Entities;

namespace SiteChecker.Domain.Ports;

public interface ISiteRepository
{
    Task<Site?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Site>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(Site Site, SiteCheck? LatestCheck)>> GetAllWithLatestCheckAsync(
        CancellationToken cancellationToken = default);

    Task AddAsync(Site site, CancellationToken cancellationToken = default);

    Task UpdateAsync(Site site, CancellationToken cancellationToken = default);

    Task RemoveAsync(Site site, CancellationToken cancellationToken = default);
}
