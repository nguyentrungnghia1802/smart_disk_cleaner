namespace SmartDiskCleaner.Domain;

public interface IFileSystemScanner
{
    Task<ScannerResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

public interface IFileMetadataReader
{
    FileMetadata Read(string path);
    IEnumerable<string> EnumerateEntries(string directoryPath);
}

public interface IAllocatedSizeProvider
{
    MetadataResult<long> GetAllocatedSize(string filePath);
}

public interface IPhysicalFileIdentityProvider
{
    MetadataResult<PhysicalIdentityMetadata> GetIdentity(string filePath);
}

public interface IVolumeInfoProvider
{
    VolumeInfo ValidateAndGet(string requestedRoot);
}

public interface IAppPathProvider
{
    string AppDataRoot { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IClassificationService
{
    string RuleSetVersion { get; }
    ClassificationResult Classify(ScanNode node, DateTimeOffset evaluatedAtUtc);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public class ScanValidationException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed class RootAccessException(string message, Exception? innerException = null)
    : ScanValidationException("ROOT_ACCESS_FAILED", message, innerException);

public sealed class SnapshotInvariantException(string message) : Exception(message);
