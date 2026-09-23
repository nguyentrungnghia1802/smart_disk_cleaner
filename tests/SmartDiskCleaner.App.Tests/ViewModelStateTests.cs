namespace SmartDiskCleaner.App.Tests;

using SmartDiskCleaner.App.Mvvm;
using SmartDiskCleaner.App.ViewModels;
using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;
using Xunit;

public sealed class ViewModelStateTests
{
    private static ScanSnapshot CreateTestSnapshot(string rootPath = @"C:\root")
    {
        var session = new ScanSession(
            Guid.NewGuid(), rootPath, "VOL_1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            SessionStatus.Completed, "1", "1");

        var root = new ScanNode(
            1, null, 0, rootPath, "root", NodeType.Directory, System.IO.FileAttributes.Directory,
            0, null, null, null, null, null, null, null, null,
            MetadataFlags.None, true, AccountingConfidence.ExactWithinCapturedMetadata,
            new DirectoryAggregate(2, 2, 2, 2, 10000, 10240, 10240, 10240, 10240, 0, 0),
            ClassificationResult.Unknown("1"));

        var dir1 = new ScanNode(
            2, 1, 1, $@"{rootPath}\dir1", "dir1", NodeType.Directory, System.IO.FileAttributes.Directory,
            0, null, null, null, null, null, null, null, null,
            MetadataFlags.None, true, AccountingConfidence.ExactWithinCapturedMetadata,
            new DirectoryAggregate(1, 0, 1, 0, 6000, 6144, 6144, 6144, 6144, 0, 0),
            ClassificationResult.Unknown("1"));

        var dir2 = new ScanNode(
            3, 1, 1, $@"{rootPath}\dir2", "dir2", NodeType.Directory, System.IO.FileAttributes.Directory,
            0, null, null, null, null, null, null, null, null,
            MetadataFlags.None, true, AccountingConfidence.ExactWithinCapturedMetadata,
            new DirectoryAggregate(1, 0, 1, 0, 4000, 4096, 4096, 4096, 4096, 0, 0),
            ClassificationResult.Unknown("1"));

        var file1 = new ScanNode(
            4, 2, 2, $@"{rootPath}\dir1\cache.tmp", "cache.tmp", NodeType.File, System.IO.FileAttributes.Normal,
            6000, 6144, 6144, new PhysicalFileIdentity("VOL_1", "FILE_1"), 1, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null,
            MetadataFlags.AllocatedSizeKnown, true, AccountingConfidence.ExactWithinCapturedMetadata,
            DirectoryAggregate.Empty,
            new ClassificationResult(CleanupCategory.HighConfidenceReclaimable, RiskLevel.Low, ConfidenceLevel.High,
                RecommendationCode.ReviewTemporaryCandidate, "TMP_EXT", ["RULE_TMP"], false, 6144, EvidenceFlags.None, "1"));

        var file2 = new ScanNode(
            5, 3, 2, $@"{rootPath}\dir2\data.bin", "data.bin", NodeType.File, System.IO.FileAttributes.Normal,
            4000, 4096, 4096, new PhysicalFileIdentity("VOL_1", "FILE_2"), 1, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null,
            MetadataFlags.AllocatedSizeKnown, true, AccountingConfidence.ExactWithinCapturedMetadata,
            DirectoryAggregate.Empty,
            new ClassificationResult(CleanupCategory.LargeFileNotJunk, RiskLevel.Low, ConfidenceLevel.Medium,
                RecommendationCode.ReviewLargePersonalFile, "LARGE", ["RULE_LARGE"], false, null, EvidenceFlags.SizeOnlyInformational, "1"));

        var warnings = new[]
        {
            new ScanWarning(1, session.Id, $@"{rootPath}\warn", "Scan", "WARN_TEST", "Warning test", null, DateTimeOffset.UtcNow, WarningSeverity.Warning, true)
        };

        var decisions = new Dictionary<long, PresentationDecision>
        {
            // AdaptiveTreePolicy: root has 4 children (dir1, dir2, fileX, ...), dir1 is auto-expanded, dir2 is collapsed
            [1] = new PresentationDecision([2, 3], [2], [3], RankingMetricKind.UniqueAllocatedRecursive, true),
            [2] = new PresentationDecision([4], [], [], RankingMetricKind.UniqueAllocatedRecursive, false),
            [3] = new PresentationDecision([5], [], [], RankingMetricKind.UniqueAllocatedRecursive, false)
        };

        var metrics = new ScanMetrics(
            5, 2, 3, 1, 10000, 10240, 10240, 10240, 10240, AccountingConfidence.ExactWithinCapturedMetadata,
            new Dictionary<CleanupCategory, long>
            {
                [CleanupCategory.HighConfidenceReclaimable] = 6144,
                [CleanupCategory.LargeFileNotJunk] = 4096
            });

        return new ScanSnapshot(session, [root, dir1, dir2, file1, file2], warnings, metrics, decisions);
    }

    private sealed class FakeOrchestrator : IScanOrchestrator
    {
        public ScanSnapshot SnapshotToReturn { get; set; } = CreateTestSnapshot();
        public bool ThrowOnScan { get; set; }
        public bool CancelOnScan { get; set; }
        public List<ScanSession> RecentSessions { get; } = [];

        public Task<ScanExecutionResult> ExecuteScanAsync(string volumeRoot, string optionsVersion, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        {
            if (CancelOnScan)
            {
                var cancelled = new ScanSession(Guid.NewGuid(), volumeRoot, "VOL_1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, SessionStatus.Cancelled, "1", "1");
                return Task.FromResult(new ScanExecutionResult(cancelled, null, false));
            }

            if (ThrowOnScan)
            {
                throw new InvalidOperationException("Injected scan failure");
            }

            progress?.Report(new ScanProgress(ScanProgressStage.Scanning, 5, 1, volumeRoot));
            progress?.Report(new ScanProgress(ScanProgressStage.Completed, 5, 1, volumeRoot));

            return Task.FromResult(new ScanExecutionResult(SnapshotToReturn.Session, SnapshotToReturn, true));
        }

        public Task<ScanSnapshot?> LoadSnapshotAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ScanSnapshot?>(SnapshotToReturn);
        }

        public Task<IReadOnlyList<ScanSession>> GetRecentSessionsAsync(int maxCount = 20, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ScanSession>>(RecentSessions);
        }

        public Task<bool> RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            var match = RecentSessions.FirstOrDefault(s => s.Id == sessionId);
            if (match != null)
            {
                RecentSessions.Remove(match);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    private sealed class FakeExportService : IExportService
    {
        public Task ExportAsync(ScanSnapshot snapshot, string destinationFilePath, ExportFormat format, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLogger : IAppLogger
    {
        public void LogInformation(string operation, string message, Guid? sessionId = null) { }
        public void LogWarning(string operation, string message, string? path = null, Guid? sessionId = null) { }
        public void LogError(string operation, string message, Exception? exception = null, Guid? sessionId = null) { }
    }

    private sealed class FakeConfigService : IAppConfigService
    {
        public AppConfig Current { get; set; } = new();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
        {
            Current = config;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void InitialState_IsIdle_AndCommandEnablementIsConsistent()
    {
        var orchestrator = new FakeOrchestrator();
        var vm = new MainWindowViewModel(orchestrator, new FakeExportService(), new ScanSearchService(), new FakeConfigService(), new FakeLogger());

        Assert.Equal(AppUiState.Idle, vm.UiState);
        Assert.True(vm.IsIdleOrReady);
        Assert.False(vm.IsScanning);
        Assert.True(vm.CanChangeDrive);

        // Start command requires a selected drive
        vm.DriveSelector.SelectedDrive = new DriveItemViewModel(@"C:\", "System", 100, 50, true);
        Assert.True(vm.StartScanCommand.CanExecute(null));
        Assert.False(vm.CancelScanCommand.CanExecute(null));
        Assert.False(vm.ExportCsvCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartScanAsync_Completed_SetsReadyAndPopulatesSummaryAndTree()
    {
        var orchestrator = new FakeOrchestrator();
        var vm = new MainWindowViewModel(orchestrator, new FakeExportService(), new ScanSearchService(), new FakeConfigService(), new FakeLogger());
        vm.DriveSelector.SelectedDrive = new DriveItemViewModel(@"C:\root", "System", 100, 50, true);

        await vm.StartScanAsync();

        Assert.Equal(AppUiState.Ready, vm.UiState);
        Assert.NotNull(vm.ActiveSnapshot);
        Assert.Equal(5, vm.ActiveSnapshot.Nodes.Count);

        // Summary assertions
        Assert.Equal(@"C:\root", vm.Summary.VolumeRoot);
        Assert.Equal(2, vm.Summary.FileCount);
        Assert.Equal(3, vm.Summary.DirectoryCount);
        Assert.Equal(1, vm.Summary.WarningCount);
        Assert.Contains("Exact", vm.Summary.AccountingConfidenceText);
        Assert.NotEmpty(vm.Summary.ReclaimableSize);
        Assert.NotEmpty(vm.Summary.LargeFileSize);

        // Tree root assertion
        Assert.Single(vm.RootNodes);
        var rootVm = vm.RootNodes[0];
        Assert.Equal("root", rootVm.DisplayName);
        Assert.True(rootVm.IsExpanded);

        // Adaptive expansion test: dir1 (id 2) was in AutoExpandDirectoryIds, so it should be expanded
        var dir1Vm = rootVm.Children.FirstOrDefault(c => c.Node.Id == 2);
        Assert.NotNull(dir1Vm);
        Assert.True(dir1Vm.IsExpanded);
        Assert.NotEmpty(dir1Vm.Children); // Materialized

        // dir2 (id 3) was collapsed
        var dir2Vm = rootVm.Children.FirstOrDefault(c => c.Node.Id == 3);
        Assert.NotNull(dir2Vm);
        Assert.False(dir2Vm.IsExpanded);

        // Manual expansion of dir2 loads its children
        dir2Vm.IsExpanded = true;
        Assert.NotEmpty(dir2Vm.Children);
        Assert.Equal("data.bin", dir2Vm.Children[0].DisplayName);

        // Export commands now enabled
        Assert.True(vm.ExportCsvCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartScanAsync_Cancelled_SetsStateIdle()
    {
        var orchestrator = new FakeOrchestrator { CancelOnScan = true };
        var vm = new MainWindowViewModel(orchestrator, new FakeExportService(), new ScanSearchService(), new FakeConfigService(), new FakeLogger());
        vm.DriveSelector.SelectedDrive = new DriveItemViewModel(@"C:\root", "System", 100, 50, true);

        await vm.StartScanAsync();

        Assert.Equal(AppUiState.Idle, vm.UiState);
        Assert.Null(vm.ActiveSnapshot);
        Assert.Contains("cancelled", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NodeDetailsViewModel_FormatsFieldsAndHandlesVanishedPathSafely()
    {
        var snapshot = CreateTestSnapshot();
        var fileNode = snapshot.Nodes.Single(n => n.Name == "cache.tmp");

        var details = new NodeDetailsViewModel();
        details.SetNode(fileNode);

        Assert.True(details.HasSelectedNode);
        Assert.Equal("cache.tmp", details.NameText);
        Assert.Equal("File", details.TypeText);
        Assert.Equal("HighConfidenceReclaimable", details.CategoryText);
        Assert.Equal("Low", details.RiskText);
        Assert.Equal("High", details.ConfidenceText);
        Assert.Equal("TMP_EXT", details.PrimaryReasonText);
        Assert.Contains("RULE_TMP", details.MatchedRulesText);
        Assert.Equal("VOL_1:FILE_1", details.PhysicalIdentityText);
        Assert.Equal("1", details.LinkCountText);

        // Test OpenInExplorer on nonexistent/vanished path doesn't throw and reports vanished
        details.OpenInExplorerCommand.Execute(null);
        Assert.True(details.HasNotification);
        Assert.Contains("no longer exists", details.NotificationMessage);
    }

    [Fact]
    public void HistoricalSnapshot_SetsBannerAndLoadsSnapshotWithoutScanning()
    {
        var orchestrator = new FakeOrchestrator();
        var snapshot = CreateTestSnapshot(@"D:\historical");
        orchestrator.SnapshotToReturn = snapshot;
        var session = snapshot.Session;
        orchestrator.RecentSessions.Add(session);

        var vm = new MainWindowViewModel(orchestrator, new FakeExportService(), new ScanSearchService(), new FakeConfigService(), new FakeLogger());

        vm.RecentSnapshots.SelectedSession = session;
        vm.RecentSnapshots.OpenSnapshotCommand.Execute(null);

        Assert.True(vm.RecentSnapshots.IsHistoricalLoaded);
        Assert.Contains("Historical snapshot", vm.RecentSnapshots.HistoricalInfoText);
        Assert.Equal(@"D:\historical", vm.Summary.VolumeRoot);
    }

    [Fact]
    public void SafetyInvariant_NoDeleteOrMutationCapabilityOnScannedContent()
    {
        // Check MainWindowViewModel, TreeNodeViewModel, NodeDetailsViewModel
        var types = new[]
        {
            typeof(MainWindowViewModel),
            typeof(TreeNodeViewModel),
            typeof(NodeDetailsViewModel),
            typeof(SearchFilterViewModel),
            typeof(SummaryViewModel),
            typeof(ScanProgressViewModel)
        };

        foreach (var type in types)
        {
            var methodsAndProperties = type.GetMembers()
                .Select(m => m.Name)
                .Where(name => !name.StartsWith("remove_", StringComparison.Ordinal))
                .Where(name =>
                    name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Move", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Clean", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.Empty(methodsAndProperties);
        }
    }
}
