namespace SmartDiskCleaner.App.Mvvm;

public enum AppUiState
{
    Idle,
    Scanning,
    Aggregating,
    Classifying,
    Saving,
    Ready,
    Cancelling,
    Error
}
