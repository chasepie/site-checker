namespace SiteChecker.Domain.DTOs;

public class PagedResponse<T> where T : class
{
    public required List<T> Items { get; set; }
    public required int TotalItems { get; set; }
    public required int PageNumber { get; set; }
    public required int PageSize { get; set; }
}
