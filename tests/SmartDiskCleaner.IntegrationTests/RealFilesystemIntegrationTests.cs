using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;
using SmartDiskCleaner.Infrastructure;
using SmartDiskCleaner.Rules;
using Xunit;
using Xunit.Sdk;

namespace SmartDiskCleaner.IntegrationTests;

public sealed class RealFilesystemIntegrationTests
{
    [Fact]
    public async Task RealFixtureScan_IsNonMutatingAndPipelineCompletes()
    {
        using var fixture = new TempFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "nested"));
        File.WriteAllText(Path.Combine(fixture.Root, "alpha.txt"), "alpha");
        File.WriteAllBytes(Path.Combine(fixture.Root, "nested", "beta.bin"), [1, 2, 3, 4]);
        var before = CaptureFixture(fixture.Root);

        var scanner = CreateScanner(fixture.Root, Path.Combine(Path.GetTempPath(), "SmartDiskCleaner-Test-AppData-Outside"));
        var rules = BuiltInRuleSetFactory.Create(new ResolvedKnownPaths(fixture.Root, UserTemp: Path.Combine(fixture.Root, "temp")));
        var pipeline = new ScanPipeline(scanner, new StorageAccountingService(), new DirectoryAggregationService(),
            new ClassificationService(rules), new AdaptiveTreePolicy(), new SystemClock());
        var result = await pipeline.RunAsync(fixture.Root, "1", null, CancellationToken.None);
        var after = CaptureFixture(fixture.Root);

        Assert.True(result.IsCompleted);
        Assert.Equal(before, after);
        Assert.Equal(4, result.Snapshot!.Nodes.Count);
        Assert.Equal(2, result.Snapshot.Metrics.FileCount);
        Assert.True(result.Snapshot.Metrics.LogicalBytes >= 9);
    }

    [Fact]
    public async Task RealDirectorySymbolicLink_IsEmittedButTargetIsNotTraversedThroughLink()
    {
        using var fixture = new TempFixture();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "target"));
        File.WriteAllText(Path.Combine(target.FullName, "inside.txt"), "payload");
        var link = Path.Combine(fixture.Root, "link");
        CreateDirectoryLinkOrJunction(link, target.FullName);
        try
        {
            var result = await CreateScanner(fixture.Root, Path.Combine(Path.GetTempPath(), "SmartDiskCleaner-Test-AppData-Outside"))
                .ScanAsync(new ScanRequest(Guid.NewGuid(), fixture.Root), null, CancellationToken.None);

            var linkNode = Assert.Single(result.Nodes, n => PathNormalizer.EqualsPath(n.FullPath, link));
            Assert.True(linkNode.IsReparsePoint);
            var linkedChildPrefix = PathNormalizer.Normalize(link) + Path.DirectorySeparatorChar;
            Assert.DoesNotContain(result.Nodes, n => PathNormalizer.Normalize(n.FullPath).StartsWith(linkedChildPrefix, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Nodes, n => n.FullPath.EndsWith("inside.txt", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link, false);
        }
    }

    [Fact]
    public async Task RealHardLinks_HaveSameIdentityAndUniqueAllocationIsCountedOnce()
    {
        using var fixture = new TempFixture();
        var original = Path.Combine(fixture.Root, "original.bin");
        var linked = Path.Combine(fixture.Root, "linked.bin");
        File.WriteAllBytes(original, Enumerable.Repeat((byte)42, 8192).ToArray());
        if (!CreateHardLink(linked, original, IntPtr.Zero))
            throw SkipException.ForSkip($"Hard-link fixture unavailable: Win32 {Marshal.GetLastPInvokeError()}");

        var identityProvider = new WindowsPhysicalFileIdentityProvider();
        var originalIdentity = identityProvider.GetIdentity(original);
        var linkedIdentity = identityProvider.GetIdentity(linked);
        Assert.True(originalIdentity.IsKnown);
        Assert.True(linkedIdentity.IsKnown);
        Assert.Equal(originalIdentity.Value!.Identity, linkedIdentity.Value!.Identity);
        Assert.True(originalIdentity.Value.LinkCount >= 2);

        var scanned = await CreateScanner(fixture.Root, Path.Combine(Path.GetTempPath(), "SmartDiskCleaner-Test-AppData-Outside"))
            .ScanAsync(new ScanRequest(Guid.NewGuid(), fixture.Root), null, CancellationToken.None);
        var canonical = RawNodeCanonicalizer.Canonicalize(scanned.Nodes);
        var accounted = new StorageAccountingService().Resolve(Guid.NewGuid(), canonical, DateTimeOffset.UtcNow);
        var files = accounted.Nodes.Where(n => n.NodeType == NodeType.File).ToArray();

        Assert.Equal(2, files.Length);
        Assert.Equal(files.Sum(n => n.AllocatedBytes), files.Single(n => n.UniqueAllocatedBytes > 0).UniqueAllocatedBytes * 2);
        Assert.Equal(1, files.Count(n => n.UniqueAllocatedBytes == 0));
    }

    [Fact]
    public void RealSparseFile_ReportsAllocatedBytesBelowLogicalBytes_WhenSupported()
    {
        using var fixture = new TempFixture();
        var path = Path.Combine(fixture.Root, "sparse.bin");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            if (!DeviceIoControl(stream.SafeFileHandle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw SkipException.ForSkip($"Sparse-file fixture unavailable: Win32 {Marshal.GetLastPInvokeError()}");
            stream.SetLength(64L * 1024 * 1024);
            stream.Position = stream.Length - 1;
            stream.WriteByte(1);
        }

        var allocated = new WindowsAllocatedSizeProvider().GetAllocatedSize(path);

        Assert.True(allocated.IsKnown);
        Assert.True(allocated.Value < new FileInfo(path).Length);
    }

    [Fact]
    public async Task LongPath_IsScannedWhenEnvironmentSupportsIt()
    {
        using var fixture = new TempFixture();
        var current = fixture.Root;
        try
        {
            for (var index = 0; index < 12; index++)
            {
                current = Path.Combine(current, $"segment-{index:D2}-abcdefghijklmnop");
                Directory.CreateDirectory(current);
            }

            File.WriteAllText(Path.Combine(current, "long-path.txt"), "long");
        }
        catch (PathTooLongException)
        {
            throw SkipException.ForSkip("Long-path support is disabled in this environment.");
        }

        var result = await CreateScanner(fixture.Root, Path.Combine(Path.GetTempPath(), "SmartDiskCleaner-Test-AppData-Outside"))
            .ScanAsync(new ScanRequest(Guid.NewGuid(), fixture.Root), null, CancellationToken.None);

        Assert.Contains(result.Nodes, n => n.FullPath.EndsWith("long-path.txt", StringComparison.Ordinal));
    }

    private static FileSystemScanner CreateScanner(string root, string appRoot) =>
        new(new WindowsFileMetadataReader(), new WindowsAllocatedSizeProvider(), new WindowsPhysicalFileIdentityProvider(),
            new TestVolumeProvider(root), new TestAppPathProvider(appRoot), new SystemClock());

    private static void CreateDirectoryLinkOrJunction(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/d", "/c", "mklink", "/J", link, target },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            Assert.NotNull(process);
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"Could not create reparse fixture: {process.StandardError.ReadToEnd()}");
        }
    }

    private static string CaptureFixture(string root)
    {
        var entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.Directory))
                    return $"D|{Path.GetRelativePath(root, path)}|{new DirectoryInfo(path).LastWriteTimeUtc.Ticks}";
                var bytes = File.ReadAllBytes(path);
                return $"F|{Path.GetRelativePath(root, path)}|{Convert.ToHexString(SHA256.HashData(bytes))}|{new FileInfo(path).LastWriteTimeUtc.Ticks}";
            });
        return string.Join('\n', entries);
    }

    private sealed class TestVolumeProvider(string root) : IVolumeInfoProvider
    {
        public VolumeInfo ValidateAndGet(string requestedRoot) =>
            new(PathNormalizer.Normalize(root), "TEST-VOLUME", "NTFS", true);
    }

    private sealed class TestAppPathProvider(string root) : IAppPathProvider
    {
        public string AppDataRoot { get; } = root;
    }

    private sealed class TempFixture : IDisposable
    {
        public TempFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "SmartDiskCleanerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private const uint FsctlSetSparse = 0x000900C4;

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
