using SmartDiskCleaner.Domain;
using Xunit;

namespace SmartDiskCleaner.Domain.Tests;

public sealed class StorageAccountingTests
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HardLinks_AreDeduplicatedOnlyInUniqueAllocation()
    {
        var identityX = new PhysicalFileIdentity("VOL", "X");
        var identityY = new PhysicalFileIdentity("VOL", "Y");
        var raw = new[]
        {
            TestNodeFactory.RawDirectory(1, null, 0, @"C:\root"),
            TestNodeFactory.RawFile(2, 1, 1, @"C:\root\A.bin", 100, 128, identityX, 2),
            TestNodeFactory.RawFile(3, 1, 1, @"C:\root\hardlink-A.bin", 100, 128, identityX, 2),
            TestNodeFactory.RawFile(4, 1, 1, @"C:\root\B.bin", 50, 64, identityY, 1)
        };

        var accounted = new StorageAccountingService().Resolve(SessionId, raw, Now);
        var nodes = new DirectoryAggregationService().Aggregate(accounted.Nodes);
        var root = nodes.Single(n => n.Id == 1);

        Assert.Equal(250, root.Aggregate.LogicalBytesRecursive);
        Assert.Equal(320, root.Aggregate.AllocatedBytesRecursive);
        Assert.Equal(192, root.Aggregate.UniqueAllocatedBytesRecursive);
        Assert.Equal(new long?[] { 128, 0, 64 }, nodes.Where(n => n.NodeType == NodeType.File).Select(n => n.UniqueAllocatedBytes));
        Assert.Equal(AccountingConfidence.ExactWithinCapturedMetadata, root.AccountingConfidence);
    }

    [Fact]
    public void UnknownAllocated_RemainsNullAndPropagatesPartial()
    {
        var raw = new[]
        {
            TestNodeFactory.RawDirectory(1, null, 0, @"C:\root"),
            TestNodeFactory.RawFile(2, 1, 1, @"C:\root\unknown.bin", 10, null)
        };

        var nodes = new DirectoryAggregationService().Aggregate(new StorageAccountingService().Resolve(SessionId, raw, Now).Nodes);
        var file = nodes.Single(n => n.NodeType == NodeType.File);
        var root = nodes.Single(n => n.NodeType == NodeType.Directory);

        Assert.Null(file.AllocatedBytes);
        Assert.Null(file.UniqueAllocatedBytes);
        Assert.Null(root.Aggregate.AllocatedBytesRecursive);
        Assert.Null(root.Aggregate.UniqueAllocatedBytesRecursive);
        Assert.Equal(1, root.Aggregate.IncompleteAllocatedItemCount);
        Assert.Equal(AccountingConfidence.Partial, root.AccountingConfidence);
    }

    [Fact]
    public void UnknownIdentity_UsesConservativePathAllocationButNeverClaimsExactUniqueTotal()
    {
        var raw = new[]
        {
            TestNodeFactory.RawDirectory(1, null, 0, @"C:\root"),
            TestNodeFactory.RawFile(2, 1, 1, @"C:\root\identity-unknown.bin", 10, 16)
        };

        var nodes = new DirectoryAggregationService().Aggregate(new StorageAccountingService().Resolve(SessionId, raw, Now).Nodes);
        var file = nodes.Single(n => n.NodeType == NodeType.File);
        var root = nodes.Single(n => n.NodeType == NodeType.Directory);

        Assert.Equal(16, file.UniqueAllocatedBytes);
        Assert.Equal(AccountingConfidence.Partial, file.AccountingConfidence);
        Assert.Equal(16, root.Aggregate.KnownUniqueAllocatedBytesRecursive);
        Assert.Null(root.Aggregate.UniqueAllocatedBytesRecursive);
        Assert.Equal(1, root.Aggregate.IncompleteIdentityItemCount);
    }

    [Fact]
    public void NestedAggregation_ComputesDirectAndDescendantCounts()
    {
        var identityA = new PhysicalFileIdentity("VOL", "A");
        var identityB = new PhysicalFileIdentity("VOL", "B");
        var raw = new[]
        {
            TestNodeFactory.RawDirectory(1, null, 0, @"C:\root"),
            TestNodeFactory.RawDirectory(2, 1, 1, @"C:\root\child"),
            TestNodeFactory.RawFile(3, 1, 1, @"C:\root\top.bin", 3, 4, identityA),
            TestNodeFactory.RawFile(4, 2, 2, @"C:\root\child\deep.bin", 5, 8, identityB)
        };

        var nodes = new DirectoryAggregationService().Aggregate(new StorageAccountingService().Resolve(SessionId, raw, Now).Nodes);
        var root = nodes.Single(n => n.Id == 1);
        var child = nodes.Single(n => n.Id == 2);

        Assert.Equal((1, 1, 2, 1), (root.Aggregate.DirectFileCount, root.Aggregate.DirectDirectoryCount,
            root.Aggregate.DescendantFileCount, root.Aggregate.DescendantDirectoryCount));
        Assert.Equal((1, 0, 1, 0), (child.Aggregate.DirectFileCount, child.Aggregate.DirectDirectoryCount,
            child.Aggregate.DescendantFileCount, child.Aggregate.DescendantDirectoryCount));
        Assert.Equal(8, root.Aggregate.LogicalBytesRecursive);
    }

    [Fact]
    public void SparseLikeMetadata_PreservesLogicalAndAllocatedDistinction()
    {
        var raw = new[]
        {
            TestNodeFactory.RawDirectory(1, null, 0, @"C:\root"),
            TestNodeFactory.RawFile(2, 1, 1, @"C:\root\sparse.bin", 1L << 40, 4096,
                new PhysicalFileIdentity("VOL", "SPARSE"))
        };

        var node = new StorageAccountingService().Resolve(SessionId, raw, Now).Nodes.Single(n => n.NodeType == NodeType.File);
        Assert.Equal(1L << 40, node.LogicalBytes);
        Assert.Equal(4096, node.AllocatedBytes);
        Assert.Equal(4096, node.UniqueAllocatedBytes);
    }

    [Fact]
    public void ConflictingHardLinkAllocation_IsPartialAndWarned()
    {
        var identity = new PhysicalFileIdentity("VOL", "X");
        var raw = new[]
        {
            TestNodeFactory.RawFile(1, 10, 1, @"C:\root\a", 10, 16, identity),
            TestNodeFactory.RawFile(2, 10, 1, @"C:\root\b", 10, 32, identity)
        };

        var result = new StorageAccountingService().Resolve(SessionId, raw, Now);
        Assert.All(result.Nodes, node => Assert.Null(node.UniqueAllocatedBytes));
        Assert.Contains(result.Warnings, warning => warning.Code == "PHYSICAL_METADATA_INCONSISTENT");
    }

    [Fact]
    public void Aggregation_ThrowsInsteadOfWrappingOnOverflow()
    {
        var raw = new[]
        {
            TestNodeFactory.RawDirectory(1, null, 0, @"C:\root"),
            TestNodeFactory.RawFile(2, 1, 1, @"C:\root\a", long.MaxValue, long.MaxValue, new PhysicalFileIdentity("V", "A")),
            TestNodeFactory.RawFile(3, 1, 1, @"C:\root\b", 1, 1, new PhysicalFileIdentity("V", "B"))
        };
        var accounted = new StorageAccountingService().Resolve(SessionId, raw, Now);
        Assert.Throws<OverflowException>(() => new DirectoryAggregationService().Aggregate(accounted.Nodes));
    }

    [Fact]
    public void Canonicalizer_AssignsSameIdsRegardlessOfDiscoveryOrder()
    {
        var root = TestNodeFactory.RawDirectory(90, null, 0, @"C:\root");
        var a = TestNodeFactory.RawFile(7, 90, 1, @"C:\root\a.bin", 1, 1, new PhysicalFileIdentity("V", "A"));
        var b = TestNodeFactory.RawFile(2, 90, 1, @"C:\root\b.bin", 1, 1, new PhysicalFileIdentity("V", "B"));

        var first = RawNodeCanonicalizer.Canonicalize([b, root, a]);
        var second = RawNodeCanonicalizer.Canonicalize([a, b, root]);

        Assert.Equal(first.Select(n => (n.TemporaryId, n.ParentTemporaryId, n.FullPath)),
            second.Select(n => (n.TemporaryId, n.ParentTemporaryId, n.FullPath)));
    }

    [Fact]
    public void Aggregation_RejectsInconsistentParentGraph()
    {
        var orphan = new ScanNode(2, 99, 1, @"C:\root\orphan.bin", "orphan.bin", NodeType.File,
            FileAttributes.Normal, 1, 1, 1, new PhysicalFileIdentity("V", "F"), 1, null, null, null, null,
            MetadataFlags.AllocatedSizeKnown | MetadataFlags.PhysicalIdentityKnown, true,
            AccountingConfidence.ExactWithinCapturedMetadata, DirectoryAggregate.Empty, ClassificationResult.Unknown("test"));

        Assert.Throws<SnapshotInvariantException>(() => new DirectoryAggregationService().Aggregate([orphan]));
    }
}
