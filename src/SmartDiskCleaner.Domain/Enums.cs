namespace SmartDiskCleaner.Domain;

public enum NodeType { File, Directory }
public enum CleanupCategory { Unknown, HighConfidenceReclaimable, ReviewRecommended, LargeFileNotJunk, Protected, Warning }
public enum RiskLevel { Low, Medium, High, Protected }
public enum ConfidenceLevel { None, Low, Medium, High }
public enum AccountingConfidence { ExactWithinCapturedMetadata, Partial, Unsupported }
public enum SessionStatus { Created, Validating, Scanning, Aggregating, Classifying, Persisting, Completed, Cancelling, Cancelled, Failed }
public enum ScanProgressStage { Validating, Scanning, Accounting, Aggregating, Classifying, BuildingSnapshot, Completed, Cancelled }
public enum WarningSeverity { Information, Warning, Error }
public enum ProtectionStrength { Normal, HardProtected, ReservedExcluded }
public enum RecommendationCode
{
    UnknownNoRecommendation,
    ReviewCacheCandidate,
    ReviewTemporaryCandidate,
    ReviewCrashArtifact,
    ReviewRecycleBin,
    ReviewLargePersonalFile,
    ReviewDeveloperCache,
    ReviewGenericCandidate,
    DoNotDeleteDirectly,
    UseWindowsManagedCleanup,
    ExcludedApplicationData
}
public enum RuleScope { File, Directory, Both }
public enum RuleSourceType { BuiltIn, User }
public enum RankingMetricKind { UniqueAllocatedRecursive, AllocatedRecursive, LogicalRecursive }
public enum MetadataResultStatus { Known, Unsupported, Failed }

[Flags]
public enum MetadataFlags
{
    None = 0,
    AllocatedSizeKnown = 1 << 0,
    PhysicalIdentityKnown = 1 << 1,
    ReparsePointDetected = 1 << 2,
    CloudPlaceholderSuspected = 1 << 3,
    MetadataPartial = 1 << 4,
    AccessRestricted = 1 << 5,
    AllocatedSizeUnsupported = 1 << 6,
    PhysicalIdentityUnsupported = 1 << 7
}

[Flags]
public enum EvidenceFlags
{
    None = 0,
    ReparsePointNotFollowed = 1 << 0,
    PartialMetadata = 1 << 1,
    SizeOnlyInformational = 1 << 2,
    ProtectedPath = 1 << 3
}

