using SmartDiskCleaner.Domain;
using SmartDiskCleaner.Rules;
using Xunit;

namespace SmartDiskCleaner.Domain.Tests;

public sealed class ClassificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly ResolvedKnownPaths Paths = new(
        @"C:\", @"C:\Windows", @"C:\Windows\System32", @"C:\Users\me",
        @"C:\Users\me\AppData\Local", @"C:\Users\me\AppData\Roaming",
        @"C:\Users\me\AppData\Local\Temp", @"C:\Users\me\AppData\Local\SmartDiskCleaner");
    private static readonly ClassificationService Service = new(BuiltInRuleSetFactory.Create(Paths));

    [Fact]
    public void ProtectedRule_IsStickyOverGenericTmpRule()
    {
        var result = Service.Classify(TestNodeFactory.File(2, @"C:\Windows\WinSxS\payload.tmp"), Now);

        Assert.Equal(CleanupCategory.Protected, result.Category);
        Assert.Equal(RiskLevel.Protected, result.Risk);
        Assert.True(result.IsProtected);
        Assert.Contains("PROTECT-WINDOWS-WINSXS-001", result.MatchedRuleIds);
        Assert.Contains("GENERIC-TMP-REVIEW-001", result.MatchedRuleIds);
        Assert.Null(result.ReclaimableBytesEstimate);
    }

    [Theory]
    [InlineData(@"c:\WINDOWS\installer\x.tmp")]
    [InlineData(@"C:\Windows\System32\cache.tmp")]
    [InlineData(@"C:\System Volume Information\tracking.log")]
    [InlineData(@"C:\pagefile.sys")]
    [InlineData(@"C:\Boot\BCD")]
    public void ProtectionFamilies_AreCaseInsensitive(string path)
    {
        Assert.Equal(CleanupCategory.Protected, Service.Classify(TestNodeFactory.File(2, path), Now).Category);
    }

    [Fact]
    public void RecognizedUserTemp_IsStrongCandidate_ButArbitraryTempFolderIsNot()
    {
        var recognized = Service.Classify(TestNodeFactory.File(2, @"C:\Users\me\AppData\Local\Temp\x.bin", allocated: 20), Now);
        var arbitrary = Service.Classify(TestNodeFactory.File(3, @"C:\Source\MyApp\Temp\important-model.bin", allocated: 20), Now);

        Assert.Equal(CleanupCategory.HighConfidenceReclaimable, recognized.Category);
        Assert.Equal(ConfidenceLevel.High, recognized.Confidence);
        Assert.Equal(20, recognized.ReclaimableBytesEstimate);
        Assert.Equal(CleanupCategory.Unknown, arbitrary.Category);
    }

    [Fact]
    public void WindowsTemp_IsCandidateWithMediumRisk()
    {
        var result = Service.Classify(TestNodeFactory.File(2, @"C:\Windows\Temp\update.dat"), Now);
        Assert.Equal(CleanupCategory.HighConfidenceReclaimable, result.Category);
        Assert.Equal(RiskLevel.Medium, result.Risk);
    }

    [Fact]
    public void ChromiumCache_DoesNotCoverNeighboringProfileData()
    {
        var cache = Service.Classify(TestNodeFactory.File(2,
            @"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache\data_0"), Now);
        var loginData = Service.Classify(TestNodeFactory.File(3,
            @"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Login Data"), Now);

        Assert.Equal(CleanupCategory.HighConfidenceReclaimable, cache.Category);
        Assert.Contains("CACHE-CHROMIUM-001", cache.MatchedRuleIds);
        Assert.Equal(CleanupCategory.Unknown, loginData.Category);
    }

    [Fact]
    public void ThumbnailCache_RequiresExpectedParentContext()
    {
        var cache = Service.Classify(TestNodeFactory.File(2,
            @"C:\Users\me\AppData\Local\Microsoft\Windows\Explorer\thumbcache_256.db"), Now);
        var nearMiss = Service.Classify(TestNodeFactory.File(3, @"C:\Project\thumbcache_256.db"), Now);

        Assert.Equal(CleanupCategory.HighConfidenceReclaimable, cache.Category);
        Assert.Equal(CleanupCategory.Unknown, nearMiss.Category);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\CrashDumps\app.dmp", "CRASH-LOCALDUMP-001")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\$R1.bin", "RECYCLE-BIN-001")]
    [InlineData(@"C:\Users\me\.gradle\caches\modules.bin", "DEV-GRADLE-CACHE-001")]
    [InlineData(@"C:\Users\me\.nuget\packages\x\1.0\x.nupkg", "DEV-NUGET-CACHE-001")]
    [InlineData(@"C:\src\app\node_modules", "DEV-NODE-MODULES-001")]
    public void ReviewFamilies_AreNeverHighConfidenceReclaimable(string path, string ruleId)
    {
        var node = path.EndsWith("node_modules", StringComparison.OrdinalIgnoreCase)
            ? TestNodeFactory.Directory(2, path, 100, 100, 100)
            : TestNodeFactory.File(2, path, 100, 100);
        var result = Service.Classify(node, Now);

        Assert.Equal(CleanupCategory.ReviewRecommended, result.Category);
        Assert.Contains(ruleId, result.MatchedRuleIds);
    }

    [Theory]
    [InlineData("archive.iso")]
    [InlineData("movie.mkv")]
    [InlineData("backup.vhdx")]
    public void LargePersonalFile_IsInformationalNotJunk(string fileName)
    {
        var result = Service.Classify(TestNodeFactory.File(2, $@"C:\Users\me\Downloads\{fileName}", 20L * 1024 * 1024 * 1024,
            20L * 1024 * 1024 * 1024), Now);

        Assert.Equal(CleanupCategory.LargeFileNotJunk, result.Category);
        Assert.Equal(RiskLevel.High, result.Risk);
        Assert.Equal(RecommendationCode.ReviewLargePersonalFile, result.Recommendation);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void OldLog_IsOnlyLowConfidenceReview_AndUnknownTimestampDoesNotMatchAgeRule()
    {
        var old = TestNodeFactory.File(2, @"C:\logs\app.log", modified: Now.AddDays(-60));
        var unknownAge = TestNodeFactory.File(3, @"C:\logs\app.log", modified: null);

        var oldResult = Service.Classify(old, Now);
        var unknownResult = Service.Classify(unknownAge, Now);

        Assert.Equal(CleanupCategory.ReviewRecommended, oldResult.Category);
        Assert.Equal(ConfidenceLevel.Low, oldResult.Confidence);
        Assert.Equal(CleanupCategory.Unknown, unknownResult.Category);
    }

    [Fact]
    public void MissingAllocatedMetadata_DoesNotMatchAllocatedCondition()
    {
        var rule = new RuleDefinition("ALLOC", "1", "allocated", true, 1, RuleScope.File,
            new RuleConditionSet { MinimumAllocatedBytes = 1 },
            new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Low, ConfidenceLevel.High,
                RecommendationCode.ReviewGenericCandidate), "allocated", "allocated");
        var service = new ClassificationService(new RuleSet("1", [rule]));

        Assert.Equal(CleanupCategory.Unknown, service.Classify(TestNodeFactory.File(2, @"C:\x", allocated: null), Now).Category);
    }

    [Fact]
    public void AllV1ConditionKinds_CanBeCombinedDeterministically()
    {
        var path = @"C:\context\parent\cache-file.tmp";
        var condition = new RuleConditionSet
        {
            ExactPath = path,
            PathPrefix = @"C:\context",
            ParentPathPrefix = @"C:\context\parent",
            NameGlob = "cache-*.tmp",
            Extension = ".TMP",
            NodeType = NodeType.File,
            MinimumLogicalBytes = 10,
            MaximumLogicalBytes = 100,
            MinimumAllocatedBytes = 10,
            MaximumAllocatedBytes = 100,
            MinimumModifiedAge = TimeSpan.FromDays(10),
            RequiredAttributes = FileAttributes.Hidden,
            IsReparsePoint = true,
            AllPathSegments = ["context", "parent"],
            AnyPathSegments = ["parent", "other"]
        };
        var rule = new RuleDefinition("ALL", "1", "all", true, 1, RuleScope.File, condition,
            new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.High,
                RecommendationCode.ReviewGenericCandidate), "all", "all");
        var service = new ClassificationService(new RuleSet("1", [rule]));
        var node = TestNodeFactory.File(2, path, 50, 64, modified: Now.AddDays(-20),
            attributes: FileAttributes.Hidden, reparse: true);

        var first = service.Classify(node, Now);
        var second = service.Classify(node, Now);

        Assert.Equal(CleanupCategory.ReviewRecommended, first.Category);
        Assert.Equal(first.Category, second.Category);
        Assert.Equal(first.Risk, second.Risk);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.Recommendation, second.Recommendation);
        Assert.Equal(first.PrimaryReason, second.PrimaryReason);
        Assert.Equal(first.MatchedRuleIds, second.MatchedRuleIds);
        Assert.Equal(first.Evidence, second.Evidence);
        Assert.Equal("1", first.RuleSetVersion);
        Assert.NotNull(first.PrimaryReason);
    }

    [Fact]
    public void InvalidOrDuplicateRules_AreRejected()
    {
        var valid = new RuleDefinition("X", "1", "x", true, 1, RuleScope.Both, new RuleConditionSet(),
            new RuleAction(CleanupCategory.Unknown, RiskLevel.High, ConfidenceLevel.None,
                RecommendationCode.UnknownNoRecommendation), "x", "x");
        Assert.Throws<ArgumentException>(() => new RuleSet("1", [valid, valid]));

        var invalidGlob = valid with { RuleId = "Y", Conditions = new RuleConditionSet { NameGlob = "[bad]" } };
        Assert.Throws<ArgumentException>(() => new RuleSet("1", [invalidGlob]));
    }
}
