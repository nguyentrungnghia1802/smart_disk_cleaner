namespace SmartDiskCleaner.Infrastructure;

using System.Text.Json;
using SmartDiskCleaner.Domain;

public sealed class JsonAppConfigService : IAppConfigService
{
    private readonly string _configFilePath;
    private readonly object _lock = new();
    private AppConfig _current = new();

    public JsonAppConfigService(IAppPathProvider pathProvider)
    {
        _configFilePath = Path.Combine(pathProvider.AppDataRoot, "config.json");
    }

    public AppConfig Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var loaded = JsonSerializer.Deserialize<AppConfigDto>(json, SerializerOptions);
                    if (loaded != null && Validate(loaded, out var validConfig))
                    {
                        _current = validConfig;
                        return Task.CompletedTask;
                    }
                }
            }
            catch
            {
                // Fall back safely to defaults
            }

            _current = new AppConfig();
        }

        return Task.CompletedTask;
    }

    public Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var dto = new AppConfigDto(
                    config.Version,
                    config.SelectedDrive,
                    config.ChildThreshold,
                    config.MaxRecentSnapshots,
                    config.LargestFolderRatio,
                    config.MinSizeFilterBytes);

                var json = JsonSerializer.Serialize(dto, SerializerOptions);
                File.WriteAllText(_configFilePath, json);
                _current = config;
            }
            catch
            {
                // Never crash callers on persistence failures
            }
        }

        return Task.CompletedTask;
    }

    private static bool Validate(AppConfigDto dto, out AppConfig config)
    {
        var childThreshold = dto.ChildThreshold > 0 ? dto.ChildThreshold : 10;
        var largestFolderRatio = dto.LargestFolderRatio > 0 && dto.LargestFolderRatio <= 1.0 ? dto.LargestFolderRatio : 0.40;
        var maxRecentSnapshots = dto.MaxRecentSnapshots > 0 ? dto.MaxRecentSnapshots : 10;
        var minSizeBytes = Math.Max(0, dto.MinSizeFilterBytes);
        var drive = !string.IsNullOrWhiteSpace(dto.SelectedDrive) ? dto.SelectedDrive : "C:\\";
        var version = !string.IsNullOrWhiteSpace(dto.Version) ? dto.Version : "1";

        config = new AppConfig(
            version,
            drive,
            maxRecentSnapshots,
            childThreshold,
            largestFolderRatio,
            minSizeBytes);

        return true;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private sealed record AppConfigDto(
        string? Version,
        string? SelectedDrive,
        int ChildThreshold,
        int MaxRecentSnapshots,
        double LargestFolderRatio,
        long MinSizeFilterBytes);
}
