namespace SmartDiskCleaner.Domain;

public sealed record AdaptiveTreeOptions(int ChildThreshold = 10, decimal LargestFolderRatio = 0.40m, string RankingPolicyVersion = "1")
{
    public void Validate()
    {
        if (ChildThreshold < 0) throw new ArgumentOutOfRangeException(nameof(ChildThreshold));
        if (LargestFolderRatio <= 0 || LargestFolderRatio > 1) throw new ArgumentOutOfRangeException(nameof(LargestFolderRatio));
        ArgumentException.ThrowIfNullOrWhiteSpace(RankingPolicyVersion);
    }
}

public sealed class AdaptiveTreePolicy
{
    private readonly AdaptiveTreeOptions _options;

    public AdaptiveTreePolicy(AdaptiveTreeOptions? options = null)
    {
        _options = options ?? new AdaptiveTreeOptions();
        _options.Validate();
    }

    public PresentationDecision Evaluate(IReadOnlyList<ScanNode> immediateChildren)
    {
        ArgumentNullException.ThrowIfNull(immediateChildren);
        var visible = immediateChildren
            .OrderBy(n => PathNormalizer.Normalize(n.FullPath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => PathNormalizer.Normalize(n.FullPath), StringComparer.Ordinal)
            .ThenBy(n => n.Id)
            .Select(n => n.Id)
            .ToArray();
        var directories = immediateChildren.Where(n => n.NodeType == NodeType.Directory).ToArray();
        var metric = ChooseMetric(directories);
        var ranked = directories
            .OrderByDescending(n => RankingBytes(n, metric))
            .ThenBy(n => PathNormalizer.Normalize(n.FullPath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => PathNormalizer.Normalize(n.FullPath), StringComparer.Ordinal)
            .ThenBy(n => n.Id)
            .ToArray();
        var triggered = immediateChildren.Count > _options.ChildThreshold;
        var count = triggered
            ? (int)Math.Ceiling(directories.Length * _options.LargestFolderRatio)
            : directories.Length;
        var expanded = ranked.Take(count).Select(n => n.Id).ToArray();
        var collapsed = ranked.Skip(count).Select(n => n.Id).ToArray();
        return new PresentationDecision(visible, expanded, collapsed, metric, triggered);
    }

    private static RankingMetricKind ChooseMetric(IReadOnlyList<ScanNode> directories)
    {
        if (directories.All(n => n.Aggregate.UniqueAllocatedBytesRecursive.HasValue)) return RankingMetricKind.UniqueAllocatedRecursive;
        if (directories.All(n => n.Aggregate.AllocatedBytesRecursive.HasValue)) return RankingMetricKind.AllocatedRecursive;
        return RankingMetricKind.LogicalRecursive;
    }

    private static long RankingBytes(ScanNode node, RankingMetricKind metric) => metric switch
    {
        RankingMetricKind.UniqueAllocatedRecursive => node.Aggregate.UniqueAllocatedBytesRecursive!.Value,
        RankingMetricKind.AllocatedRecursive => node.Aggregate.AllocatedBytesRecursive!.Value,
        _ => node.Aggregate.LogicalBytesRecursive
    };
}
