using Microsoft.EntityFrameworkCore;
using SiteChecker.Database.Extensions;
using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain.Events;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Database.Repositories;

public class SiteCheckRepository(SiteCheckerDbContext dbContext, IDomainEventDispatcher dispatcher) : ISiteCheckRepository
{
    public async Task<SiteCheck?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await dbContext.SiteChecks.AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == id, cancellationToken);

    public async Task<SiteCheck?> GetByIdForSiteAsync(int siteId, int id, CancellationToken cancellationToken = default)
        => await dbContext.SiteChecks.AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.SiteId == siteId && sc.Id == id, cancellationToken);

    public async Task<PagedResponse<SiteCheck>> GetPagedAsync(
        int siteId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
        => await dbContext.SiteChecks.AsNoTracking()
            .Where(sc => sc.SiteId == siteId)
            .OrderByDescending(sc => sc.StartDate)
            .ToPagedResponseAsync(pageNumber, pageSize, cancellationToken);

    public async Task<SiteCheck?> GetLatestForSiteAsync(int siteId, CancellationToken cancellationToken = default)
        => await dbContext.SiteChecks.AsNoTracking()
            .Where(sc => sc.SiteId == siteId)
            .OrderByDescending(sc => sc.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SiteCheck?> GetPreviousSuccessfulAsync(
        int siteId,
        int beforeId,
        CancellationToken cancellationToken = default)
        => await dbContext.SiteChecks.AsNoTracking()
            .Where(sc => sc.SiteId == siteId && sc.Id < beforeId && sc.Status == CheckStatus.Done)
            .OrderByDescending(sc => sc.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default)
    {
        dbContext.SiteChecks.Add(siteCheck);
        await dbContext.SaveChangesAsync(cancellationToken);
        await dispatcher.DispatchAsync(new EntityCreatedEvent<SiteCheck>(siteCheck), cancellationToken);
    }

    public async Task UpdateAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default)
    {
        var old = await dbContext.SiteChecks.AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == siteCheck.Id, cancellationToken);

        dbContext.SiteChecks.Update(siteCheck);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (old != null)
        {
            await dispatcher.DispatchAsync(new EntityUpdatedEvent<SiteCheck>(old, siteCheck), cancellationToken);
        }
    }

    public async Task RemoveAsync(SiteCheck siteCheck, CancellationToken cancellationToken = default)
    {
        dbContext.SiteChecks.Remove(siteCheck);
        await dbContext.SaveChangesAsync(cancellationToken);
        await dispatcher.DispatchAsync(new EntityDeletedEvent<SiteCheck>(siteCheck), cancellationToken);
    }

    public async Task RemoveAllForSiteAsync(int siteId, CancellationToken cancellationToken = default)
        => await dbContext.SiteChecks
            .Where(sc => sc.SiteId == siteId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task AddScreenshotAsync(SiteCheckScreenshot screenshot, CancellationToken cancellationToken = default)
    {
        dbContext.SiteCheckScreenshots.Add(screenshot);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SiteCheckScreenshot?> GetScreenshotAsync(int siteCheckId, CancellationToken cancellationToken = default)
        => await dbContext.SiteCheckScreenshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SiteCheckId == siteCheckId, cancellationToken);
}
