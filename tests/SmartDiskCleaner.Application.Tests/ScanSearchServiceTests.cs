namespace SmartDiskCleaner.Application.Tests;

using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;
using Xunit;

public sealed class ScanSearchServiceTests
{
    private static ScanSnapshot CreateTestSnapshot()
    {
        var session = new ScanSession(Guid.NewGuid(), @"C:\root", "V1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, SessionStatus.Completed, "1", "1");

        var dir = new ScanNode(
            1, null, 0, @"C:\root\Logs", "Logs", NodeType.Directory, FileAttributes.Directory,
            0, null, null, null, null, null, null, null, null,
            MetadataFlags.None, true, AccountingConfidence.ExactWithinCapturedMetadata,
            new DirectoryAggregate(2, 0, 2, 0, 5000, 5000, 5000, 5000, 5000, 0, 0),
            ClassificationResult.Unknown("1"));

        var file1 = new ScanNode(
            2, 1, 1, @"C:\root\Logs\app.log", "app.log", NodeType.File, FileAttributes.Normal,
            1000, 1024, 1024, null, 1, null, null, null, null,
            MetadataFlags.AllocatedSizeKnown, true, AccountingConfidence.ExactWithinCapturedMetadata,
            DirectoryAggregate.Empty,
            new ClassificationResult(CleanupCategory.ReviewRecommended, RiskLevel.Low, ConfidenceLevel.Medium,
                RecommendationCode.ReviewGenericCandidate, "LOG", ["R_LOG"], false, 1024, EvidenceFlags.None, "1"));

        var file2 = new ScanNode(
            3, 1, 1, @"C:\root\Logs\cache.tmp", "cache.tmp", NodeType.File, FileAttributes.Normal,
            4000, 4096, 4096, null, 1, null, null, null, null,
            MetadataFlags.AllocatedSizeKnown, true, AccountingConfidence.ExactWithinCapturedMetadata,
            DirectoryAggregate.Empty,
            new ClassificationResult(CleanupCategory.HighConfidenceReclaimable, RiskLevel.Low, ConfidenceLevel.High,
                RecommendationCode.ReviewTemporaryCandidate, "TMP", ["R_TMP"], false, 4096, EvidenceFlags.None, "1"));

        var protectedFile = new ScanNode(
            4, null, 0, @"C:\root\system.sys", "system.sys", NodeType.File, FileAttributes.System,
            10000, 10240, 10240, null, 1, null, null, null, null,
            MetadataFlags.AllocatedSizeKnown, true, AccountingConfidence.ExactWithinCapturedMetadata,
            DirectoryAggregate.Empty,
            new ClassificationResult(CleanupCategory.Protected, RiskLevel.Protected, ConfidenceLevel.High,
                RecommendationCode.DoNotDeleteDirectly, "PROT", ["R_PROT"], true, null, EvidenceFlags.ProtectedPath, "1"));

        var reparseDir = new ScanNode(
            5, null, 0, @"C:\root\Junction", "Junction", NodeType.Directory, FileAttributes.Directory | FileAttributes.ReparsePoint,
            0, null, null, null, null, 0xA0000003u, null, null, null,
            MetadataFlags.ReparsePointDetected, true, AccountingConfidence.ExactWithinCapturedMetadata,
            DirectoryAggregate.Empty,
            ClassificationResult.Unknown("1"));

        var nodes = new[] { dir, file1, file2, protectedFile, reparseDir };
        var metrics = new ScanMetrics(5, 3, 2, 0, 15000, 15360, 15360, 15360, 15360, AccountingConfidence.ExactWithinCapturedMetadata, new Dictionary<CleanupCategory, long>());
        return new ScanSnapshot(session, nodes, [], metrics, new Dictionary<long, PresentationDecision>());
    }

    [Fact]
    public void Search_ByText_MatchesNameOrPathCaseInsensitively()
    {
        var snapshot = CreateTestSnapshot();
        var service = new ScanSearchService();

        var results = service.Search(snapshot, new SearchFilterCriteria { SearchText = "LoG" });

        Assert.Equal(3, results.Count); // "Logs" dir, "app.log", and "cache.tmp" (inside Logs)
        Assert.Contains(results, n => n.Name == "Logs");
        Assert.Contains(results, n => n.Name == "app.log");
        Assert.Contains(results, n => n.Name == "cache.tmp");
    }

    [Fact]
    public void Search_ByCategory_FiltersCorrectly()
    {
        var snapshot = CreateTestSnapshot();
        var service = new ScanSearchService();

        var results = service.Search(snapshot, new SearchFilterCriteria { Category = CleanupCategory.HighConfidenceReclaimable });

        var single = Assert.Single(results);
        Assert.Equal("cache.tmp", single.Name);
    }

    [Fact]
    public void Search_ByMinSize_FiltersCorrectly()
    {
        var snapshot = CreateTestSnapshot();
        var service = new ScanSearchService();

        var results = service.Search(snapshot, new SearchFilterCriteria { MinSizeBytes = 4000 });

        Assert.Contains(results, n => n.Name == "Logs"); // recursive 5000
        Assert.Contains(results, n => n.Name == "cache.tmp"); // 4096
        Assert.Contains(results, n => n.Name == "system.sys"); // 10240
        Assert.DoesNotContain(results, n => n.Name == "app.log"); // 1024
    }

    [Fact]
    public void Search_ByProtectedOnly_FiltersCorrectly()
    {
        var snapshot = CreateTestSnapshot();
        var service = new ScanSearchService();

        var results = service.Search(snapshot, new SearchFilterCriteria { ProtectedOnly = true });

        var single = Assert.Single(results);
        Assert.Equal("system.sys", single.Name);
    }

    [Fact]
    public void Search_ByReparseOnly_FiltersCorrectly()
    {
        var snapshot = CreateTestSnapshot();
        var service = new ScanSearchService();

        var results = service.Search(snapshot, new SearchFilterCriteria { ReparseOnly = true });

        var single = Assert.Single(results);
        Assert.Equal("Junction", single.Name);
    }

    [Fact]
    public void Search_SortBySizeDescending_OrdersProperly()
    {
        var snapshot = CreateTestSnapshot();
        var service = new ScanSearchService();

        var results = service.Search(snapshot, new SearchFilterCriteria { SortBy = SearchSortOption.SizeDescending });

        Assert.Equal("system.sys", results[0].Name); // 10240
        Assert.Equal("Logs", results[1].Name); // 5000
        Assert.Equal("cache.tmp", results[2].Name); // 4096
        Assert.Equal("app.log", results[3].Name); // 1024
    }
}
