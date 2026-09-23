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

public interface IScanSnapshotRepository
{
    Task SaveSnapshotAsync(ScanSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<ScanSnapshot?> LoadSnapshotAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScanSession>> GetRecentSessionsAsync(int maxCount = 20, CancellationToken cancellationToken = default);
    Task<bool> RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

public interface IAppLogger
{
    void LogInformation(string operation, string message, Guid? sessionId = null);
    void LogWarning(string operation, string message, string? path = null, Guid? sessionId = null);
    void LogError(string operation, string message, Exception? exception = null, Guid? sessionId = null);
}

public sealed record AppConfig(
    string Version = "1",
    string SelectedDrive = "C:\\",
    int MaxRecentSnapshots = 10,
    int ChildThreshold = 10,
    double LargestFolderRatio = 0.40,
    long MinSizeFilterBytes = 0);

public interface IAppConfigService
{
    AppConfig Current { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default);
}
