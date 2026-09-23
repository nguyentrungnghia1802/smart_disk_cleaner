namespace SmartDiskCleaner.Domain;

public sealed class DirectoryAggregationService
{
    private sealed class Accumulator
    {
        public int DirectFiles;
        public int DirectDirectories;
        public int DescendantFiles;
        public int DescendantDirectories;
        public long Logical;
        public long KnownAllocated;
        public long KnownUnique;
        public int IncompleteAllocated;
        public int IncompleteIdentity;
        public int UnsupportedAllocated;
    }

    public IReadOnlyList<ScanNode> Aggregate(IReadOnlyList<ScanNode> nodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var byId = nodes.ToDictionary(n => n.Id);
        var accumulators = nodes.Where(n => n.NodeType == NodeType.Directory)
            .ToDictionary(n => n.Id, _ => new Accumulator());

        foreach (var node in nodes)
        {
            if (node.ParentId is null)
            {
                if (node.Depth != 0) throw new SnapshotInvariantException("A root node must have depth zero.");
                continue;
            }

            if (!byId.TryGetValue(node.ParentId.Value, out var parent) || parent.NodeType != NodeType.Directory)
            {
                throw new SnapshotInvariantException($"Node '{node.FullPath}' has an invalid parent.");
            }

            if (node.Depth != parent.Depth + 1)
            {
                throw new SnapshotInvariantException($"Node '{node.FullPath}' has an invalid depth.");
            }
        }

        foreach (var node in nodes.OrderByDescending(n => n.Depth).ThenByDescending(n => n.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.ParentId is null) continue;
            var parent = accumulators[node.ParentId.Value];

            checked
            {
                if (node.NodeType == NodeType.File)
                {
                    parent.DirectFiles++;
                    parent.DescendantFiles++;
                    parent.Logical += node.LogicalBytes;
                    if (node.AllocatedBytes.HasValue) parent.KnownAllocated += node.AllocatedBytes.Value;
                    else
                    {
                        parent.IncompleteAllocated++;
                        if (node.AccountingConfidence == AccountingConfidence.Unsupported) parent.UnsupportedAllocated++;
                    }
                    if (node.UniqueAllocatedBytes.HasValue) parent.KnownUnique += node.UniqueAllocatedBytes.Value;
                    if (!node.UniqueAllocatedBytes.HasValue || !node.PhysicalIdentity.HasValue) parent.IncompleteIdentity++;
                }
                else
                {
                    var child = accumulators[node.Id];
                    parent.DirectDirectories++;
                    parent.DescendantDirectories += 1 + child.DescendantDirectories;
                    parent.DescendantFiles += child.DescendantFiles;
                    parent.Logical += child.Logical;
                    parent.KnownAllocated += child.KnownAllocated;
                    parent.KnownUnique += child.KnownUnique;
                    parent.IncompleteAllocated += child.IncompleteAllocated;
                    parent.IncompleteIdentity += child.IncompleteIdentity;
                    parent.UnsupportedAllocated += child.UnsupportedAllocated;
                }
            }
        }

        return nodes.Select(node =>
        {
            if (node.NodeType == NodeType.File) return node;
            var acc = accumulators[node.Id];
            var aggregate = new DirectoryAggregate(
                acc.DirectFiles,
                acc.DirectDirectories,
                acc.DescendantFiles,
                acc.DescendantDirectories,
                acc.Logical,
                acc.KnownAllocated,
                acc.IncompleteAllocated == 0 ? acc.KnownAllocated : null,
                acc.KnownUnique,
                acc.IncompleteAllocated == 0 && acc.IncompleteIdentity == 0 ? acc.KnownUnique : null,
                acc.IncompleteAllocated,
                acc.IncompleteIdentity);
            var confidence = aggregate.IsUniqueAllocatedComplete
                ? AccountingConfidence.ExactWithinCapturedMetadata
                : acc.UnsupportedAllocated == aggregate.DescendantFileCount && aggregate.DescendantFileCount > 0
                    ? AccountingConfidence.Unsupported
                    : AccountingConfidence.Partial;
            return node with { Aggregate = aggregate, AccountingConfidence = confidence };
        }).OrderBy(n => n.Id).ToArray();
    }
}
