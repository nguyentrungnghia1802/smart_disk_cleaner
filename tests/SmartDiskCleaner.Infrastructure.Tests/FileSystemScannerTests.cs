using SmartDiskCleaner.Domain;
using SmartDiskCleaner.Infrastructure;
using Xunit;

namespace SmartDiskCleaner.Infrastructure.Tests;

public sealed class FileSystemScannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReparseDirectory_IsEmittedButNeverEnumerated()
    {
        var reader = new FakeMetadataReader(@"C:\root")
            .AddDirectory(@"C:\root\link", reparse: true)
            .AddFile(@"C:\root\normal.bin", 5);
        reader.ThrowIfEnumerated.Add(PathNormalizer.Normalize(@"C:\root\link"));
        var scanner = CreateScanner(reader);

        var result = await scanner.ScanAsync(new ScanRequest(Guid.NewGuid(), @"C:\root"), null, CancellationToken.None);

        var link = Assert.Single(result.Nodes, n => n.FullPath.EndsWith("link", StringComparison.Ordinal));
        Assert.True(link.IsReparsePoint);
        Assert.DoesNotContain(PathNormalizer.Normalize(@"C:\root\link"), reader.EnumeratedDirectories);
    }

    [Fact]
    public async Task InaccessibleBranch_BecomesWarningAndDoesNotAbortSiblings()
    {
        var reader = new FakeMetadataReader(@"C:\root")
            .AddDirectory(@"C:\root\blocked")
            .AddFile(@"C:\root\visible.bin", 7);
        reader.ThrowIfEnumerated.Add(PathNormalizer.Normalize(@"C:\root\blocked"));
        var result = await CreateScanner(reader).ScanAsync(new ScanRequest(Guid.NewGuid(), @"C:\root"), null, CancellationToken.None);

        Assert.Contains(result.Nodes, node => node.FullPath.EndsWith("visible.bin", StringComparison.Ordinal));
        Assert.False(result.Nodes.Single(node => node.FullPath.EndsWith("blocked", StringComparison.Ordinal)).IsAccessible);
        Assert.Contains(result.Warnings, warning => warning.Code == "ACCESS_DENIED");
    }

    [Fact]
    public async Task VanishedEntry_BecomesWarningAndOtherEntriesContinue()
    {
        var reader = new FakeMetadataReader(@"C:\root")
            .AddFile(@"C:\root\gone.bin", 1)
            .AddFile(@"C:\root\kept.bin", 2);
        reader.ThrowIfRead.Add(PathNormalizer.Normalize(@"C:\root\gone.bin"));

        var result = await CreateScanner(reader).ScanAsync(new ScanRequest(Guid.NewGuid(), @"C:\root"), null, CancellationToken.None);

        Assert.Contains(result.Nodes, node => node.FullPath.EndsWith("kept.bin", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Nodes, node => node.FullPath.EndsWith("gone.bin", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Code == "PATH_VANISHED");
    }

    [Fact]
    public async Task ExactApplicationDataRoot_IsExcludedWithoutBroadeningToAppDataParent()
    {
        var reader = new FakeMetadataReader(@"C:\root")
            .AddDirectory(@"C:\root\AppData")
            .AddDirectory(@"C:\root\AppData\SmartDiskCleaner")
            .AddFile(@"C:\root\AppData\SmartDiskCleaner\database.db", 100)
            .AddFile(@"C:\root\AppData\other.txt", 3);
        var scanner = CreateScanner(reader, @"C:\root\AppData\SmartDiskCleaner");

        var result = await scanner.ScanAsync(new ScanRequest(Guid.NewGuid(), @"C:\root"), null, CancellationToken.None);

        Assert.Contains(result.Nodes, node => node.FullPath.EndsWith("other.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Nodes, node => PathNormalizer.IsWithinOrEqual(node.FullPath, @"C:\root\AppData\SmartDiskCleaner"));
        Assert.Contains(result.Warnings, warning => warning.Code == "APP_DATA_EXCLUDED");
    }

    [Fact]
    public async Task NativeMetadataFailure_IsExplicitlyPartialNotZero()
    {
        var reader = new FakeMetadataReader(@"C:\root").AddFile(@"C:\root\x.bin", 10);
        var scanner = new FileSystemScanner(reader, new FailedAllocatedProvider(), new UnsupportedIdentityProvider(),
            new FakeVolumeProvider(@"C:\root"), new FakeAppPathProvider(@"D:\app"), new FixedClock());

        var result = await scanner.ScanAsync(new ScanRequest(Guid.NewGuid(), @"C:\root"), null, CancellationToken.None);
        var file = result.Nodes.Single(n => n.NodeType == NodeType.File);

        Assert.Null(file.AllocatedBytes);
        Assert.Null(file.PhysicalIdentity);
        Assert.True(file.MetadataFlags.HasFlag(MetadataFlags.MetadataPartial));
        Assert.True(file.MetadataFlags.HasFlag(MetadataFlags.PhysicalIdentityUnsupported));
        Assert.Contains(result.Warnings, warning => warning.Operation == "AllocatedSize");
    }

    [Fact]
    public async Task FileReparsePoint_DoesNotQueryTargetAccountingProviders()
    {
        var reader = new FakeMetadataReader(@"C:\root").AddFile(@"C:\root\file-link", 999, reparse: true);
        var scanner = new FileSystemScanner(reader, new ThrowingAllocatedProvider(), new ThrowingIdentityProvider(),
            new FakeVolumeProvider(@"C:\root"), new FakeAppPathProvider(@"D:\app"), new FixedClock());

        var result = await scanner.ScanAsync(new ScanRequest(Guid.NewGuid(), @"C:\root"), null, CancellationToken.None);
        var link = Assert.Single(result.Nodes, node => node.NodeType == NodeType.File);

        Assert.True(link.IsReparsePoint);
        Assert.Null(link.AllocatedBytes);
        Assert.Null(link.PhysicalIdentity);
        Assert.True(link.MetadataFlags.HasFlag(MetadataFlags.MetadataPartial));
    }

    [Fact]
    public async Task Cancellation_StopsTraversalCleanly()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new FakeMetadataReader(@"C:\root");
        for (var index = 0; index < 100; index++) reader.AddFile($@"C:\root\f{index}.bin", index);
        reader.OnRead = path =>
        {
            if (path.EndsWith("f2.bin", StringComparison.Ordinal)) cancellation.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CreateScanner(reader).ScanAsync(new ScanRequest(Guid.NewGuid(), @"C:\root"), null, cancellation.Token));
    }

    [Fact]
    public async Task RootEnumerationFailure_IsSessionFatal()
    {
        var reader = new FakeMetadataReader(@"C:\root");
        reader.ThrowIfEnumerated.Add(PathNormalizer.Normalize(@"C:\root"));

        await Assert.ThrowsAsync<RootAccessException>(async () =>
            await CreateScanner(reader).ScanAsync(new ScanRequest(Guid.NewGuid(), @"C:\root"), null, CancellationToken.None));
    }

    [Fact]
    public void ProductionVolumeValidation_RejectsNonDriveAndUnsupportedRootsBeforeTraversal()
    {
        var provider = new WindowsVolumeInfoProvider();

        var nonRoot = Assert.Throws<ScanValidationException>(() => provider.ValidateAndGet(@"C:\folder"));
        var unsupported = Assert.Throws<ScanValidationException>(() => provider.ValidateAndGet(@"E:\"));

        Assert.Equal("ROOT_NOT_DRIVE", nonRoot.Code);
        Assert.Equal("DRIVE_NOT_SUPPORTED", unsupported.Code);
    }

    private static FileSystemScanner CreateScanner(FakeMetadataReader reader, string appRoot = @"D:\app") =>
        new(reader, new KnownAllocatedProvider(), new KnownIdentityProvider(),
            new FakeVolumeProvider(@"C:\root"), new FakeAppPathProvider(appRoot), new FixedClock());

    private sealed class FakeMetadataReader : IFileMetadataReader
    {
        private readonly Dictionary<string, FileMetadata> _metadata = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _children = new(StringComparer.OrdinalIgnoreCase);

        public FakeMetadataReader(string root)
        {
            var normalized = PathNormalizer.Normalize(root);
            _metadata[normalized] = Metadata(normalized, NodeType.Directory, 0, false);
            _children[normalized] = [];
        }

        public HashSet<string> ThrowIfEnumerated { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ThrowIfRead { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> EnumeratedDirectories { get; } = [];
        public Action<string>? OnRead { get; set; }

        public FakeMetadataReader AddDirectory(string path, bool reparse = false)
        {
            Add(path, NodeType.Directory, 0, reparse);
            _children[PathNormalizer.Normalize(path)] = [];
            return this;
        }

        public FakeMetadataReader AddFile(string path, long length, bool reparse = false)
        {
            Add(path, NodeType.File, length, reparse);
            return this;
        }

        public FileMetadata Read(string path)
        {
            var normalized = PathNormalizer.Normalize(path);
            OnRead?.Invoke(normalized);
            if (ThrowIfRead.Contains(normalized)) throw new FileNotFoundException("Injected disappearance", normalized);
            return _metadata[normalized];
        }

        public IEnumerable<string> EnumerateEntries(string directoryPath)
        {
            var normalized = PathNormalizer.Normalize(directoryPath);
            EnumeratedDirectories.Add(normalized);
            if (ThrowIfEnumerated.Contains(normalized)) return ThrowAccessDenied();
            return _children[normalized].ToArray();
        }

        private void Add(string path, NodeType type, long length, bool reparse)
        {
            var normalized = PathNormalizer.Normalize(path);
            var parent = Path.GetDirectoryName(normalized) ?? throw new InvalidOperationException();
            _metadata[normalized] = Metadata(normalized, type, length, reparse);
            _children[parent].Add(normalized);
        }

        private static FileMetadata Metadata(string path, NodeType type, long length, bool reparse)
        {
            var attributes = type == NodeType.Directory ? FileAttributes.Directory : FileAttributes.Normal;
            if (reparse) attributes |= FileAttributes.ReparsePoint;
            return new FileMetadata(path, Path.GetFileName(path), type, attributes, length, Now, Now, Now,
                reparse ? 0xA0000003u : null);
        }

        private static IEnumerable<string> ThrowAccessDenied()
        {
            throw new UnauthorizedAccessException("Injected access denial");
        }
    }

    private sealed class KnownAllocatedProvider : IAllocatedSizeProvider
    {
        public MetadataResult<long> GetAllocatedSize(string filePath) => MetadataResult<long>.Known(8);
    }

    private sealed class FailedAllocatedProvider : IAllocatedSizeProvider
    {
        public MetadataResult<long> GetAllocatedSize(string filePath) => MetadataResult<long>.Failed("INJECTED");
    }

    private sealed class KnownIdentityProvider : IPhysicalFileIdentityProvider
    {
        public MetadataResult<PhysicalIdentityMetadata> GetIdentity(string filePath) =>
            MetadataResult<PhysicalIdentityMetadata>.Known(new PhysicalIdentityMetadata(
                new PhysicalFileIdentity("VOL", PathNormalizer.Normalize(filePath)), 1));
    }

    private sealed class UnsupportedIdentityProvider : IPhysicalFileIdentityProvider
    {
        public MetadataResult<PhysicalIdentityMetadata> GetIdentity(string filePath) =>
            MetadataResult<PhysicalIdentityMetadata>.Unsupported("INJECTED");
    }

    private sealed class ThrowingAllocatedProvider : IAllocatedSizeProvider
    {
        public MetadataResult<long> GetAllocatedSize(string filePath) => throw new InvalidOperationException("Must not follow a file reparse point.");
    }

    private sealed class ThrowingIdentityProvider : IPhysicalFileIdentityProvider
    {
        public MetadataResult<PhysicalIdentityMetadata> GetIdentity(string filePath) => throw new InvalidOperationException("Must not follow a file reparse point.");
    }

    private sealed class FakeVolumeProvider(string root) : IVolumeInfoProvider
    {
        public VolumeInfo ValidateAndGet(string requestedRoot) => new(PathNormalizer.Normalize(root), "VOL", "NTFS", true);
    }

    private sealed class FakeAppPathProvider(string root) : IAppPathProvider
    {
        public string AppDataRoot { get; } = root;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
