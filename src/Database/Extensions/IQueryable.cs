using Microsoft.EntityFrameworkCore;
using SiteChecker.Domain.Common;

namespace SiteChecker.Database.Extensions;

public static class IQueryableExtensions
{
    extension<T>(IQueryable<T> query)
        where T : class
    {
        public async Task<PagedResponse<T>> ToPagedResponseAsync(
            int pageNumber,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            var totalItems = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(pageNumber * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResponse<T>
            {
                Items = items,
                TotalItems = totalItems,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
    }
}
