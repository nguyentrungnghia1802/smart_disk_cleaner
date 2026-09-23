namespace SmartDiskCleaner.App.ViewModels;

using System.Collections.ObjectModel;
using System.IO;
using SmartDiskCleaner.App.Mvvm;

public sealed class DriveItemViewModel : ObservableObject
{
    public DriveItemViewModel(string rootPath, string volumeLabel, long totalBytes, long freeBytes, bool isReady)
    {
        RootPath = rootPath;
        VolumeLabel = string.IsNullOrWhiteSpace(volumeLabel) ? "Local Disk" : volumeLabel;
        TotalBytes = totalBytes;
        FreeBytes = freeBytes;
        IsReady = isReady;
    }

    public string RootPath { get; }
    public string VolumeLabel { get; }
    public long TotalBytes { get; }
    public long FreeBytes { get; }
    public bool IsReady { get; }

    public string DisplayText
    {
        get
        {
            if (!IsReady || TotalBytes <= 0)
            {
                return $"{RootPath} ({VolumeLabel}) [Not Ready]";
            }

            var freeGb = FreeBytes / (1024.0 * 1024.0 * 1024.0);
            var totalGb = TotalBytes / (1024.0 * 1024.0 * 1024.0);
            return $"{RootPath} ({VolumeLabel}) - {freeGb:F1} GB free / {totalGb:F1} GB";
        }
    }
}

public sealed class DriveSelectorViewModel : ObservableObject
{
    private DriveItemViewModel? _selectedDrive;

    public DriveSelectorViewModel()
    {
        AvailableDrives = new ObservableCollection<DriveItemViewModel>();
        RefreshDrives();
    }

    public ObservableCollection<DriveItemViewModel> AvailableDrives { get; }

    public DriveItemViewModel? SelectedDrive
    {
        get => _selectedDrive;
        set => SetProperty(ref _selectedDrive, value);
    }

    public void RefreshDrives()
    {
        AvailableDrives.Clear();
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var d in drives)
            {
                try
                {
                    var isReady = d.IsReady;
                    var label = isReady ? d.VolumeLabel : "";
                    var total = isReady ? d.TotalSize : 0;
                    var free = isReady ? d.AvailableFreeSpace : 0;
                    var item = new DriveItemViewModel(d.Name, label, total, free, isReady);
                    AvailableDrives.Add(item);
                }
                catch
                {
                    // Ignore drives with I/O errors during enumeration
                }
            }
        }
        catch
        {
            // Fallback default C:\
            AvailableDrives.Add(new DriveItemViewModel("C:\\", "System", 0, 0, true));
        }

        if (AvailableDrives.Count > 0 && SelectedDrive == null)
        {
            SelectedDrive = AvailableDrives.FirstOrDefault(d => d.RootPath.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                ?? AvailableDrives[0];
        }
    }
}
