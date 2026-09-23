namespace SmartDiskCleaner.Domain;

public sealed record StorageAccountingResult(IReadOnlyList<ScanNode> Nodes, IReadOnlyList<ScanWarning> Warnings);

public sealed class StorageAccountingService
{
    public StorageAccountingResult Resolve(
        Guid sessionId,
        IReadOnlyList<RawScanNode> rawNodes,
        DateTimeOffset warningTimeUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawNodes);
        var warnings = new List<ScanWarning>();
        var identityGroups = rawNodes
            .Where(n => n.NodeType == NodeType.File && n.PhysicalIdentity.HasValue)
            .GroupBy(n => n.PhysicalIdentity!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.TemporaryId).ToArray());

        var groupAllocation = new Dictionary<PhysicalFileIdentity, long?>();
        foreach (var (identity, group) in identityGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = group.Select(n => n.AllocatedBytes).Distinct().ToArray();
            if (values.Length == 1 && values[0].HasValue)
            {
                groupAllocation[identity] = values[0].GetValueOrDefault();
            }
            else
            {
                groupAllocation[identity] = null;
                warnings.Add(new ScanWarning(0, sessionId, group[0].FullPath, "PhysicalAccounting",
                    "PHYSICAL_METADATA_INCONSISTENT", "Hard-link paths reported incomplete or conflicting allocation metadata.",
                    null, warningTimeUtc, WarningSeverity.Warning, true));
            }
        }

        var nodes = new List<ScanNode>(rawNodes.Count);
        foreach (var raw in rawNodes.OrderBy(n => n.TemporaryId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (raw.LogicalBytes < 0 || raw.AllocatedBytes < 0)
            {
                throw new SnapshotInvariantException($"Negative byte count for '{raw.FullPath}'.");
            }

            long? uniqueAllocated;
            AccountingConfidence confidence;
            if (raw.NodeType == NodeType.Directory)
            {
                uniqueAllocated = 0;
                confidence = AccountingConfidence.ExactWithinCapturedMetadata;
            }
            else if (!raw.AllocatedBytes.HasValue)
            {
                uniqueAllocated = null;
                confidence = raw.MetadataFlags.HasFlag(MetadataFlags.AllocatedSizeUnsupported)
                    ? AccountingConfidence.Unsupported
                    : AccountingConfidence.Partial;
            }
            else if (!raw.PhysicalIdentity.HasValue)
            {
                uniqueAllocated = raw.AllocatedBytes;
                confidence = raw.MetadataFlags.HasFlag(MetadataFlags.PhysicalIdentityUnsupported)
                    ? AccountingConfidence.Unsupported
                    : AccountingConfidence.Partial;
            }
            else if (!groupAllocation[raw.PhysicalIdentity.Value].HasValue)
            {
                uniqueAllocated = null;
                confidence = AccountingConfidence.Partial;
            }
            else
            {
                var firstId = identityGroups[raw.PhysicalIdentity.Value][0].TemporaryId;
                uniqueAllocated = raw.TemporaryId == firstId ? groupAllocation[raw.PhysicalIdentity.Value] : 0;
                confidence = AccountingConfidence.ExactWithinCapturedMetadata;
            }

            nodes.Add(new ScanNode(
                raw.TemporaryId, raw.ParentTemporaryId, raw.Depth, raw.FullPath, raw.Name, raw.NodeType,
                raw.Attributes, raw.LogicalBytes, raw.AllocatedBytes, uniqueAllocated, raw.PhysicalIdentity,
                raw.LinkCount, raw.ReparseTag, raw.CreatedUtc, raw.ModifiedUtc, raw.LastAccessUtc,
                raw.MetadataFlags, raw.IsAccessible, confidence, DirectoryAggregate.Empty,
                ClassificationResult.Unknown("unclassified",
                    raw.IsReparsePoint ? EvidenceFlags.ReparsePointNotFollowed : EvidenceFlags.None)));
        }

        return new StorageAccountingResult(nodes, warnings);
    }
}
