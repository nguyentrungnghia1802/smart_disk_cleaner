namespace SmartDiskCleaner.Application;

using System.Globalization;
using System.Text;
using System.Text.Json;
using SmartDiskCleaner.Domain;

public enum ExportFormat
{
    Csv,
    Json
}

public interface IExportService
{
    Task ExportAsync(ScanSnapshot snapshot, string destinationFilePath, ExportFormat format, CancellationToken cancellationToken = default);
}

public sealed class ExportService : IExportService
{
    public async Task ExportAsync(ScanSnapshot snapshot, string destinationFilePath, ExportFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(destinationFilePath))
        {
            throw new ArgumentException("Export destination file path cannot be empty.", nameof(destinationFilePath));
        }

        var dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException($"Destination directory does not exist: '{dir}'");
        }

        switch (format)
        {
            case ExportFormat.Csv:
                await ExportCsvAsync(snapshot, destinationFilePath, cancellationToken).ConfigureAwait(false);
                break;
            case ExportFormat.Json:
                await ExportJsonAsync(snapshot, destinationFilePath, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.");
        }
    }

    private static async Task ExportCsvAsync(ScanSnapshot snapshot, string filePath, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        // CSV Header
        sb.AppendLine("Id,FullPath,Name,NodeType,Depth,LogicalBytes,AllocatedBytes,UniqueAllocatedBytes,RecursiveLogicalBytes,RecursiveAllocatedBytes,RecursiveUniqueAllocatedBytes,Category,Risk,Confidence,Recommendation,PrimaryReason,IsProtected,IsReparsePoint,IsAccessible,MatchedRules,CreatedUtc,ModifiedUtc");

        foreach (var node in snapshot.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = string.Join(',',
                node.Id,
                EscapeCsv(node.FullPath),
                EscapeCsv(node.Name),
                node.NodeType,
                node.Depth,
                node.LogicalBytes,
                node.AllocatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "",
                node.UniqueAllocatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "",
                node.RecursiveLogicalBytes,
                node.RecursiveAllocatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "",
                node.RecursiveUniqueAllocatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "",
                node.Classification.Category,
                node.Classification.Risk,
                node.Classification.Confidence,
                node.Classification.Recommendation,
                EscapeCsv(node.Classification.PrimaryReason ?? ""),
                node.Classification.IsProtected ? "true" : "false",
                node.IsReparsePoint ? "true" : "false",
                node.IsAccessible ? "true" : "false",
                EscapeCsv(string.Join(';', node.Classification.MatchedRuleIds)),
                node.CreatedUtc?.ToString("O") ?? "",
                node.ModifiedUtc?.ToString("O") ?? "");

            sb.AppendLine(line);
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExportJsonAsync(ScanSnapshot snapshot, string filePath, CancellationToken cancellationToken)
    {
        var dto = new
        {
            Session = new
            {
                snapshot.Session.Id,
                snapshot.Session.VolumeRoot,
                snapshot.Session.VolumeIdentity,
                snapshot.Session.StartedAtUtc,
                snapshot.Session.CompletedAtUtc,
                Status = snapshot.Session.Status.ToString(),
                snapshot.Session.OptionsVersion,
                snapshot.Session.RuleSetVersion
            },
            Metrics = new
            {
                snapshot.Metrics.NodeCount,
                snapshot.Metrics.FileCount,
                snapshot.Metrics.DirectoryCount,
                snapshot.Metrics.WarningCount,
                snapshot.Metrics.LogicalBytes,
                snapshot.Metrics.AllocatedBytes,
                snapshot.Metrics.UniqueAllocatedBytes,
                AccountingConfidence = snapshot.Metrics.AccountingConfidence.ToString(),
                CandidateAllocatedBytes = snapshot.Metrics.KnownCandidateAllocatedBytes.ToDictionary(k => k.Key.ToString(), v => v.Value)
            },
            Warnings = snapshot.Warnings.Select(w => new
            {
                w.Id,
                w.Path,
                w.Operation,
                w.Code,
                Severity = w.Severity.ToString(),
                w.MessageSafe,
                w.OccurredAtUtc,
                w.Recoverable
            }),
            Nodes = snapshot.Nodes.Select(n => new
            {
                n.Id,
                n.ParentId,
                n.Depth,
                n.FullPath,
                n.Name,
                NodeType = n.NodeType.ToString(),
                n.LogicalBytes,
                n.AllocatedBytes,
                n.UniqueAllocatedBytes,
                n.RecursiveLogicalBytes,
                n.RecursiveAllocatedBytes,
                n.RecursiveUniqueAllocatedBytes,
                Classification = new
                {
                    Category = n.Classification.Category.ToString(),
                    Risk = n.Classification.Risk.ToString(),
                    Confidence = n.Classification.Confidence.ToString(),
                    Recommendation = n.Classification.Recommendation.ToString(),
                    n.Classification.PrimaryReason,
                    n.Classification.MatchedRuleIds,
                    n.Classification.IsProtected
                },
                n.IsReparsePoint,
                n.IsAccessible,
                n.CreatedUtc,
                n.ModifiedUtc
            })
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, dto, options, cancellationToken).ConfigureAwait(false);
    }

    private static string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
