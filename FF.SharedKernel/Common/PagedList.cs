namespace FF.SharedKernel.Common;

public class PagedList<T>(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
{
    public IReadOnlyList<T> Items { get; } = items;
    public int Page { get; } = page;
    public int PageSize { get; } = pageSize;
    public int TotalCount { get; } = totalCount;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;

    public static PagedList<T> Create(IEnumerable<T> source, int page, int pageSize)
    {
        var list = source.ToList();
        var totalCount = list.Count;
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedList<T>(items, page, pageSize, totalCount);
    }
}

public record PaginationParams(int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;
    public int PageSize { get; init; } = Math.Min(PageSize, MaxPageSize);
}