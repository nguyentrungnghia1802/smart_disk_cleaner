namespace SmartDiskCleaner.Application;

using SmartDiskCleaner.Domain;

public enum SearchSortOption
{
    SizeDescending,
    SizeAscending,
    NameAscending,
    NameDescending,
    PathAscending,
    PathDescending
}

public sealed record SearchFilterCriteria(
    string? SearchText = null,
    CleanupCategory? Category = null,
    RiskLevel? Risk = null,
    long? MinSizeBytes = null,
    bool? ProtectedOnly = null,
    bool? ReparseOnly = null,
    bool? InaccessibleOnly = null,
    bool? LargeFileOnly = null,
    SearchSortOption SortBy = SearchSortOption.SizeDescending,
    int Limit = 500);

public interface IScanSearchService
{
    IReadOnlyList<ScanNode> Search(ScanSnapshot snapshot, SearchFilterCriteria criteria);
}

public sealed class ScanSearchService : IScanSearchService
{
    public IReadOnlyList<ScanNode> Search(ScanSnapshot snapshot, SearchFilterCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(criteria);

        IEnumerable<ScanNode> query = snapshot.Nodes;

        // 1. Text filter
        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
        {
            var text = criteria.SearchText.Trim();
            query = query.Where(n =>
                n.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                n.FullPath.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        // 2. Category filter
        if (criteria.Category.HasValue)
        {
            query = query.Where(n => n.Classification.Category == criteria.Category.Value);
        }

        // 3. Risk filter
        if (criteria.Risk.HasValue)
        {
            query = query.Where(n => n.Classification.Risk == criteria.Risk.Value);
        }

        // 4. Min size filter
        if (criteria.MinSizeBytes.HasValue && criteria.MinSizeBytes.Value > 0)
        {
            var minBytes = criteria.MinSizeBytes.Value;
            query = query.Where(n =>
                (n.RecursiveUniqueAllocatedBytes ?? n.RecursiveAllocatedBytes ?? n.RecursiveLogicalBytes) >= minBytes);
        }

        // 5. Protected filter
        if (criteria.ProtectedOnly == true)
        {
            query = query.Where(n => n.Classification.IsProtected);
        }

        // 6. Reparse points filter
        if (criteria.ReparseOnly == true)
        {
            query = query.Where(n => n.IsReparsePoint);
        }

        // 7. Inaccessible filter
        if (criteria.InaccessibleOnly == true)
        {
            query = query.Where(n => !n.IsAccessible);
        }

        // 8. Large file only filter
        if (criteria.LargeFileOnly == true)
        {
            query = query.Where(n => n.Classification.Category == CleanupCategory.LargeFileNotJunk);
        }

        // 9. Sorting
        query = criteria.SortBy switch
        {
            SearchSortOption.SizeDescending => query.OrderByDescending(n => n.RecursiveUniqueAllocatedBytes ?? n.RecursiveAllocatedBytes ?? n.RecursiveLogicalBytes)
                                                    .ThenBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase),
            SearchSortOption.SizeAscending => query.OrderBy(n => n.RecursiveUniqueAllocatedBytes ?? n.RecursiveAllocatedBytes ?? n.RecursiveLogicalBytes)
                                                   .ThenBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase),
            SearchSortOption.NameAscending => query.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                                                   .ThenBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase),
            SearchSortOption.NameDescending => query.OrderByDescending(n => n.Name, StringComparer.OrdinalIgnoreCase)
                                                    .ThenBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase),
            SearchSortOption.PathAscending => query.OrderBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase),
            SearchSortOption.PathDescending => query.OrderByDescending(n => n.FullPath, StringComparer.OrdinalIgnoreCase),
            _ => query
        };

        if (criteria.Limit > 0)
        {
            query = query.Take(criteria.Limit);
        }

        return query.ToArray();
    }
}
