using SmartDiskCleaner.Domain;

namespace SmartDiskCleaner.Domain.Tests;

internal static class TestNodeFactory
{
    public static RawScanNode RawDirectory(long id, long? parentId, int depth, string path, bool reparse = false, bool accessible = true) =>
        new(id, parentId, depth, path, Path.GetFileName(path.TrimEnd('\\')),
            NodeType.Directory, FileAttributes.Directory | (reparse ? FileAttributes.ReparsePoint : 0),
            0, null, null, null, reparse ? 0xA0000003u : null, null, null, null,
            reparse ? MetadataFlags.ReparsePointDetected : MetadataFlags.None, accessible);

    public static RawScanNode RawFile(
        long id,
        long parentId,
        int depth,
        string path,
        long logical,
        long? allocated,
        PhysicalFileIdentity? identity = null,
        uint? linkCount = null,
        DateTimeOffset? modified = null)
    {
        var flags = MetadataFlags.None;
        if (allocated.HasValue) flags |= MetadataFlags.AllocatedSizeKnown;
        else flags |= MetadataFlags.MetadataPartial;
        if (identity.HasValue) flags |= MetadataFlags.PhysicalIdentityKnown;
        else flags |= MetadataFlags.MetadataPartial;
        return new RawScanNode(id, parentId, depth, path, Path.GetFileName(path), NodeType.File,
            FileAttributes.Normal, logical, allocated, identity, linkCount, null, null, modified, null, flags);
    }

    public static ScanNode Directory(
        long id,
        string path,
        long logical,
        long? allocated,
        long? unique,
        long? parentId = 1)
    {
        var aggregate = new DirectoryAggregate(0, 0, 0, 0, logical, allocated ?? 0, allocated,
            unique ?? 0, unique, allocated.HasValue ? 0 : 1, unique.HasValue ? 0 : 1);
        return new ScanNode(id, parentId, 1, path, Path.GetFileName(path), NodeType.Directory,
            FileAttributes.Directory, 0, null, null, null, null, null, null, null, null,
            MetadataFlags.None, true, unique.HasValue ? AccountingConfidence.ExactWithinCapturedMetadata : AccountingConfidence.Partial,
            aggregate, ClassificationResult.Unknown("test"));
    }

    public static ScanNode File(long id, string path, long logical = 1, long? allocated = 1, long? parentId = 1,
        DateTimeOffset? modified = null, FileAttributes attributes = FileAttributes.Normal, bool reparse = false)
    {
        var flags = reparse ? MetadataFlags.ReparsePointDetected : MetadataFlags.None;
        return new ScanNode(id, parentId, 1, path, Path.GetFileName(path), NodeType.File,
            attributes | (reparse ? FileAttributes.ReparsePoint : 0), logical, allocated, allocated,
            allocated.HasValue ? new PhysicalFileIdentity("VOL", id.ToString()) : null, 1, null, null, modified, null,
            flags, true, allocated.HasValue ? AccountingConfidence.ExactWithinCapturedMetadata : AccountingConfidence.Partial,
            DirectoryAggregate.Empty, ClassificationResult.Unknown("test"));
    }
}
