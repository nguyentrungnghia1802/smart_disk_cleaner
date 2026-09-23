using SmartDiskCleaner.Domain;

namespace SmartDiskCleaner.Rules;

public sealed record ResolvedKnownPaths(
    string VolumeRoot,
    string? WindowsDirectory = null,
    string? SystemDirectory = null,
    string? UserProfile = null,
    string? LocalAppData = null,
    string? RoamingAppData = null,
    string? UserTemp = null,
    string? ApplicationDataRoot = null);

public static class BuiltInRuleSetFactory
{
    public const string Version = "1.0.0";
    private const long LargeFileThreshold = 1024L * 1024L * 1024L;

    public static RuleSet Create(ResolvedKnownPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var rules = new List<RuleDefinition>();

        AddWindowsProtectionRules(rules, paths);
        AddRootProtectionRules(rules, paths);
        AddTemporaryRules(rules, paths);
        AddCacheRules(rules, paths);
        AddReviewRules(rules, paths);

        rules.Add(Rule(
            "DEV-NODE-MODULES-001", 500, RuleScope.Directory,
            new RuleConditionSet { AnyPathSegments = ["node_modules"], IsReparsePoint = false },
            new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.Medium,
                RecommendationCode.ReviewDeveloperCache, UseAllocatedReclaimEstimate: true),
            "Dependency material may be regeneratable but can contain local state.", "DeveloperDependencyReview"));

        rules.Add(Rule(
            "GENERIC-TMP-REVIEW-001", 250, RuleScope.File,
            new RuleConditionSet { Extension = ".tmp", IsReparsePoint = false },
            new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.Low,
                RecommendationCode.ReviewGenericCandidate, UseAllocatedReclaimEstimate: true),
            "A .tmp extension is only a weak cleanup signal outside a recognized temporary root.", "GenericTemporaryExtension"));

        rules.Add(Rule(
            "GENERIC-OLD-LOG-REVIEW-001", 240, RuleScope.File,
            new RuleConditionSet { Extension = ".log", MinimumModifiedAge = TimeSpan.FromDays(30), IsReparsePoint = false },
            new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.Low,
                RecommendationCode.ReviewGenericCandidate, UseAllocatedReclaimEstimate: true),
            "An old log is a review signal only; age does not prove it is disposable.", "OldLogReview"));

        rules.Add(Rule(
            "LARGE-FILE-INFO-001", 100, RuleScope.File,
            new RuleConditionSet { MinimumLogicalBytes = LargeFileThreshold },
            new RuleAction(CleanupCategory.LargeFileNotJunk, RiskLevel.High, ConfidenceLevel.High,
                RecommendationCode.ReviewLargePersonalFile, Evidence: EvidenceFlags.SizeOnlyInformational),
            "The file is large, but size alone is not evidence that it is junk.", "LargeFileInformational"));

        return new RuleSet(Version, rules);
    }

    public static ResolvedKnownPaths ResolveCurrent(string volumeRoot, string applicationDataRoot)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var temp = Path.GetTempPath();
        return new ResolvedKnownPaths(volumeRoot,
            EmptyToNull(windows), EmptyToNull(system), EmptyToNull(profile), EmptyToNull(local), EmptyToNull(roaming),
            EmptyToNull(temp), EmptyToNull(applicationDataRoot));
    }

    private static void AddWindowsProtectionRules(List<RuleDefinition> rules, ResolvedKnownPaths paths)
    {
        if (paths.WindowsDirectory is not null)
        {
            AddProtection(rules, "PROTECT-WINDOWS-WINSXS-001", Path.Combine(paths.WindowsDirectory, "WinSxS"), false,
                RecommendationCode.UseWindowsManagedCleanup, "WindowsComponentStore");
            AddProtection(rules, "PROTECT-WINDOWS-INSTALLER-001", Path.Combine(paths.WindowsDirectory, "Installer"), false,
                RecommendationCode.DoNotDeleteDirectly, "WindowsInstallerCache");
            AddProtection(rules, "PROTECT-WINDOWS-SYSTEM32-001", Path.Combine(paths.WindowsDirectory, "System32"), false,
                RecommendationCode.DoNotDeleteDirectly, "CoreSystemPath");
        }

        if (paths.SystemDirectory is not null)
        {
            AddProtection(rules, "PROTECT-SYSTEM-DIRECTORY-001", paths.SystemDirectory, false,
                RecommendationCode.DoNotDeleteDirectly, "CoreSystemPath");
        }
    }

    private static void AddRootProtectionRules(List<RuleDefinition> rules, ResolvedKnownPaths paths)
    {
        AddProtection(rules, "PROTECT-SYSTEM-VOLUME-001", Path.Combine(paths.VolumeRoot, "System Volume Information"), false,
            RecommendationCode.DoNotDeleteDirectly, "SystemVolumeMetadata");
        AddProtection(rules, "PROTECT-BOOT-001", Path.Combine(paths.VolumeRoot, "Boot"), false,
            RecommendationCode.DoNotDeleteDirectly, "BootMetadata");
        AddProtection(rules, "PROTECT-EFI-001", Path.Combine(paths.VolumeRoot, "EFI"), false,
            RecommendationCode.DoNotDeleteDirectly, "BootMetadata");
        AddProtection(rules, "PROTECT-RECOVERY-001", Path.Combine(paths.VolumeRoot, "Recovery"), false,
            RecommendationCode.DoNotDeleteDirectly, "RecoveryMetadata");
        foreach (var file in new[] { "pagefile.sys", "swapfile.sys", "hiberfil.sys" })
        {
            AddProtection(rules, $"PROTECT-ROOT-{file.Replace('.', '-').ToUpperInvariant()}-001", Path.Combine(paths.VolumeRoot, file), true,
                RecommendationCode.UseWindowsManagedCleanup, "ManagedSystemBackingFile");
        }

        if (paths.ApplicationDataRoot is not null)
        {
            rules.Add(Rule(
                "EXCLUDE-APPDATA-OWNED-001", 20_000, RuleScope.Both,
                new RuleConditionSet { PathPrefix = paths.ApplicationDataRoot },
                new RuleAction(CleanupCategory.Protected, RiskLevel.Protected, ConfidenceLevel.High,
                    RecommendationCode.ExcludedApplicationData, ProtectionStrength.ReservedExcluded,
                    Evidence: EvidenceFlags.ProtectedPath),
                "Application-owned mutable data is reserved and excluded from scan analysis.", "ApplicationDataExcluded"));
        }
    }

    private static void AddTemporaryRules(List<RuleDefinition> rules, ResolvedKnownPaths paths)
    {
        if (paths.UserTemp is not null)
        {
            rules.Add(Rule(
                "TEMP-USER-001", 900, RuleScope.Both,
                new RuleConditionSet { PathPrefix = paths.UserTemp, IsReparsePoint = false },
                new RuleAction(CleanupCategory.HighConfidenceReclaimable, RiskLevel.Low, ConfidenceLevel.High,
                    RecommendationCode.ReviewTemporaryCandidate, UseAllocatedReclaimEstimate: true),
                "The path is inside the resolved user temporary-data root.", "RecognizedUserTemp"));
        }

        if (paths.WindowsDirectory is not null)
        {
            rules.Add(Rule(
                "TEMP-WINDOWS-001", 890, RuleScope.Both,
                new RuleConditionSet { PathPrefix = Path.Combine(paths.WindowsDirectory, "Temp"), IsReparsePoint = false },
                new RuleAction(CleanupCategory.HighConfidenceReclaimable, RiskLevel.Medium, ConfidenceLevel.High,
                    RecommendationCode.ReviewTemporaryCandidate, UseAllocatedReclaimEstimate: true),
                "The path is inside the resolved Windows temporary-data root.", "RecognizedWindowsTemp"));
        }
    }

    private static void AddCacheRules(List<RuleDefinition> rules, ResolvedKnownPaths paths)
    {
        if (paths.LocalAppData is null) return;

        rules.Add(Rule(
            "CACHE-CHROMIUM-001", 800, RuleScope.Both,
            new RuleConditionSet
            {
                PathPrefix = Path.Combine(paths.LocalAppData, "Google", "Chrome", "User Data"),
                AnyPathSegments = ["Cache", "Code Cache", "GPUCache"],
                IsReparsePoint = false
            },
            new RuleAction(CleanupCategory.HighConfidenceReclaimable, RiskLevel.Low, ConfidenceLevel.High,
                RecommendationCode.ReviewCacheCandidate, UseAllocatedReclaimEstimate: true),
            "The path is a recognized Chromium cache subtree, not the entire browser profile.", "RecognizedChromiumCache"));

        rules.Add(Rule(
            "CACHE-CHROMIUM-EDGE-001", 800, RuleScope.Both,
            new RuleConditionSet
            {
                PathPrefix = Path.Combine(paths.LocalAppData, "Microsoft", "Edge", "User Data"),
                AnyPathSegments = ["Cache", "Code Cache", "GPUCache"],
                IsReparsePoint = false
            },
            new RuleAction(CleanupCategory.HighConfidenceReclaimable, RiskLevel.Low, ConfidenceLevel.High,
                RecommendationCode.ReviewCacheCandidate, UseAllocatedReclaimEstimate: true),
            "The path is a recognized Edge cache subtree, not the entire browser profile.", "RecognizedChromiumCache"));

        rules.Add(Rule(
            "CACHE-THUMBNAIL-001", 790, RuleScope.File,
            new RuleConditionSet
            {
                ParentPathPrefix = Path.Combine(paths.LocalAppData, "Microsoft", "Windows", "Explorer"),
                NameGlob = "thumbcache_*.db",
                IsReparsePoint = false
            },
            new RuleAction(CleanupCategory.HighConfidenceReclaimable, RiskLevel.Low, ConfidenceLevel.High,
                RecommendationCode.ReviewCacheCandidate, UseAllocatedReclaimEstimate: true),
            "The file matches the Windows thumbnail cache pattern in its recognized location.", "RecognizedThumbnailCache"));
    }

    private static void AddReviewRules(List<RuleDefinition> rules, ResolvedKnownPaths paths)
    {
        if (paths.LocalAppData is not null)
        {
            rules.Add(Rule(
                "CRASH-LOCALDUMP-001", 700, RuleScope.Both,
                new RuleConditionSet { PathPrefix = Path.Combine(paths.LocalAppData, "CrashDumps"), IsReparsePoint = false },
                new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.High,
                    RecommendationCode.ReviewCrashArtifact, UseAllocatedReclaimEstimate: true),
                "The path is a recognized crash dump artifact that may still be useful for debugging.", "CrashArtifactReview"));
        }

        rules.Add(Rule(
            "RECYCLE-BIN-001", 690, RuleScope.Both,
            new RuleConditionSet { PathPrefix = Path.Combine(paths.VolumeRoot, "$Recycle.Bin"), IsReparsePoint = false },
            new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.High,
                RecommendationCode.ReviewRecycleBin, UseAllocatedReclaimEstimate: true),
            "Recycle Bin content is already user-deleted but permanent removal is irreversible.", "RecycleBinReview"));

        if (paths.UserProfile is not null)
        {
            rules.Add(Rule(
                "DEV-GRADLE-CACHE-001", 650, RuleScope.Both,
                new RuleConditionSet { PathPrefix = Path.Combine(paths.UserProfile, ".gradle", "caches"), IsReparsePoint = false },
                new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.High,
                    RecommendationCode.ReviewDeveloperCache, UseAllocatedReclaimEstimate: true),
                "The path is in the Gradle cache and may be expensive or impossible to restore offline.", "DeveloperCacheReview"));
            rules.Add(Rule(
                "DEV-NUGET-CACHE-001", 650, RuleScope.Both,
                new RuleConditionSet { PathPrefix = Path.Combine(paths.UserProfile, ".nuget", "packages"), IsReparsePoint = false },
                new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.High,
                    RecommendationCode.ReviewDeveloperCache, UseAllocatedReclaimEstimate: true),
                "The path is in the NuGet package cache and restore availability is not guaranteed.", "DeveloperCacheReview"));
        }
    }

    private static void AddProtection(
        List<RuleDefinition> rules,
        string id,
        string path,
        bool exact,
        RecommendationCode recommendation,
        string reason)
    {
        rules.Add(Rule(
            id, 10_000, RuleScope.Both,
            exact ? new RuleConditionSet { ExactPath = path } : new RuleConditionSet { PathPrefix = path },
            new RuleAction(CleanupCategory.Protected, RiskLevel.Protected, ConfidenceLevel.High,
                recommendation, ProtectionStrength.HardProtected, Evidence: EvidenceFlags.ProtectedPath),
            "This path is protected from normal cleanup recommendations.", reason));
    }

    private static RuleDefinition Rule(
        string id,
        int priority,
        RuleScope scope,
        RuleConditionSet conditions,
        RuleAction action,
        string explanation,
        string reason) =>
        new(id, Version, id, true, priority, scope, conditions, action, explanation, reason);

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
