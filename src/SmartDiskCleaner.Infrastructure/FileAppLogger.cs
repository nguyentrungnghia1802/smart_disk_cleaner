namespace SmartDiskCleaner.Infrastructure;

using System.Globalization;
using System.Text;
using SmartDiskCleaner.Domain;

public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logDirectory;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxRetainedFiles;
    private readonly object _lock = new();

    public FileAppLogger(IAppPathProvider pathProvider, long maxFileSizeBytes = 5 * 1024 * 1024, int maxRetainedFiles = 5)
    {
        _logDirectory = Path.Combine(pathProvider.AppDataRoot, "logs");
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxRetainedFiles = maxRetainedFiles;
    }

    public void LogInformation(string operation, string message, Guid? sessionId = null) =>
        WriteEntry("INFO", operation, message, null, null, sessionId);

    public void LogWarning(string operation, string message, string? path = null, Guid? sessionId = null) =>
        WriteEntry("WARN", operation, message, path, null, sessionId);

    public void LogError(string operation, string message, Exception? exception = null, Guid? sessionId = null) =>
        WriteEntry("ERROR", operation, message, null, exception, sessionId);

    private void WriteEntry(string level, string operation, string message, string? path, Exception? exception, Guid? sessionId)
    {
        try
        {
            lock (_lock)
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                EnforceRetention();

                var fileName = $"app-{DateTime.UtcNow:yyyyMMdd}.log";
                var filePath = Path.Combine(_logDirectory, fileName);

                if (File.Exists(filePath) && new FileInfo(filePath).Length >= _maxFileSizeBytes)
                {
                    var rolledName = $"app-{DateTime.UtcNow:yyyyMMdd}-{DateTime.UtcNow:HHmmss}.log";
                    filePath = Path.Combine(_logDirectory, rolledName);
                }

                var sb = new StringBuilder();
                sb.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                  .Append(" [").Append(level).Append("] ")
                  .Append('[').Append(operation).Append("] ");

                if (sessionId.HasValue)
                {
                    sb.Append("[Session: ").Append(sessionId.Value.ToString("D")).Append("] ");
                }

                if (!string.IsNullOrEmpty(path))
                {
                    sb.Append("[Path: ").Append(path).Append("] ");
                }

                sb.Append(message);

                if (exception != null)
                {
                    sb.AppendLine().Append("Exception: ").Append(exception.GetType().FullName)
                      .Append(": ").Append(exception.Message)
                      .AppendLine().Append(exception.StackTrace);
                }

                sb.AppendLine();
                File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never crash the application or scan tree
        }
    }

    private void EnforceRetention()
    {
        try
        {
            var files = new DirectoryInfo(_logDirectory)
                .GetFiles("app-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(_maxRetainedFiles)
                .ToArray();

            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Ignore transient lock
                }
            }
        }
        catch
        {
            // Ignore directory query failure
        }
    }
}
