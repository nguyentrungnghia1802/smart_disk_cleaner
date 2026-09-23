namespace SmartDiskCleaner.Infrastructure;

using SmartDiskCleaner.Domain;

public sealed class WindowsAppPathProvider : IAppPathProvider
{
    private readonly string _appDataRoot;

    public WindowsAppPathProvider(string? customRoot = null)
    {
        _appDataRoot = !string.IsNullOrWhiteSpace(customRoot)
            ? customRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartDiskCleaner");
    }

    public string AppDataRoot => _appDataRoot;
}
