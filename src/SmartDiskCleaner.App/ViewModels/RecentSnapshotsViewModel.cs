namespace SmartDiskCleaner.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using SmartDiskCleaner.App.Mvvm;
using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;

public sealed class RecentSnapshotsViewModel : ObservableObject
{
    private readonly IScanOrchestrator _orchestrator;
    private readonly Action<ScanSnapshot> _onSnapshotLoaded;
    private ScanSession? _selectedSession;
    private bool _isLoading;
    private string _statusMessage = "";
    private bool _isHistoricalLoaded;
    private string _historicalInfoText = "";

    public RecentSnapshotsViewModel(IScanOrchestrator orchestrator, Action<ScanSnapshot> onSnapshotLoaded)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _onSnapshotLoaded = onSnapshotLoaded ?? throw new ArgumentNullException(nameof(onSnapshotLoaded));

        RecentSessions = new ObservableCollection<ScanSession>();

        LoadRecentCommand = new AsyncRelayCommand(RefreshRecentSessionsAsync);
        OpenSnapshotCommand = new AsyncRelayCommand(OpenSnapshotAsync, () => _selectedSession != null && !_isLoading);
        DeleteSnapshotCommand = new AsyncRelayCommand(DeleteSnapshotAsync, () => _selectedSession != null && !_isLoading);
    }

    public ObservableCollection<ScanSession> RecentSessions { get; }
    public ICommand LoadRecentCommand { get; }
    public ICommand OpenSnapshotCommand { get; }
    public ICommand DeleteSnapshotCommand { get; }

    public ScanSession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                ((AsyncRelayCommand)OpenSnapshotCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DeleteSnapshotCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                ((AsyncRelayCommand)OpenSnapshotCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DeleteSnapshotCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsHistoricalLoaded
    {
        get => _isHistoricalLoaded;
        set => SetProperty(ref _isHistoricalLoaded, value);
    }

    public string HistoricalInfoText
    {
        get => _historicalInfoText;
        set => SetProperty(ref _historicalInfoText, value);
    }

    public async Task RefreshRecentSessionsAsync()
    {
        IsLoading = true;
        try
        {
            var sessions = await _orchestrator.GetRecentSessionsAsync(20).ConfigureAwait(true);
            RecentSessions.Clear();
            foreach (var s in sessions)
            {
                RecentSessions.Add(s);
            }
            StatusMessage = $"{RecentSessions.Count} historical snapshots found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to query snapshots: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OpenSnapshotAsync()
    {
        if (_selectedSession == null) return;

        IsLoading = true;
        StatusMessage = "Loading historical snapshot from local database...";
        try
        {
            var snapshot = await _orchestrator.LoadSnapshotAsync(_selectedSession.Id).ConfigureAwait(true);
            if (snapshot != null)
            {
                IsHistoricalLoaded = true;
                HistoricalInfoText = $"Historical snapshot from {_selectedSession.StartedAtUtc:yyyy-MM-dd HH:mm:ss} UTC. Volume: {_selectedSession.VolumeRoot}. (Live filesystem state may have changed).";
                _onSnapshotLoaded(snapshot);
                StatusMessage = "Snapshot loaded successfully.";
            }
            else
            {
                StatusMessage = "Snapshot data was not found in the database.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load snapshot: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DeleteSnapshotAsync()
    {
        if (_selectedSession == null) return;

        var sessionId = _selectedSession.Id;
        IsLoading = true;
        try
        {
            // Deletes ONLY from app-owned SQLite database
            var removed = await _orchestrator.RemoveSessionAsync(sessionId).ConfigureAwait(true);
            if (removed)
            {
                RecentSessions.Remove(_selectedSession);
                SelectedSession = null;
                StatusMessage = "Snapshot deleted from local database.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete snapshot: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
