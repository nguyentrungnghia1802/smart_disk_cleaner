namespace SmartDiskCleaner.Domain;

public static class RawNodeCanonicalizer
{
    public static IReadOnlyList<RawScanNode> Canonicalize(IEnumerable<RawScanNode> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var input = source.ToArray();
        if (input.Select(n => n.TemporaryId).Distinct().Count() != input.Length)
        {
            throw new SnapshotInvariantException("Raw node IDs must be unique.");
        }

        var ordered = input
            .OrderBy(n => PathNormalizer.Normalize(n.FullPath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => PathNormalizer.Normalize(n.FullPath), StringComparer.Ordinal)
            .ThenBy(n => n.NodeType)
            .ToArray();
        var idMap = ordered.Select((node, index) => (node.TemporaryId, Id: (long)index + 1))
            .ToDictionary(x => x.TemporaryId, x => x.Id);

        return ordered.Select(node => node with
        {
            TemporaryId = idMap[node.TemporaryId],
            ParentTemporaryId = node.ParentTemporaryId is null
                ? null
                : idMap.TryGetValue(node.ParentTemporaryId.Value, out var parentId)
                    ? parentId
                    : throw new SnapshotInvariantException($"Missing parent {node.ParentTemporaryId} for '{node.FullPath}'.")
        }).ToArray();
    }
}

