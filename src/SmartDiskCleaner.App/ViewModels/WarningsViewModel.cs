namespace SmartDiskCleaner.App.ViewModels;

using System.Collections.ObjectModel;
using SmartDiskCleaner.App.Mvvm;
using SmartDiskCleaner.Domain;

public sealed class WarningsViewModel : ObservableObject
{
    private string _summaryText = "0 warnings recorded";

    public WarningsViewModel()
    {
        Warnings = new ObservableCollection<ScanWarning>();
    }

    public ObservableCollection<ScanWarning> Warnings { get; }

    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    public void UpdateWarnings(IReadOnlyList<ScanWarning> warnings)
    {
        Warnings.Clear();
        foreach (var w in warnings)
        {
            Warnings.Add(w);
        }

        SummaryText = $"{warnings.Count} recoverable warnings recorded during scan.";
    }
}
