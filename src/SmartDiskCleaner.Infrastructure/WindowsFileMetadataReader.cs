using SmartDiskCleaner.Domain;

namespace SmartDiskCleaner.Infrastructure;

public sealed class WindowsFileMetadataReader : IFileMetadataReader
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
        MatchCasing = MatchCasing.PlatformDefault,
        MatchType = MatchType.Simple
    };

    public IEnumerable<string> EnumerateEntries(string directoryPath) =>
        Directory.EnumerateFileSystemEntries(directoryPath, "*", EnumerationOptions);

    public FileMetadata Read(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = attributes.HasFlag(FileAttributes.Directory);
        var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);
        FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        info.Refresh();
        // A file symlink's Length can reflect its target. V1 records the link object only.
        var length = isDirectory || isReparsePoint ? 0 : ((FileInfo)info).Length;
        return new FileMetadata(
            PathNormalizer.Normalize(path),
            info.Name,
            isDirectory ? NodeType.Directory : NodeType.File,
            attributes,
            length,
            ToUtc(info.CreationTimeUtc),
            ToUtc(info.LastWriteTimeUtc),
            ToUtc(info.LastAccessTimeUtc));
    }

    private static DateTimeOffset? ToUtc(DateTime value) =>
        value == DateTime.MinValue ? null : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
