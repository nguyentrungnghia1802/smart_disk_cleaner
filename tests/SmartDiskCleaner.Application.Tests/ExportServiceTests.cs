namespace SmartDiskCleaner.Application.Tests;

using System.Text.Json;
using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;
using Xunit;

public sealed class ExportServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ExportService _exportService = new();

    public ExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SmartDiskCleaner-ExportTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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

    private static ScanSnapshot CreateTestSnapshot()
    {
        var session = new ScanSession(
            Guid.NewGuid(), @"C:\root", "V1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            SessionStatus.Completed, "1", "1");

        var file1 = new ScanNode(
            1, null, 0, @"C:\root\test,with""quotes"".txt", "test,with\"quotes\".txt", NodeType.File, FileAttributes.Normal,
            100, 128, 128, null, 1, null, null, null, null,
            MetadataFlags.None, true, AccountingConfidence.ExactWithinCapturedMetadata,
            DirectoryAggregate.Empty,
            new ClassificationResult(CleanupCategory.ReviewRecommended, RiskLevel.Medium, ConfidenceLevel.High,
                RecommendationCode.ReviewGenericCandidate, "GENERIC", ["R1"], false, 128, EvidenceFlags.None, "1"));

        var file2 = new ScanNode(
            2, null, 0, @"C:\root\unicode_ tiếng_việt_ 🚀.bin", "unicode_ tiếng_việt_ 🚀.bin", NodeType.File, FileAttributes.Normal,
            200, null, null, null, 1, null, null, null, null,
            MetadataFlags.MetadataPartial, true, AccountingConfidence.Partial,
            DirectoryAggregate.Empty,
            ClassificationResult.Unknown("1"));

        var warnings = new[]
        {
            new ScanWarning(1, session.Id, @"C:\root\warn", "Op", "WARN_CODE", "Warning message", null, DateTimeOffset.UtcNow, WarningSeverity.Warning, true)
        };

        var metrics = new ScanMetrics(2, 2, 0, 1, 300, 128, null, 128, null, AccountingConfidence.Partial, new Dictionary<CleanupCategory, long>());
        return new ScanSnapshot(session, [file1, file2], warnings, metrics, new Dictionary<long, PresentationDecision>());
    }

    [Fact]
    public async Task ExportCsv_HandlesEscapingQuotesAndUnicodeProperly()
    {
        var snapshot = CreateTestSnapshot();
        var exportPath = Path.Combine(_tempDir, "export.csv");

        await _exportService.ExportAsync(snapshot, exportPath, ExportFormat.Csv);

        Assert.True(File.Exists(exportPath));
        var lines = await File.ReadAllLinesAsync(exportPath);
        Assert.True(lines.Length >= 3); // Header + 2 rows

        // Check header
        Assert.Contains("FullPath,Name,NodeType", lines[0]);

        // Check escaping of quotes and comma
        var file1Line = lines.First(l => l.Contains("test,with"));
        Assert.Contains("\"C:\\root\\test,with\"\"quotes\"\".txt\"", file1Line);

        // Check Unicode
        var file2Line = lines.First(l => l.Contains("tiếng_việt"));
        Assert.Contains("tiếng_việt_ 🚀", file2Line);
    }

    [Fact]
    public async Task ExportJson_CreatesValidJsonWithCompleteSnapshotFacts()
    {
        var snapshot = CreateTestSnapshot();
        var exportPath = Path.Combine(_tempDir, "export.json");

        await _exportService.ExportAsync(snapshot, exportPath, ExportFormat.Json);

        Assert.True(File.Exists(exportPath));
        var json = await File.ReadAllTextAsync(exportPath);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(@"C:\root", doc.RootElement.GetProperty("Session").GetProperty("VolumeRoot").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("Nodes").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("Warnings").GetArrayLength());
    }

    [Fact]
    public async Task Export_ThrowsOnInvalidDestinationDirectory()
    {
        var snapshot = CreateTestSnapshot();
        var invalidPath = Path.Combine(@"Z:\NonExistentDirectory_9999\export.csv");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _exportService.ExportAsync(snapshot, invalidPath, ExportFormat.Csv));
    }
}
