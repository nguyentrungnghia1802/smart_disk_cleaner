namespace SmartDiskCleaner.App.ViewModels;

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using SmartDiskCleaner.App.Mvvm;
using SmartDiskCleaner.Domain;

public sealed class NodeDetailsViewModel : ObservableObject
{
    private ScanNode? _currentNode;
    private string _notificationMessage = "";
    private bool _hasNotification;

    public NodeDetailsViewModel()
    {
        CopyPathCommand = new RelayCommand(CopyPath, () => _currentNode != null);
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer, () => _currentNode != null);
    }

    public ICommand CopyPathCommand { get; }
    public ICommand OpenInExplorerCommand { get; }

    public bool HasSelectedNode => _currentNode != null;
    public string PathText => _currentNode?.FullPath ?? "—";
    public string NameText => _currentNode?.Name ?? "—";
    public string TypeText => _currentNode?.NodeType.ToString() ?? "—";

    public string LogicalSizeText => _currentNode != null
        ? SummaryViewModel.FormatBytes(_currentNode.NodeType == NodeType.Directory ? _currentNode.RecursiveLogicalBytes : _currentNode.LogicalBytes)
        : "—";

    public string AllocatedSizeText => _currentNode != null
        ? (_currentNode.NodeType == NodeType.Directory
            ? (_currentNode.RecursiveAllocatedBytes.HasValue ? SummaryViewModel.FormatBytes(_currentNode.RecursiveAllocatedBytes.Value) : "Unknown")
            : (_currentNode.AllocatedBytes.HasValue ? SummaryViewModel.FormatBytes(_currentNode.AllocatedBytes.Value) : "Unknown"))
        : "—";

    public string UniqueAllocatedSizeText => _currentNode != null
        ? (_currentNode.NodeType == NodeType.Directory
            ? (_currentNode.RecursiveUniqueAllocatedBytes.HasValue ? SummaryViewModel.FormatBytes(_currentNode.RecursiveUniqueAllocatedBytes.Value) : "Unknown")
            : (_currentNode.UniqueAllocatedBytes.HasValue ? SummaryViewModel.FormatBytes(_currentNode.UniqueAllocatedBytes.Value) : "Unknown"))
        : "—";

    public string CreatedText => _currentNode?.CreatedUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "—";
    public string ModifiedText => _currentNode?.ModifiedUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "—";
    public string LastAccessText => _currentNode?.LastAccessUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "—";
    public string AttributesText => _currentNode?.Attributes.ToString() ?? "—";

    public string PhysicalIdentityText => _currentNode?.PhysicalIdentity?.ToString() ?? "—";
    public string LinkCountText => _currentNode?.LinkCount?.ToString() ?? "—";
    public string ReparseTagText => _currentNode?.ReparseTag.HasValue == true ? $"0x{_currentNode.ReparseTag.Value:X8}" : "—";

    public string CategoryText => _currentNode?.Classification.Category.ToString() ?? "—";
    public string RiskText => _currentNode?.Classification.Risk.ToString() ?? "—";
    public string ConfidenceText => _currentNode?.Classification.Confidence.ToString() ?? "—";
    public string PrimaryReasonText => _currentNode?.Classification.PrimaryReason ?? "—";
    public string MatchedRulesText => _currentNode != null && _currentNode.Classification.MatchedRuleIds.Count > 0
        ? string.Join(", ", _currentNode.Classification.MatchedRuleIds)
        : "—";

    public string NotificationMessage
    {
        get => _notificationMessage;
        set
        {
            if (SetProperty(ref _notificationMessage, value))
            {
                HasNotification = !string.IsNullOrEmpty(value);
            }
        }
    }

    public bool HasNotification
    {
        get => _hasNotification;
        private set => SetProperty(ref _hasNotification, value);
    }

    public void SetNode(ScanNode? node)
    {
        _currentNode = node;
        NotificationMessage = "";

        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(NameText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(LogicalSizeText));
        OnPropertyChanged(nameof(AllocatedSizeText));
        OnPropertyChanged(nameof(UniqueAllocatedSizeText));
        OnPropertyChanged(nameof(CreatedText));
        OnPropertyChanged(nameof(ModifiedText));
        OnPropertyChanged(nameof(LastAccessText));
        OnPropertyChanged(nameof(AttributesText));
        OnPropertyChanged(nameof(PhysicalIdentityText));
        OnPropertyChanged(nameof(LinkCountText));
        OnPropertyChanged(nameof(ReparseTagText));
        OnPropertyChanged(nameof(CategoryText));
        OnPropertyChanged(nameof(RiskText));
        OnPropertyChanged(nameof(ConfidenceText));
        OnPropertyChanged(nameof(PrimaryReasonText));
        OnPropertyChanged(nameof(MatchedRulesText));

        ((RelayCommand)CopyPathCommand).NotifyCanExecuteChanged();
        ((RelayCommand)OpenInExplorerCommand).NotifyCanExecuteChanged();
    }

    private void CopyPath()
    {
        if (_currentNode == null) return;
        try
        {
            Clipboard.SetText(_currentNode.FullPath);
            NotificationMessage = "Path copied to clipboard.";
        }
        catch (Exception ex)
        {
            NotificationMessage = $"Failed to copy path: {ex.Message}";
        }
    }

    private void OpenInExplorer()
    {
        if (_currentNode == null) return;
        var path = _currentNode.FullPath;

        try
        {
            if (_currentNode.NodeType == NodeType.File)
            {
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                    NotificationMessage = "Selected in Explorer.";
                }
                else
                {
                    NotificationMessage = "File no longer exists on disk (vanished).";
                }
            }
            else
            {
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", $"\"{path}\"");
                    NotificationMessage = "Opened folder in Explorer.";
                }
                else
                {
                    NotificationMessage = "Directory no longer exists on disk (vanished).";
                }
            }
        }
        catch (Exception ex)
        {
            NotificationMessage = $"Could not open Explorer: {ex.Message}";
        }
    }
}
