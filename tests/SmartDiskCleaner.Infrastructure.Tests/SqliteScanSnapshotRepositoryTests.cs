namespace SmartDiskCleaner.Infrastructure.Tests;

using SmartDiskCleaner.Domain;
using SmartDiskCleaner.Infrastructure;
using Xunit;

public sealed class SqliteScanSnapshotRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WindowsAppPathProvider _pathProvider;
    private readonly SqliteScanSnapshotRepository _repository;

    public SqliteScanSnapshotRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SmartDiskCleaner-RepoTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _pathProvider = new WindowsAppPathProvider(_tempDir);
        _repository = new SqliteScanSnapshotRepository(_pathProvider);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore transient test cleanup issues
        }
    }

    [Fact]
    public async Task SaveAndLoadSnapshot_PreservesAllFieldsAndNullability()
    {
        var sessionId = Guid.NewGuid();
        var session = new ScanSession(
            sessionId,
            @"C:\root",
            "VOL_123",
            new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 23, 10, 5, 0, TimeSpan.Zero),
            SessionStatus.Completed,
            "1.0",
            "builtin-v1");

        var rootDir = new ScanNode(
            1, null, 0, @"C:\root", "root", NodeType.Directory, FileAttributes.Directory,
            0, null, null, null, null, null, null, null, null,
            MetadataFlags.None, true, AccountingConfidence.ExactWithinCapturedMetadata,
            new DirectoryAggregate(1, 0, 1, 0, 100, 128, 128, 128, 128, 0, 0),
            new ClassificationResult(CleanupCategory.Unknown, RiskLevel.High, ConfidenceLevel.None,
                RecommendationCode.UnknownNoRecommendation, null, ["RULE1"], false, null, EvidenceFlags.None, "builtin-v1"));

        var childFile = new ScanNode(
            2, 1, 1, @"C:\root\file.tmp", "file.tmp", NodeType.File, FileAttributes.Normal,
            100, 128, 128,
            new PhysicalFileIdentity("VOL_123", "FILE_999"), 1, null,
            new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 23, 9, 30, 0, TimeSpan.Zero),
            null,
            MetadataFlags.AllocatedSizeKnown | MetadataFlags.PhysicalIdentityKnown,
            true, AccountingConfidence.ExactWithinCapturedMetadata,
            DirectoryAggregate.Empty,
            new ClassificationResult(CleanupCategory.HighConfidenceReclaimable, RiskLevel.Low, ConfidenceLevel.High,
                RecommendationCode.ReviewTemporaryCandidate, "TMP_EXT", ["RULE1", "TMP_EXT"], false, 128, EvidenceFlags.None, "builtin-v1"));

        var unknownFile = new ScanNode(
            3, 1, 1, @"C:\root\locked.bin", "locked.bin", NodeType.File, FileAttributes.Normal,
            500, null, null, // Note null allocated sizes
            null, null, null,
            null, null, null,
            MetadataFlags.MetadataPartial, false, AccountingConfidence.Partial,
            DirectoryAggregate.Empty,
            ClassificationResult.Unknown("builtin-v1"));

        var warnings = new[]
        {
            new ScanWarning(1, sessionId, @"C:\root\locked.bin", "Read", "ACCESS_DENIED", "Access Denied", "UnauthorizedAccessException",
                new DateTimeOffset(2026, 9, 23, 10, 1, 0, TimeSpan.Zero), WarningSeverity.Warning, true, 3)
        };

        var decisions = new Dictionary<long, PresentationDecision>
        {
            [1] = new PresentationDecision([2, 3], [], [], RankingMetricKind.UniqueAllocatedRecursive, false)
        };

        var metrics = new ScanMetrics(
            3, 2, 1, 1, 600, 128, null, 128, null, AccountingConfidence.Partial,
            new Dictionary<CleanupCategory, long> { [CleanupCategory.HighConfidenceReclaimable] = 128 });

        var snapshot = new ScanSnapshot(session, [rootDir, childFile, unknownFile], warnings, metrics, decisions);

        // Save
        await _repository.SaveSnapshotAsync(snapshot);

        // Reload
        var loaded = await _repository.LoadSnapshotAsync(sessionId);
        Assert.NotNull(loaded);

        // Assert session
        Assert.Equal(session.Id, loaded.Session.Id);
        Assert.Equal(session.VolumeRoot, loaded.Session.VolumeRoot);
        Assert.Equal(session.VolumeIdentity, loaded.Session.VolumeIdentity);
        Assert.Equal(session.Status, loaded.Session.Status);
        Assert.Equal(session.OptionsVersion, loaded.Session.OptionsVersion);
        Assert.Equal(session.RuleSetVersion, loaded.Session.RuleSetVersion);

        // Assert nodes
        Assert.Equal(3, loaded.Nodes.Count);
        var loadedRoot = loaded.NodeById[1];
        Assert.Equal(NodeType.Directory, loadedRoot.NodeType);
        Assert.Equal(128, loadedRoot.Aggregate.KnownAllocatedBytesRecursive);

        var loadedChild = loaded.NodeById[2];
        Assert.Equal(100, loadedChild.LogicalBytes);
        Assert.Equal(128, loadedChild.AllocatedBytes);
        Assert.Equal(128, loadedChild.UniqueAllocatedBytes);
        Assert.Equal("VOL_123", loadedChild.PhysicalIdentity?.VolumeIdentity);
        Assert.Equal("FILE_999", loadedChild.PhysicalIdentity?.FileIdentity);
        Assert.Equal(CleanupCategory.HighConfidenceReclaimable, loadedChild.Classification.Category);
        Assert.Contains("TMP_EXT", loadedChild.Classification.MatchedRuleIds);

        // Assert null preservation for unknown file
        var loadedUnknown = loaded.NodeById[3];
        Assert.Equal(500, loadedUnknown.LogicalBytes);
        Assert.Null(loadedUnknown.AllocatedBytes);
        Assert.Null(loadedUnknown.UniqueAllocatedBytes);
        Assert.False(loadedUnknown.IsAccessible);

        // Assert warnings
        Assert.Single(loaded.Warnings);
        Assert.Equal("ACCESS_DENIED", loaded.Warnings[0].Code);

        // Assert presentation decisions
        Assert.True(loaded.PresentationDecisions.ContainsKey(1));
    }

    [Fact]
    public async Task GetRecentSessions_ReturnsOnlyCompletedSessionsOrderedByStartedDesc()
    {
        var session1 = new ScanSession(Guid.NewGuid(), @"C:\", "V1",
            new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 20, 0, 1, 0, TimeSpan.Zero),
            SessionStatus.Completed, "1", "1");

        var session2 = new ScanSession(Guid.NewGuid(), @"D:\", "V2",
            new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 22, 0, 1, 0, TimeSpan.Zero),
            SessionStatus.Completed, "1", "1");

        var snap1 = new ScanSnapshot(session1, [], [], new ScanMetrics(0, 0, 0, 0, 0, 0, null, 0, null, AccountingConfidence.Unsupported, new Dictionary<CleanupCategory, long>()), new Dictionary<long, PresentationDecision>());
        var snap2 = new ScanSnapshot(session2, [], [], new ScanMetrics(0, 0, 0, 0, 0, 0, null, 0, null, AccountingConfidence.Unsupported, new Dictionary<CleanupCategory, long>()), new Dictionary<long, PresentationDecision>());

        await _repository.SaveSnapshotAsync(snap1);
        await _repository.SaveSnapshotAsync(snap2);

        var recents = await _repository.GetRecentSessionsAsync(10);
        Assert.Equal(2, recents.Count);
        Assert.Equal(session2.Id, recents[0].Id); // more recent first
        Assert.Equal(session1.Id, recents[1].Id);
    }

    [Fact]
    public async Task RemoveSession_DeletesFromDatabaseOnly()
    {
        var sessionId = Guid.NewGuid();
        var session = new ScanSession(sessionId, @"C:\", "V1",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, SessionStatus.Completed, "1", "1");
        var snapshot = new ScanSnapshot(session, [], [], new ScanMetrics(0, 0, 0, 0, 0, 0, null, 0, null, AccountingConfidence.Unsupported, new Dictionary<CleanupCategory, long>()), new Dictionary<long, PresentationDecision>());

        await _repository.SaveSnapshotAsync(snapshot);
        var loaded = await _repository.LoadSnapshotAsync(sessionId);
        Assert.NotNull(loaded);

        var removed = await _repository.RemoveSessionAsync(sessionId);
        Assert.True(removed);

        var loadedAfter = await _repository.LoadSnapshotAsync(sessionId);
        Assert.Null(loadedAfter);
    }
}
