using System.Collections.ObjectModel;

namespace SmartDiskCleaner.Domain;

public readonly record struct PhysicalFileIdentity(string VolumeIdentity, string FileIdentity)
{
    public override string ToString() => $"{VolumeIdentity}:{FileIdentity}";
}

public sealed record RawScanNode(
    long TemporaryId,
    long? ParentTemporaryId,
    int Depth,
    string FullPath,
    string Name,
    NodeType NodeType,
    FileAttributes Attributes,
    long LogicalBytes,
    long? AllocatedBytes,
    PhysicalFileIdentity? PhysicalIdentity,
    uint? LinkCount,
    uint? ReparseTag,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset? LastAccessUtc,
    MetadataFlags MetadataFlags,
    bool IsAccessible = true)
{
    public bool IsReparsePoint => MetadataFlags.HasFlag(MetadataFlags.ReparsePointDetected);
}

public sealed record DirectoryAggregate(
    int DirectFileCount,
    int DirectDirectoryCount,
    int DescendantFileCount,
    int DescendantDirectoryCount,
    long LogicalBytesRecursive,
    long KnownAllocatedBytesRecursive,
    long? AllocatedBytesRecursive,
    long KnownUniqueAllocatedBytesRecursive,
    long? UniqueAllocatedBytesRecursive,
    int IncompleteAllocatedItemCount,
    int IncompleteIdentityItemCount)
{
    public static DirectoryAggregate Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public bool IsAllocatedComplete => IncompleteAllocatedItemCount == 0 && AllocatedBytesRecursive.HasValue;
    public bool IsUniqueAllocatedComplete => IncompleteAllocatedItemCount == 0 && IncompleteIdentityItemCount == 0 && UniqueAllocatedBytesRecursive.HasValue;
}

public sealed record ClassificationResult(
    CleanupCategory Category,
    RiskLevel Risk,
    ConfidenceLevel Confidence,
    RecommendationCode Recommendation,
    string? PrimaryReason,
    IReadOnlyList<string> MatchedRuleIds,
    bool IsProtected,
    long? ReclaimableBytesEstimate,
    EvidenceFlags Evidence,
    string RuleSetVersion)
{
    public static ClassificationResult Unknown(string ruleSetVersion, EvidenceFlags evidence = EvidenceFlags.None) =>
        new(CleanupCategory.Unknown, RiskLevel.High, ConfidenceLevel.None, RecommendationCode.UnknownNoRecommendation,
            null, Array.Empty<string>(), false, null, evidence, ruleSetVersion);
}

public sealed record ScanNode(
    long Id,
    long? ParentId,
    int Depth,
    string FullPath,
    string Name,
    NodeType NodeType,
    FileAttributes Attributes,
    long LogicalBytes,
    long? AllocatedBytes,
    long? UniqueAllocatedBytes,
    PhysicalFileIdentity? PhysicalIdentity,
    uint? LinkCount,
    uint? ReparseTag,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset? LastAccessUtc,
    MetadataFlags MetadataFlags,
    bool IsAccessible,
    AccountingConfidence AccountingConfidence,
    DirectoryAggregate Aggregate,
    ClassificationResult Classification)
{
    public bool IsReparsePoint => MetadataFlags.HasFlag(MetadataFlags.ReparsePointDetected);
    public long RecursiveLogicalBytes => NodeType == NodeType.File ? LogicalBytes : Aggregate.LogicalBytesRecursive;
    public long? RecursiveAllocatedBytes => NodeType == NodeType.File ? AllocatedBytes : Aggregate.AllocatedBytesRecursive;
    public long? RecursiveUniqueAllocatedBytes => NodeType == NodeType.File ? UniqueAllocatedBytes : Aggregate.UniqueAllocatedBytesRecursive;
}

public sealed record ScanWarning(
    long Id,
    Guid SessionId,
    string? Path,
    string Operation,
    string Code,
    string MessageSafe,
    string? ExceptionType,
    DateTimeOffset OccurredAtUtc,
    WarningSeverity Severity,
    bool Recoverable,
    long? NodeId = null);

public sealed record ScanSession(
    Guid Id,
    string VolumeRoot,
    string? VolumeIdentity,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    SessionStatus Status,
    string OptionsVersion,
    string RuleSetVersion,
    string? FailureCode = null);

public sealed record ScanMetrics(
    int NodeCount,
    int FileCount,
    int DirectoryCount,
    int WarningCount,
    long LogicalBytes,
    long KnownAllocatedBytes,
    long? AllocatedBytes,
    long KnownUniqueAllocatedBytes,
    long? UniqueAllocatedBytes,
    AccountingConfidence AccountingConfidence,
    IReadOnlyDictionary<CleanupCategory, long> KnownCandidateAllocatedBytes);

public sealed record PresentationDecision(
    IReadOnlyList<long> VisibleChildIds,
    IReadOnlyList<long> AutoExpandDirectoryIds,
    IReadOnlyList<long> CollapsedDirectoryIds,
    RankingMetricKind RankingMetric,
    bool ThresholdTriggered);

public sealed class ScanSnapshot
{
    public ScanSnapshot(
        ScanSession session,
        IEnumerable<ScanNode> nodes,
        IEnumerable<ScanWarning> warnings,
        ScanMetrics metrics,
        IReadOnlyDictionary<long, PresentationDecision> presentationDecisions)
    {
        Session = session;
        Nodes = Array.AsReadOnly(nodes.OrderBy(n => n.Id).ToArray());
        Warnings = Array.AsReadOnly(warnings.OrderBy(w => w.Path, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Code, StringComparer.Ordinal).ToArray());
        Metrics = metrics;
        NodeById = new ReadOnlyDictionary<long, ScanNode>(Nodes.ToDictionary(n => n.Id));
        ChildrenByParentId = new ReadOnlyDictionary<long, IReadOnlyList<long>>(
            Nodes.GroupBy(n => n.ParentId ?? 0L)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<long>)Array.AsReadOnly(g.OrderBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.FullPath, StringComparer.Ordinal).Select(n => n.Id).ToArray())));
        PresentationDecisions = new ReadOnlyDictionary<long, PresentationDecision>(new Dictionary<long, PresentationDecision>(presentationDecisions));
    }

    public ScanSession Session { get; }
    public IReadOnlyList<ScanNode> Nodes { get; }
    public IReadOnlyList<ScanWarning> Warnings { get; }
    public ScanMetrics Metrics { get; }
    public IReadOnlyDictionary<long, ScanNode> NodeById { get; }
    public IReadOnlyDictionary<long, IReadOnlyList<long>> ChildrenByParentId { get; }
    public IReadOnlyDictionary<long, PresentationDecision> PresentationDecisions { get; }
}

public sealed record ScanRequest(Guid SessionId, string VolumeRoot, string OptionsVersion = "1");

public sealed record ScannerResult(
    VolumeInfo Volume,
    IReadOnlyList<RawScanNode> Nodes,
    IReadOnlyList<ScanWarning> Warnings);

public sealed record ScanProgress(ScanProgressStage Stage, int DiscoveredNodes, int WarningCount, string? CurrentPath = null);

public sealed record ScanPipelineResult(ScanSession Session, ScanSnapshot? Snapshot)
{
    public bool IsCompleted => Session.Status == SessionStatus.Completed && Snapshot is not null;
}

public sealed record VolumeInfo(string RootPath, string VolumeIdentity, string FileSystemName, bool IsReady);

public sealed record FileMetadata(
    string FullPath,
    string Name,
    NodeType NodeType,
    FileAttributes Attributes,
    long LogicalBytes,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset? LastAccessUtc,
    uint? ReparseTag = null);

public readonly record struct MetadataResult<T>(MetadataResultStatus Status, T? Value, string? ErrorCode = null)
{
    public bool IsKnown => Status == MetadataResultStatus.Known;
    public static MetadataResult<T> Known(T value) => new(MetadataResultStatus.Known, value);
    public static MetadataResult<T> Unsupported(string? code = null) => new(MetadataResultStatus.Unsupported, default, code);
    public static MetadataResult<T> Failed(string code) => new(MetadataResultStatus.Failed, default, code);
}

public sealed record PhysicalIdentityMetadata(PhysicalFileIdentity Identity, uint LinkCount);

