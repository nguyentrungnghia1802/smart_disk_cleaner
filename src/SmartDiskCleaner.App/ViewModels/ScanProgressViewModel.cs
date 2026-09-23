namespace SmartDiskCleaner.App.ViewModels;

using SmartDiskCleaner.App.Mvvm;
using SmartDiskCleaner.Domain;

public sealed class ScanProgressViewModel : ObservableObject
{
    private string _stageText = "Idle";
    private string _elapsedTimeText = "00:00:00";
    private int _discoveredNodes;
    private int _warningCount;
    private string? _currentPath;
    private bool _isIndeterminate = true;
    private bool _isActive;

    public string StageText
    {
        get => _stageText;
        set => SetProperty(ref _stageText, value);
    }

    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        set => SetProperty(ref _elapsedTimeText, value);
    }

    public int DiscoveredNodes
    {
        get => _discoveredNodes;
        set => SetProperty(ref _discoveredNodes, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        set => SetProperty(ref _warningCount, value);
    }

    public string? CurrentPath
    {
        get => _currentPath;
        set => SetProperty(ref _currentPath, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public void UpdateProgress(ScanProgress progress)
    {
        DiscoveredNodes = progress.DiscoveredNodes;
        WarningCount = progress.WarningCount;
        CurrentPath = progress.CurrentPath;

        StageText = progress.Stage switch
        {
            ScanProgressStage.Validating => "Validating volume and safety boundaries...",
            ScanProgressStage.Scanning => "Scanning filesystem entries (read-only)...",
            ScanProgressStage.Accounting => "Computing NTFS hard-link and storage accounting...",
            ScanProgressStage.Aggregating => "Aggregating directory sizes bottom-up...",
            ScanProgressStage.Classifying => "Evaluating classification rules...",
            ScanProgressStage.BuildingSnapshot => "Building presentation tree...",
            ScanProgressStage.Completed => "Scan analysis completed.",
            ScanProgressStage.Cancelled => "Scan canceled by user.",
            _ => progress.Stage.ToString()
        };
    }

    public void Reset()
    {
        StageText = "Idle";
        ElapsedTimeText = "00:00:00";
        DiscoveredNodes = 0;
        WarningCount = 0;
        CurrentPath = null;
        IsActive = false;
    }
}
