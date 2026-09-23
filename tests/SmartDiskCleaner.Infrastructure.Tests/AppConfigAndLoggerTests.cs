namespace SmartDiskCleaner.Infrastructure.Tests;

using SmartDiskCleaner.Domain;
using SmartDiskCleaner.Infrastructure;
using Xunit;

public sealed class AppConfigAndLoggerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WindowsAppPathProvider _pathProvider;

    public AppConfigAndLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SmartDiskCleaner-ConfigLogTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _pathProvider = new WindowsAppPathProvider(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task ConfigService_FallsBackToDefaultsWhenFileNotFound()
    {
        var service = new JsonAppConfigService(_pathProvider);
        await service.LoadAsync();

        Assert.Equal("1", service.Current.Version);
        Assert.Equal("C:\\", service.Current.SelectedDrive);
        Assert.Equal(10, service.Current.ChildThreshold);
        Assert.Equal(0.40, service.Current.LargestFolderRatio);
    }

    [Fact]
    public async Task ConfigService_SavesAndReloadsValidConfig()
    {
        var service = new JsonAppConfigService(_pathProvider);
        var custom = new AppConfig("1", "D:\\", 20, 15, 0.50, 1024);

        await service.SaveAsync(custom);

        var service2 = new JsonAppConfigService(_pathProvider);
        await service2.LoadAsync();

        Assert.Equal("D:\\", service2.Current.SelectedDrive);
        Assert.Equal(20, service2.Current.MaxRecentSnapshots);
        Assert.Equal(15, service2.Current.ChildThreshold);
        Assert.Equal(0.50, service2.Current.LargestFolderRatio);
        Assert.Equal(1024, service2.Current.MinSizeFilterBytes);
    }

    [Fact]
    public async Task ConfigService_FallsBackToDefaultsOnInvalidJson()
    {
        var configFile = Path.Combine(_tempDir, "config.json");
        await File.WriteAllTextAsync(configFile, "{ invalid json corrupt content }");

        var service = new JsonAppConfigService(_pathProvider);
        await service.LoadAsync();

        Assert.Equal("1", service.Current.Version);
        Assert.Equal("C:\\", service.Current.SelectedDrive);
    }

    [Fact]
    public void FileLogger_WritesStructuredLogFileAndDoesNotThrow()
    {
        var logger = new FileAppLogger(_pathProvider);
        logger.LogInformation("TestOp", "Information message");
        logger.LogWarning("TestOp", "Warning message", @"C:\some\path");
        logger.LogError("TestOp", "Error message", new InvalidOperationException("Test exception"));

        var logsDir = Path.Combine(_tempDir, "logs");
        Assert.True(Directory.Exists(logsDir));

        var logFiles = Directory.GetFiles(logsDir, "app-*.log");
        Assert.NotEmpty(logFiles);

        var content = File.ReadAllText(logFiles[0]);
        Assert.Contains("[INFO]", content);
        Assert.Contains("[WARN]", content);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("Test exception", content);
    }
}
