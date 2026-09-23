namespace SmartDiskCleaner.Domain;

public sealed record RuleConditionSet
{
    public string? ExactPath { get; init; }
    public string? PathPrefix { get; init; }
    public string? ParentPathPrefix { get; init; }
    public string? NameGlob { get; init; }
    public string? Extension { get; init; }
    public NodeType? NodeType { get; init; }
    public long? MinimumLogicalBytes { get; init; }
    public long? MaximumLogicalBytes { get; init; }
    public long? MinimumAllocatedBytes { get; init; }
    public long? MaximumAllocatedBytes { get; init; }
    public TimeSpan? MinimumModifiedAge { get; init; }
    public FileAttributes? RequiredAttributes { get; init; }
    public bool? IsReparsePoint { get; init; }
    public IReadOnlyList<string> AllPathSegments { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AnyPathSegments { get; init; } = Array.Empty<string>();

    public bool Matches(ScanNode node, DateTimeOffset evaluatedAtUtc)
    {
        if (ExactPath is not null && !PathNormalizer.EqualsPath(node.FullPath, ExactPath)) return false;
        if (PathPrefix is not null && !PathNormalizer.IsWithinOrEqual(node.FullPath, PathPrefix)) return false;
        if (ParentPathPrefix is not null && !PathNormalizer.IsWithinOrEqual(PathNormalizer.Parent(node.FullPath), ParentPathPrefix)) return false;
        if (NameGlob is not null && !GlobMatcher.IsMatch(node.Name, NameGlob)) return false;
        if (Extension is not null && !string.Equals(Path.GetExtension(node.Name), NormalizeExtension(Extension), StringComparison.OrdinalIgnoreCase)) return false;
        if (NodeType.HasValue && node.NodeType != NodeType.Value) return false;

        var logicalBytes = node.RecursiveLogicalBytes;
        if (MinimumLogicalBytes.HasValue && logicalBytes < MinimumLogicalBytes.Value) return false;
        if (MaximumLogicalBytes.HasValue && logicalBytes > MaximumLogicalBytes.Value) return false;

        var allocatedBytes = node.RecursiveAllocatedBytes;
        if (MinimumAllocatedBytes.HasValue && (!allocatedBytes.HasValue || allocatedBytes.Value < MinimumAllocatedBytes.Value)) return false;
        if (MaximumAllocatedBytes.HasValue && (!allocatedBytes.HasValue || allocatedBytes.Value > MaximumAllocatedBytes.Value)) return false;
        if (MinimumModifiedAge.HasValue && (!node.ModifiedUtc.HasValue || evaluatedAtUtc - node.ModifiedUtc.Value < MinimumModifiedAge.Value)) return false;
        if (RequiredAttributes.HasValue && (node.Attributes & RequiredAttributes.Value) != RequiredAttributes.Value) return false;
        if (IsReparsePoint.HasValue && node.IsReparsePoint != IsReparsePoint.Value) return false;

        var segments = PathNormalizer.Segments(node.FullPath);
        if (AllPathSegments.Count > 0 && AllPathSegments.Any(required => !segments.Contains(required, StringComparer.OrdinalIgnoreCase))) return false;
        if (AnyPathSegments.Count > 0 && !AnyPathSegments.Any(required => segments.Contains(required, StringComparer.OrdinalIgnoreCase))) return false;
        return true;
    }

    public void Validate()
    {
        if (MinimumLogicalBytes < 0 || MaximumLogicalBytes < 0 || MinimumAllocatedBytes < 0 || MaximumAllocatedBytes < 0)
            throw new ArgumentException("Rule size conditions cannot be negative.");
        if (MinimumLogicalBytes > MaximumLogicalBytes || MinimumAllocatedBytes > MaximumAllocatedBytes)
            throw new ArgumentException("Rule minimum size cannot exceed maximum size.");
        if (MinimumModifiedAge < TimeSpan.Zero) throw new ArgumentException("Rule age cannot be negative.");
        if (NameGlob is not null) GlobMatcher.Validate(NameGlob);
        if (ExactPath is not null) _ = PathNormalizer.Normalize(ExactPath);
        if (PathPrefix is not null) _ = PathNormalizer.Normalize(PathPrefix);
        if (ParentPathPrefix is not null) _ = PathNormalizer.Normalize(ParentPathPrefix);
    }

    private static string NormalizeExtension(string extension) => extension.StartsWith('.') ? extension : "." + extension;
}

public sealed record RuleAction(
    CleanupCategory Category,
    RiskLevel Risk,
    ConfidenceLevel Confidence,
    RecommendationCode Recommendation,
    ProtectionStrength ProtectionStrength = ProtectionStrength.Normal,
    bool UseAllocatedReclaimEstimate = false,
    EvidenceFlags Evidence = EvidenceFlags.None);

public sealed record RuleDefinition(
    string RuleId,
    string Version,
    string Name,
    bool Enabled,
    int Priority,
    RuleScope Scope,
    RuleConditionSet Conditions,
    RuleAction Action,
    string Explanation,
    string PrimaryReasonCode,
    RuleSourceType SourceType = RuleSourceType.BuiltIn);

public sealed class RuleSet
{
    public RuleSet(string version, IEnumerable<RuleDefinition> rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(rules);
        Version = version;
        Rules = Array.AsReadOnly(rules.ToArray());
        Validate();
    }

    public string Version { get; }
    public IReadOnlyList<RuleDefinition> Rules { get; }

    private void Validate()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in Rules)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.RuleId);
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.Version);
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.Explanation);
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.PrimaryReasonCode);
            if (!ids.Add(rule.RuleId)) throw new ArgumentException($"Duplicate rule ID '{rule.RuleId}'.");
            rule.Conditions.Validate();
            if (rule.Action.ProtectionStrength != ProtectionStrength.Normal && rule.Action.Category != CleanupCategory.Protected)
                throw new ArgumentException($"Protection rule '{rule.RuleId}' must produce Protected category.");
        }
    }
}

public sealed class ClassificationService : IClassificationService
{
    private readonly IReadOnlyList<RuleDefinition> _rules;

    public ClassificationService(RuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        RuleSetVersion = ruleSet.Version;
        _rules = ruleSet.Rules.Where(r => r.Enabled)
            .OrderByDescending(r => r.Action.ProtectionStrength)
            .ThenByDescending(r => r.Priority)
            .ThenBy(r => r.RuleId, StringComparer.Ordinal)
            .ToArray();
    }

    public string RuleSetVersion { get; }

    public ClassificationResult Classify(ScanNode node, DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(node);
        var matches = _rules.Where(rule => ScopeMatches(rule.Scope, node.NodeType) && rule.Conditions.Matches(node, evaluatedAtUtc)).ToArray();
        if (matches.Length == 0)
        {
            var unknownEvidence = node.IsReparsePoint ? EvidenceFlags.ReparsePointNotFollowed : EvidenceFlags.None;
            if (node.MetadataFlags.HasFlag(MetadataFlags.MetadataPartial)) unknownEvidence |= EvidenceFlags.PartialMetadata;
            return ClassificationResult.Unknown(RuleSetVersion, unknownEvidence);
        }

        var protectedMatch = matches.FirstOrDefault(r => r.Action.ProtectionStrength != ProtectionStrength.Normal);
        var winner = protectedMatch ?? matches[0];
        var isProtected = protectedMatch is not null;
        var risk = isProtected ? RiskLevel.Protected : matches.Max(r => r.Action.Risk);
        var category = isProtected ? CleanupCategory.Protected : winner.Action.Category;
        var recommendation = winner.Action.Recommendation;
        var evidence = matches.Aggregate(EvidenceFlags.None, (current, rule) => current | rule.Action.Evidence);
        if (node.IsReparsePoint) evidence |= EvidenceFlags.ReparsePointNotFollowed;
        if (node.AccountingConfidence != AccountingConfidence.ExactWithinCapturedMetadata) evidence |= EvidenceFlags.PartialMetadata;
        var estimate = !isProtected && winner.Action.UseAllocatedReclaimEstimate
            ? node.RecursiveUniqueAllocatedBytes ?? node.RecursiveAllocatedBytes
            : null;

        return new ClassificationResult(
            category,
            risk,
            winner.Action.Confidence,
            recommendation,
            winner.PrimaryReasonCode,
            matches.Select(r => r.RuleId).ToArray(),
            isProtected,
            estimate,
            evidence,
            RuleSetVersion);
    }

    private static bool ScopeMatches(RuleScope scope, NodeType nodeType) =>
        scope == RuleScope.Both || (scope == RuleScope.File && nodeType == NodeType.File) || (scope == RuleScope.Directory && nodeType == NodeType.Directory);
}

internal static class GlobMatcher
{
    public static void Validate(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (pattern.IndexOfAny(['[', ']', '\0']) >= 0) throw new ArgumentException("Only '*' and '?' wildcard syntax is supported.", nameof(pattern));
    }

    public static bool IsMatch(string value, string pattern)
    {
        Validate(pattern);
        var valueIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var retryValueIndex = -1;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                valueIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                retryValueIndex = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++retryValueIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*') patternIndex++;
        return patternIndex == pattern.Length;
    }
}
