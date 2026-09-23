using System.Security;
using SmartDiskCleaner.Domain;

namespace SmartDiskCleaner.Infrastructure;

public sealed class FileSystemScanner : IFileSystemScanner
{
    private readonly IFileMetadataReader _metadataReader;
    private readonly IAllocatedSizeProvider _allocatedSizeProvider;
    private readonly IPhysicalFileIdentityProvider _identityProvider;
    private readonly IVolumeInfoProvider _volumeInfoProvider;
    private readonly IAppPathProvider _appPathProvider;
    private readonly IClock _clock;

    public FileSystemScanner(
        IFileMetadataReader metadataReader,
        IAllocatedSizeProvider allocatedSizeProvider,
        IPhysicalFileIdentityProvider identityProvider,
        IVolumeInfoProvider volumeInfoProvider,
        IAppPathProvider appPathProvider,
        IClock clock)
    {
        _metadataReader = metadataReader;
        _allocatedSizeProvider = allocatedSizeProvider;
        _identityProvider = identityProvider;
        _volumeInfoProvider = volumeInfoProvider;
        _appPathProvider = appPathProvider;
        _clock = clock;
    }

    public Task<ScannerResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(request, progress, cancellationToken), cancellationToken);

    private ScannerResult Scan(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var volume = _volumeInfoProvider.ValidateAndGet(request.VolumeRoot);
        var root = PathNormalizer.Normalize(volume.RootPath);
        var appRoot = PathNormalizer.Normalize(_appPathProvider.AppDataRoot);
        var excludeAppRoot = PathNormalizer.IsWithinOrEqual(appRoot, root) && !PathNormalizer.EqualsPath(appRoot, root);

        FileMetadata rootMetadata;
        try
        {
            rootMetadata = _metadataReader.Read(root);
            if (rootMetadata.NodeType != NodeType.Directory) throw new RootAccessException("The scan root is not a directory.");
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            throw new RootAccessException("The selected root cannot be inspected.", exception);
        }

        var nodes = new List<RawScanNode>();
        var warnings = new List<ScanWarning>();
        var nodeIndexes = new Dictionary<long, int>();
        var pending = new Queue<(long Id, string Path, int Depth)>();
        long nextNodeId = 1;
        long nextWarningId = 1;

        var rootNode = ToRawNode(nextNodeId++, null, 0, rootMetadata, request.SessionId, warnings, ref nextWarningId);
        nodeIndexes[rootNode.TemporaryId] = nodes.Count;
        nodes.Add(rootNode);
        pending.Enqueue((rootNode.TemporaryId, root, 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Dequeue();
            IEnumerator<string>? enumerator = null;
            try
            {
                try
                {
                    enumerator = _metadataReader.EnumerateEntries(current.Path).GetEnumerator();
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    MarkInaccessible(nodes, nodeIndexes[current.Id]);
                    AddWarning(warnings, ref nextWarningId, request.SessionId, current.Path, "EnumerateDirectory", exception);
                    if (current.Id == rootNode.TemporaryId)
                        throw new RootAccessException("The selected root cannot be enumerated.", exception);
                    continue;
                }

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool moved;
                    try
                    {
                        moved = enumerator.MoveNext();
                    }
                    catch (Exception exception) when (IsRecoverable(exception))
                    {
                        MarkInaccessible(nodes, nodeIndexes[current.Id]);
                        AddWarning(warnings, ref nextWarningId, request.SessionId, current.Path, "EnumerateDirectory", exception);
                        if (current.Id == rootNode.TemporaryId)
                            throw new RootAccessException("The selected root cannot be enumerated.", exception);
                        break;
                    }

                    if (!moved) break;
                    var entry = enumerator.Current;
                    if (excludeAppRoot && PathNormalizer.EqualsPath(entry, appRoot))
                    {
                        warnings.Add(new ScanWarning(nextWarningId++, request.SessionId, appRoot, "ExcludeApplicationData",
                            "APP_DATA_EXCLUDED", "Application-owned data was intentionally excluded from analysis.", null,
                            _clock.UtcNow, WarningSeverity.Information, true));
                        continue;
                    }

                    try
                    {
                        var metadata = _metadataReader.Read(entry);
                        var raw = ToRawNode(nextNodeId++, current.Id, current.Depth + 1, metadata, request.SessionId, warnings, ref nextWarningId);
                        nodeIndexes[raw.TemporaryId] = nodes.Count;
                        nodes.Add(raw);
                        if (raw.NodeType == NodeType.Directory && !raw.IsReparsePoint)
                            pending.Enqueue((raw.TemporaryId, raw.FullPath, raw.Depth));
                    }
                    catch (Exception exception) when (IsRecoverable(exception))
                    {
                        AddWarning(warnings, ref nextWarningId, request.SessionId, entry, "ReadMetadata", exception);
                    }

                    if ((nodes.Count & 0xFF) == 0)
                        progress?.Report(new ScanProgress(ScanProgressStage.Scanning, nodes.Count, warnings.Count, current.Path));
                }
            }
            finally
            {
                enumerator?.Dispose();
            }
        }

        progress?.Report(new ScanProgress(ScanProgressStage.Scanning, nodes.Count, warnings.Count));
        return new ScannerResult(volume, nodes, warnings);
    }

    private RawScanNode ToRawNode(
        long id,
        long? parentId,
        int depth,
        FileMetadata metadata,
        Guid sessionId,
        List<ScanWarning> warnings,
        ref long nextWarningId)
    {
        var flags = MetadataFlags.None;
        if (metadata.Attributes.HasFlag(FileAttributes.ReparsePoint))
            flags |= MetadataFlags.ReparsePointDetected | MetadataFlags.MetadataPartial;
        long? allocated = null;
        PhysicalFileIdentity? identity = null;
        uint? linkCount = null;

        if (metadata.NodeType == NodeType.File && !flags.HasFlag(MetadataFlags.ReparsePointDetected))
        {
            var allocation = _allocatedSizeProvider.GetAllocatedSize(metadata.FullPath);
            if (allocation.IsKnown)
            {
                allocated = allocation.Value;
                flags |= MetadataFlags.AllocatedSizeKnown;
            }
            else
            {
                flags |= MetadataFlags.MetadataPartial;
                if (allocation.Status == MetadataResultStatus.Unsupported) flags |= MetadataFlags.AllocatedSizeUnsupported;
                if (allocation.Status == MetadataResultStatus.Failed)
                    AddMetadataWarning(warnings, ref nextWarningId, sessionId, metadata.FullPath, "AllocatedSize", allocation.ErrorCode);
            }

            var physical = _identityProvider.GetIdentity(metadata.FullPath);
            if (physical.IsKnown && physical.Value is not null)
            {
                identity = physical.Value.Identity;
                linkCount = physical.Value.LinkCount;
                flags |= MetadataFlags.PhysicalIdentityKnown;
            }
            else
            {
                flags |= MetadataFlags.MetadataPartial;
                if (physical.Status == MetadataResultStatus.Unsupported) flags |= MetadataFlags.PhysicalIdentityUnsupported;
                if (physical.Status == MetadataResultStatus.Failed)
                    AddMetadataWarning(warnings, ref nextWarningId, sessionId, metadata.FullPath, "PhysicalIdentity", physical.ErrorCode);
            }
        }

        return new RawScanNode(id, parentId, depth, metadata.FullPath, metadata.Name, metadata.NodeType,
            metadata.Attributes, metadata.LogicalBytes, allocated, identity, linkCount, metadata.ReparseTag,
            metadata.CreatedUtc, metadata.ModifiedUtc, metadata.LastAccessUtc, flags);
    }

    private void AddMetadataWarning(
        List<ScanWarning> warnings,
        ref long nextWarningId,
        Guid sessionId,
        string path,
        string operation,
        string? code)
    {
        warnings.Add(new ScanWarning(nextWarningId++, sessionId, path, operation, code ?? "NATIVE_METADATA_FAILED",
            "Optional native filesystem metadata could not be read; accounting is partial.", null,
            _clock.UtcNow, WarningSeverity.Warning, true));
    }

    private void AddWarning(List<ScanWarning> warnings, ref long nextWarningId, Guid sessionId, string path, string operation, Exception exception)
    {
        warnings.Add(new ScanWarning(nextWarningId++, sessionId, path, operation, WarningCode(exception),
            "A filesystem entry could not be inspected; the scan continued with partial coverage.", exception.GetType().Name,
            _clock.UtcNow, WarningSeverity.Warning, true));
    }

    private static void MarkInaccessible(List<RawScanNode> nodes, int index)
    {
        var node = nodes[index];
        nodes[index] = node with
        {
            IsAccessible = false,
            MetadataFlags = node.MetadataFlags | MetadataFlags.AccessRestricted | MetadataFlags.MetadataPartial
        };
    }

    private static string WarningCode(Exception exception) => exception switch
    {
        UnauthorizedAccessException or SecurityException => "ACCESS_DENIED",
        DirectoryNotFoundException or FileNotFoundException => "PATH_VANISHED",
        IOException => "IO_ERROR",
        _ => "METADATA_ERROR"
    };

    private static bool IsRecoverable(Exception exception) => exception is
        UnauthorizedAccessException or SecurityException or DirectoryNotFoundException or FileNotFoundException or IOException;
}
