namespace SmartDiskCleaner.App.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartDiskCleaner.App.Mvvm;
using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IScanOrchestrator _orchestrator;
    private readonly IExportService _exportService;
    private readonly IScanSearchService _searchService;
    private readonly IAppConfigService _configService;
    private readonly IAppLogger _logger;

    private AppUiState _uiState = AppUiState.Idle;
    private string _statusText = "Ready to scan.";
    private string? _errorMessage;
    private ScanSnapshot? _activeSnapshot;
    private CancellationTokenSource? _scanCts;
    private Stopwatch? _scanStopwatch;
    private DispatcherTimer? _timer;

    public MainWindowViewModel(
        IScanOrchestrator orchestrator,
        IExportService exportService,
        IScanSearchService searchService,
        IAppConfigService configService,
        IAppLogger logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        DriveSelector = new DriveSelectorViewModel();
        Progress = new ScanProgressViewModel();
        Summary = new SummaryViewModel();
        Details = new NodeDetailsViewModel();
        Warnings = new WarningsViewModel();
        RootNodes = new ObservableCollection<TreeNodeViewModel>();

        Search = new SearchFilterViewModel(_searchService, SelectNode);
        RecentSnapshots = new RecentSnapshotsViewModel(_orchestrator, ApplyLoadedSnapshot);

        StartScanCommand = new AsyncRelayCommand(StartScanAsync, CanStartScan);
        CancelScanCommand = new RelayCommand(CancelScan, CanCancelScan);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, CanExport);
        ExportJsonCommand = new AsyncRelayCommand(ExportJsonAsync, CanExport);
    }

    public DriveSelectorViewModel DriveSelector { get; }
    public ScanProgressViewModel Progress { get; }
    public SummaryViewModel Summary { get; }
    public NodeDetailsViewModel Details { get; }
    public SearchFilterViewModel Search { get; }
    public RecentSnapshotsViewModel RecentSnapshots { get; }
    public WarningsViewModel Warnings { get; }
    public ObservableCollection<TreeNodeViewModel> RootNodes { get; }

    public ICommand StartScanCommand { get; }
    public ICommand CancelScanCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportJsonCommand { get; }

    public AppUiState UiState
    {
        get => _uiState;
        set
        {
            if (SetProperty(ref _uiState, value))
            {
                OnPropertyChanged(nameof(IsIdleOrReady));
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(CanChangeDrive));
                ((AsyncRelayCommand)StartScanCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CancelScanCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)ExportCsvCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)ExportJsonCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsIdleOrReady => UiState is AppUiState.Idle or AppUiState.Ready or AppUiState.Error;
    public bool IsScanning => UiState is AppUiState.Scanning or AppUiState.Aggregating or AppUiState.Classifying or AppUiState.Saving or AppUiState.Cancelling;
    public bool CanChangeDrive => IsIdleOrReady;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ScanSnapshot? ActiveSnapshot
    {
        get => _activeSnapshot;
        private set
        {
            if (SetProperty(ref _activeSnapshot, value))
            {
                ((AsyncRelayCommand)ExportCsvCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)ExportJsonCommand).NotifyCanExecuteChanged();
            }
        }
    }

    private bool CanStartScan() => IsIdleOrReady && DriveSelector.SelectedDrive != null;
    private bool CanCancelScan() => IsScanning && UiState != AppUiState.Cancelling;
    private bool CanExport() => ActiveSnapshot != null && IsIdleOrReady;

    public async Task InitializeAsync()
    {
        await _configService.LoadAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(_configService.Current.SelectedDrive))
        {
            var match = DriveSelector.AvailableDrives.FirstOrDefault(d =>
                string.Equals(d.RootPath, _configService.Current.SelectedDrive, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                DriveSelector.SelectedDrive = match;
            }
        }

        await RecentSnapshots.RefreshRecentSessionsAsync().ConfigureAwait(true);
    }

    public async Task StartScanAsync()
    {
        if (DriveSelector.SelectedDrive == null) return;

        var volumeRoot = DriveSelector.SelectedDrive.RootPath;
        ErrorMessage = null;
        UiState = AppUiState.Scanning;
        StatusText = $"Scanning drive {volumeRoot}...";
        Progress.Reset();
        Progress.IsActive = true;
        RecentSnapshots.IsHistoricalLoaded = false;

        _scanCts = new CancellationTokenSource();
        _scanStopwatch = Stopwatch.StartNew();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (s, e) =>
        {
            if (_scanStopwatch != null)
            {
                Progress.ElapsedTimeText = _scanStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
            }
        };
        _timer.Start();

        var throttledProgress = new ThrottledProgress<ScanProgress>(
            p =>
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    Progress.UpdateProgress(p);
                    UpdateUiStateFromStage(p.Stage);
                });
            },
            TimeSpan.FromMilliseconds(100),
            p => p.Stage is ScanProgressStage.Accounting or ScanProgressStage.Aggregating or ScanProgressStage.Classifying or ScanProgressStage.BuildingSnapshot or ScanProgressStage.Completed or ScanProgressStage.Cancelled);

        try
        {
            var result = await Task.Run(() =>
                _orchestrator.ExecuteScanAsync(volumeRoot, _configService.Current.Version, throttledProgress, _scanCts.Token)
            ).ConfigureAwait(true);

            _timer.Stop();
            _scanStopwatch.Stop();

            if (result.IsSuccess && result.Snapshot != null)
            {
                ApplyLoadedSnapshot(result.Snapshot);
                UiState = AppUiState.Ready;
                StatusText = $"Scan completed successfully for {volumeRoot}. Found {result.Snapshot.Nodes.Count} entries.";

                // Save last selected drive in config
                _ = _configService.SaveAsync(_configService.Current with { SelectedDrive = volumeRoot });
                _ = RecentSnapshots.RefreshRecentSessionsAsync();
            }
            else if (result.Session.Status == SessionStatus.Cancelled)
            {
                UiState = AppUiState.Idle;
                StatusText = "Scan was cancelled by user.";
                Progress.StageText = "Scan Cancelled.";
            }
            else
            {
                UiState = AppUiState.Error;
                ErrorMessage = $"Scan failed: {result.Session.FailureCode ?? "Unknown error"}.";
                StatusText = "Scan failed.";
            }
        }
        catch (OperationCanceledException)
        {
            UiState = AppUiState.Idle;
            StatusText = "Scan was cancelled.";
            Progress.StageText = "Scan Cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError("ScanExecution", "Scan failed with unhandled exception.", ex);
            UiState = AppUiState.Error;
            ErrorMessage = $"Error during scan: {ex.Message}";
            StatusText = "Scan failed.";
        }
        finally
        {
            _timer?.Stop();
            _scanStopwatch?.Stop();
            Progress.IsActive = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    public void CancelScan()
    {
        if (CanCancelScan() && _scanCts != null)
        {
            UiState = AppUiState.Cancelling;
            StatusText = "Cancelling scan... waiting for current operation to complete.";
            Progress.StageText = "Cancelling...";
            _scanCts.Cancel();
        }
    }

    private void UpdateUiStateFromStage(ScanProgressStage stage)
    {
        switch (stage)
        {
            case ScanProgressStage.Scanning:
                UiState = AppUiState.Scanning;
                break;
            case ScanProgressStage.Accounting:
            case ScanProgressStage.Aggregating:
                UiState = AppUiState.Aggregating;
                break;
            case ScanProgressStage.Classifying:
                UiState = AppUiState.Classifying;
                break;
            case ScanProgressStage.BuildingSnapshot:
                UiState = AppUiState.Saving;
                break;
        }
    }

    public void ApplyLoadedSnapshot(ScanSnapshot snapshot)
    {
        ActiveSnapshot = snapshot;
        Summary.Update(snapshot);
        Warnings.UpdateWarnings(snapshot.Warnings);
        Search.SetSnapshot(snapshot);

        // Build Presentation Root Nodes
        RootNodes.Clear();
        var roots = snapshot.Nodes.Where(n => n.ParentId == null).OrderBy(n => n.FullPath).ToArray();
        foreach (var r in roots)
        {
            var vm = new TreeNodeViewModel(r, snapshot, SelectNode);
            RootNodes.Add(vm);
            vm.IsExpanded = true;
            vm.EnsureChildrenLoaded(autoExpandMaxDepth: 6);
        }

        if (RootNodes.Count > 0)
        {
            SelectNode(RootNodes[0].Node);
        }

        UiState = AppUiState.Ready;
    }

    public void SelectNode(ScanNode node)
    {
        Details.SetNode(node);
    }

    private async Task ExportCsvAsync()
    {
        if (ActiveSnapshot == null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = $"SmartDiskCleaner_{ActiveSnapshot.Session.VolumeRoot.Replace(":", "").Replace("\\", "")}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _exportService.ExportAsync(ActiveSnapshot, dialog.FileName, ExportFormat.Csv).ConfigureAwait(true);
                StatusText = $"Successfully exported to CSV: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError("ExportCsv", "Export failed", ex);
                ErrorMessage = $"Failed to export CSV: {ex.Message}";
            }
        }
    }

    private async Task ExportJsonAsync()
    {
        if (ActiveSnapshot == null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = $"SmartDiskCleaner_{ActiveSnapshot.Session.VolumeRoot.Replace(":", "").Replace("\\", "")}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _exportService.ExportAsync(ActiveSnapshot, dialog.FileName, ExportFormat.Json).ConfigureAwait(true);
                StatusText = $"Successfully exported to JSON: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError("ExportJson", "Export failed", ex);
                ErrorMessage = $"Failed to export JSON: {ex.Message}";
            }
        }
    }
}
