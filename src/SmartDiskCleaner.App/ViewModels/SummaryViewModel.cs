namespace SmartDiskCleaner.App.ViewModels;

using SmartDiskCleaner.App.Mvvm;
using SmartDiskCleaner.Domain;

public sealed class SummaryViewModel : ObservableObject
{
    private string _volumeRoot = "—";
    private string _scanDuration = "—";
    private int _fileCount;
    private int _directoryCount;
    private int _warningCount;
    private string _totalLogicalSize = "—";
    private string _totalUniqueAllocatedSize = "—";
    private string _accountingConfidenceText = "—";
    private string _confidenceBadge = "—";

    // Category sizes
    private string _reclaimableSize = "0 B";
    private string _reviewRecommendedSize = "0 B";
    private string _largeFileSize = "0 B";
    private string _protectedSize = "0 B";
    private string _unknownSize = "0 B";

    public string VolumeRoot
    {
        get => _volumeRoot;
        set => SetProperty(ref _volumeRoot, value);
    }

    public string ScanDuration
    {
        get => _scanDuration;
        set => SetProperty(ref _scanDuration, value);
    }

    public int FileCount
    {
        get => _fileCount;
        set => SetProperty(ref _fileCount, value);
    }

    public int DirectoryCount
    {
        get => _directoryCount;
        set => SetProperty(ref _directoryCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        set => SetProperty(ref _warningCount, value);
    }

    public string TotalLogicalSize
    {
        get => _totalLogicalSize;
        set => SetProperty(ref _totalLogicalSize, value);
    }

    public string TotalUniqueAllocatedSize
    {
        get => _totalUniqueAllocatedSize;
        set => SetProperty(ref _totalUniqueAllocatedSize, value);
    }

    public string AccountingConfidenceText
    {
        get => _accountingConfidenceText;
        set => SetProperty(ref _accountingConfidenceText, value);
    }

    public string ConfidenceBadge
    {
        get => _confidenceBadge;
        set => SetProperty(ref _confidenceBadge, value);
    }

    public string ReclaimableSize
    {
        get => _reclaimableSize;
        set => SetProperty(ref _reclaimableSize, value);
    }

    public string ReviewRecommendedSize
    {
        get => _reviewRecommendedSize;
        set => SetProperty(ref _reviewRecommendedSize, value);
    }

    public string LargeFileSize
    {
        get => _largeFileSize;
        set => SetProperty(ref _largeFileSize, value);
    }

    public string ProtectedSize
    {
        get => _protectedSize;
        set => SetProperty(ref _protectedSize, value);
    }

    public string UnknownSize
    {
        get => _unknownSize;
        set => SetProperty(ref _unknownSize, value);
    }

    public void Update(ScanSnapshot snapshot)
    {
        VolumeRoot = snapshot.Session.VolumeRoot;
        var duration = snapshot.Session.CompletedAtUtc.HasValue
            ? snapshot.Session.CompletedAtUtc.Value - snapshot.Session.StartedAtUtc
            : TimeSpan.Zero;
        ScanDuration = $"{(int)duration.TotalMinutes:D2}:{duration.Seconds:D2}.{duration.Milliseconds / 100:D1}";

        FileCount = snapshot.Metrics.FileCount;
        DirectoryCount = snapshot.Metrics.DirectoryCount;
        WarningCount = snapshot.Metrics.WarningCount;

        TotalLogicalSize = FormatBytes(snapshot.Metrics.LogicalBytes);
        TotalUniqueAllocatedSize = snapshot.Metrics.UniqueAllocatedBytes.HasValue
            ? FormatBytes(snapshot.Metrics.UniqueAllocatedBytes.Value)
            : $"{FormatBytes(snapshot.Metrics.KnownUniqueAllocatedBytes)} (partial)";

        AccountingConfidenceText = snapshot.Metrics.AccountingConfidence switch
        {
            AccountingConfidence.ExactWithinCapturedMetadata => "Exact (Captured NTFS metadata)",
            AccountingConfidence.Partial => "Partial (Some file sizes or identities could not be queried)",
            AccountingConfidence.Unsupported => "Unsupported (Non-NTFS or native query unavailable)",
            _ => snapshot.Metrics.AccountingConfidence.ToString()
        };

        ConfidenceBadge = snapshot.Metrics.AccountingConfidence switch
        {
            AccountingConfidence.ExactWithinCapturedMetadata => "[Exact]",
            AccountingConfidence.Partial => "[Partial]",
            AccountingConfidence.Unsupported => "[Unsupported]",
            _ => ""
        };

        // Category breakdown
        var totals = snapshot.Metrics.KnownCandidateAllocatedBytes;
        ReclaimableSize = FormatBytes(totals.GetValueOrDefault(CleanupCategory.HighConfidenceReclaimable, 0));
        ReviewRecommendedSize = FormatBytes(totals.GetValueOrDefault(CleanupCategory.ReviewRecommended, 0));
        LargeFileSize = FormatBytes(totals.GetValueOrDefault(CleanupCategory.LargeFileNotJunk, 0));

        var protectedBytes = snapshot.Nodes
            .Where(n => n.NodeType == NodeType.File && n.Classification.IsProtected)
            .Sum(n => n.UniqueAllocatedBytes ?? n.AllocatedBytes ?? n.LogicalBytes);
        ProtectedSize = FormatBytes(protectedBytes);

        UnknownSize = FormatBytes(totals.GetValueOrDefault(CleanupCategory.Unknown, 0));
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double val = bytes;
        int unitIndex = 0;
        while (val >= 1024.0 && unitIndex < units.Length - 1)
        {
            val /= 1024.0;
            unitIndex++;
        }
        return $"{val:0.##} {units[unitIndex]}";
    }
}
