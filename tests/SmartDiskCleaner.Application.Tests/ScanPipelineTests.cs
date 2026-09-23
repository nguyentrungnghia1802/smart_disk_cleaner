using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;
using Xunit;

namespace SmartDiskCleaner.Application.Tests;

public sealed class ScanPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GoldenPipeline_AccountsAggregatesClassifiesThenBuildsFullPresentation()
    {
        var raw = new List<RawScanNode>
        {
            Directory(90, null, 0, @"C:\root")
        };
        for (var index = 0; index < 11; index++)
        {
            raw.Add(Directory(200 + index, 90, 1, $@"C:\root\d{index:D2}"));
            raw.Add(File(500 + index, 200 + index, 2, $@"C:\root\d{index:D2}\payload.bin", index + 1, index + 1,
                new PhysicalFileIdentity("VOL", $"F{index}")));
        }

        var shared = new PhysicalFileIdentity("VOL", "SHARED");
        raw.Add(File(700, 200, 2, @"C:\root\d00\shared-a.bin", 10, 16, shared));
        raw.Add(File(701, 201, 2, @"C:\root\d01\shared-b.bin", 10, 16, shared));
        var scanner = new FakeScanner(raw);
        var pipeline = CreatePipeline(scanner, DirectorySizeClassifier());

        var result = await pipeline.RunAsync(@"C:\root", "1", null, CancellationToken.None);

        Assert.True(result.IsCompleted);
        var snapshot = Assert.IsType<ScanSnapshot>(result.Snapshot);
        Assert.Equal(raw.Count, snapshot.Nodes.Count);
        Assert.Single(snapshot.Warnings);
        var root = snapshot.Nodes.Single(n => n.ParentId is null);
        Assert.Equal(86, root.Aggregate.LogicalBytesRecursive);
        Assert.Equal(82, root.Aggregate.UniqueAllocatedBytesRecursive);
        Assert.Equal(CleanupCategory.Unknown, root.Classification.Category);
        Assert.Equal(5, snapshot.PresentationDecisions[root.Id].AutoExpandDirectoryIds.Count);
        Assert.Equal(11, snapshot.PresentationDecisions[root.Id].VisibleChildIds.Count);
        Assert.Equal(82, snapshot.Metrics.UniqueAllocatedBytes);
        Assert.Equal(AccountingConfidence.ExactWithinCapturedMetadata, snapshot.Metrics.AccountingConfidence);
    }

    [Fact]
    public async Task SizeDependentDirectoryRule_RunsAfterAggregation()
    {
        var raw = new[]
        {
            Directory(1, null, 0, @"C:\root"),
            Directory(2, 1, 1, @"C:\root\large-dir"),
            File(3, 2, 2, @"C:\root\large-dir\data.bin", 200, 256, new PhysicalFileIdentity("VOL", "F"))
        };

        var result = await CreatePipeline(new FakeScanner(raw), DirectorySizeClassifier())
            .RunAsync(@"C:\root", "1", null, CancellationToken.None);

        var directory = result.Snapshot!.Nodes.Single(n => n.Name == "large-dir");
        Assert.Equal(200, directory.Aggregate.LogicalBytesRecursive);
        Assert.Equal(CleanupCategory.ReviewRecommended, directory.Classification.Category);
        Assert.Contains("DIR-SIZE", directory.Classification.MatchedRuleIds);
    }

    [Fact]
    public async Task CancellationAtAccountingStage_ReturnsTypedCancelledSessionWithoutSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(value =>
        {
            if (value.Stage == ScanProgressStage.Accounting) cancellation.Cancel();
        });
        var scanner = new FakeScanner([Directory(1, null, 0, @"C:\root")]);

        var result = await CreatePipeline(scanner, DirectorySizeClassifier())
            .RunAsync(@"C:\root", "1", progress, cancellation.Token);

        Assert.Equal(SessionStatus.Cancelled, result.Session.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task ScannerFailure_ReturnsTypedFailedSessionWithoutPartialSnapshot()
    {
        var result = await CreatePipeline(new ThrowingScanner(), DirectorySizeClassifier())
            .RunAsync(@"C:\root", "1", null, CancellationToken.None);

        Assert.Equal(SessionStatus.Failed, result.Session.Status);
        Assert.Equal("ROOT_ACCESS_FAILED", result.Session.FailureCode);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task SameFactsProduceDeterministicNodesAndPresentation()
    {
        var raw = new[]
        {
            File(8, 9, 1, @"C:\root\b.bin", 2, 4, new PhysicalFileIdentity("VOL", "B")),
            Directory(9, null, 0, @"C:\root"),
            File(7, 9, 1, @"C:\root\a.bin", 1, 4, new PhysicalFileIdentity("VOL", "A"))
        };
        var first = await CreatePipeline(new FakeScanner(raw), DirectorySizeClassifier()).RunAsync(@"C:\root", "1", null, CancellationToken.None);
        var second = await CreatePipeline(new FakeScanner(raw.Reverse()), DirectorySizeClassifier()).RunAsync(@"C:\root", "1", null, CancellationToken.None);

        Assert.Equal(first.Snapshot!.Nodes.Select(Project), second.Snapshot!.Nodes.Select(Project));
        Assert.Equal(first.Snapshot.PresentationDecisions.Select(ProjectDecision),
            second.Snapshot.PresentationDecisions.Select(ProjectDecision));
    }

    private static object Project(ScanNode node) => new
    {
        node.Id,
        node.ParentId,
        node.FullPath,
        node.LogicalBytes,
        node.AllocatedBytes,
        node.UniqueAllocatedBytes,
        node.Aggregate,
        node.Classification.Category,
        Rules = string.Join(',', node.Classification.MatchedRuleIds)
    };

    private static object ProjectDecision(KeyValuePair<long, PresentationDecision> pair) => new
    {
        pair.Key,
        Visible = string.Join(',', pair.Value.VisibleChildIds),
        Expanded = string.Join(',', pair.Value.AutoExpandDirectoryIds),
        Collapsed = string.Join(',', pair.Value.CollapsedDirectoryIds),
        pair.Value.RankingMetric,
        pair.Value.ThresholdTriggered
    };

    private static ScanPipeline CreatePipeline(IFileSystemScanner scanner, IClassificationService classification) =>
        new(scanner, new StorageAccountingService(), new DirectoryAggregationService(), classification,
            new AdaptiveTreePolicy(), new FixedClock());

    private static ClassificationService DirectorySizeClassifier()
    {
        var rule = new RuleDefinition("DIR-SIZE", "1", "Directory size", true, 1, RuleScope.Directory,
            new RuleConditionSet { MinimumLogicalBytes = 100 },
            new RuleAction(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.High,
                RecommendationCode.ReviewGenericCandidate), "directory aggregate is large", "DirectoryLarge");
        return new ClassificationService(new RuleSet("1", [rule]));
    }

    private static RawScanNode Directory(long id, long? parentId, int depth, string path) =>
        new(id, parentId, depth, path, Path.GetFileName(path.TrimEnd('\\')), NodeType.Directory, FileAttributes.Directory,
            0, null, null, null, null, Now, Now, Now, MetadataFlags.None);

    private static RawScanNode File(long id, long parentId, int depth, string path, long logical, long allocated, PhysicalFileIdentity identity) =>
        new(id, parentId, depth, path, Path.GetFileName(path), NodeType.File, FileAttributes.Normal, logical, allocated,
            identity, 1, null, Now, Now, Now, MetadataFlags.AllocatedSizeKnown | MetadataFlags.PhysicalIdentityKnown);

    private sealed class FakeScanner(IEnumerable<RawScanNode> nodes) : IFileSystemScanner
    {
        private readonly IReadOnlyList<RawScanNode> _nodes = nodes.ToArray();

        public Task<ScannerResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var warning = new ScanWarning(99, request.SessionId, @"C:\root\warning", "Injected", "INJECTED",
                "Injected recoverable warning.", null, Now, WarningSeverity.Warning, true);
            return Task.FromResult(new ScannerResult(new VolumeInfo(@"C:\root", "VOL", "NTFS", true), _nodes, [warning]));
        }
    }

    private sealed class ThrowingScanner : IFileSystemScanner
    {
        public Task<ScannerResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
            throw new RootAccessException("Injected root failure.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class ImmediateProgress(Action<ScanProgress> action) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => action(value);
    }
}
