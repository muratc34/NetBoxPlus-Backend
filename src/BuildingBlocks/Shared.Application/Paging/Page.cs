using Shared.Domain;

namespace Shared.Application.Paging;

/// <summary>One page of a list, with the total so that a client can draw its pager.</summary>
/// <param name="PageNumber">1-based.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount);

/// <summary>Page limits shared by every list endpoint.</summary>
public static class PageLimits
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public static readonly Error OutOfRange = Error.Validation(
        "Page.OutOfRange",
        $"The page number must be at least 1 and the page size between 1 and {MaxPageSize}.");

    /// <summary>Rows to skip for a 1-based page number.</summary>
    public static int Offset(int pageNumber, int pageSize) => (pageNumber - 1) * pageSize;

    /// <summary>Whether a page number and size from a request are in range; the offset of a valid page fits an <see cref="int"/>.</summary>
    public static bool IsValid(int pageNumber, int pageSize)
        => pageSize is >= 1 and <= MaxPageSize && pageNumber >= 1 && pageNumber <= int.MaxValue / pageSize;
}
