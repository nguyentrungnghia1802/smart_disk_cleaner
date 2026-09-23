using SmartDiskCleaner.Domain;

namespace SmartDiskCleaner.Application;

public sealed class ScanPipeline : IScanPipeline
{
    private readonly IFileSystemScanner _scanner;
    private readonly StorageAccountingService _accounting;
    private readonly DirectoryAggregationService _aggregation;
    private readonly IClassificationService _classification;
    private readonly AdaptiveTreePolicy _treePolicy;
    private readonly IClock _clock;

    public ScanPipeline(
        IFileSystemScanner scanner,
        StorageAccountingService accounting,
        DirectoryAggregationService aggregation,
        IClassificationService classification,
        AdaptiveTreePolicy treePolicy,
        IClock clock)
    {
        _scanner = scanner;
        _accounting = accounting;
        _aggregation = aggregation;
        _classification = classification;
        _treePolicy = treePolicy;
        _clock = clock;
    }

    public async Task<ScanPipelineResult> RunAsync(
        string volumeRoot,
        string optionsVersion,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionsVersion);
        var sessionId = Guid.NewGuid();
        var started = _clock.UtcNow;
        var initial = new ScanSession(sessionId, volumeRoot, null, started, null, SessionStatus.Created,
            optionsVersion, _classification.RuleSetVersion);

        try
        {
            progress?.Report(new ScanProgress(ScanProgressStage.Validating, 0, 0));
            cancellationToken.ThrowIfCancellationRequested();
            var scanned = await _scanner.ScanAsync(new ScanRequest(sessionId, volumeRoot, optionsVersion), progress, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new ScanProgress(ScanProgressStage.Accounting, scanned.Nodes.Count, scanned.Warnings.Count));
            var canonical = RawNodeCanonicalizer.Canonicalize(scanned.Nodes);
            var accounted = _accounting.Resolve(sessionId, canonical, _clock.UtcNow, cancellationToken);

            progress?.Report(new ScanProgress(ScanProgressStage.Aggregating, accounted.Nodes.Count,
                scanned.Warnings.Count + accounted.Warnings.Count));
            var aggregated = _aggregation.Aggregate(accounted.Nodes, cancellationToken);

            progress?.Report(new ScanProgress(ScanProgressStage.Classifying, aggregated.Count,
                scanned.Warnings.Count + accounted.Warnings.Count));
            var evaluationTime = _clock.UtcNow;
            var classified = new ScanNode[aggregated.Count];
            for (var index = 0; index < aggregated.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = aggregated[index];
                classified[index] = node with { Classification = _classification.Classify(node, evaluationTime) };
            }

            progress?.Report(new ScanProgress(ScanProgressStage.BuildingSnapshot, classified.Length,
                scanned.Warnings.Count + accounted.Warnings.Count));
            var presentation = BuildPresentationDecisions(classified, cancellationToken);
            var warnings = CanonicalizeWarnings(scanned.Warnings.Concat(accounted.Warnings), sessionId);
            var metrics = BuildMetrics(classified, warnings.Count);
            var completed = initial with
            {
                VolumeRoot = scanned.Volume.RootPath,
                VolumeIdentity = scanned.Volume.VolumeIdentity,
                CompletedAtUtc = _clock.UtcNow,
                Status = SessionStatus.Completed
            };
            var snapshot = new ScanSnapshot(completed, classified, warnings, metrics, presentation);
            progress?.Report(new ScanProgress(ScanProgressStage.Completed, classified.Length, warnings.Count));
            return new ScanPipelineResult(completed, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancelled = initial with { CompletedAtUtc = _clock.UtcNow, Status = SessionStatus.Cancelled };
            progress?.Report(new ScanProgress(ScanProgressStage.Cancelled, 0, 0));
            return new ScanPipelineResult(cancelled, null);
        }
        catch (Exception exception)
        {
            var code = exception is ScanValidationException validation ? validation.Code : exception.GetType().Name;
            var failed = initial with { CompletedAtUtc = _clock.UtcNow, Status = SessionStatus.Failed, FailureCode = code };
            return new ScanPipelineResult(failed, null);
        }
    }

    private IReadOnlyDictionary<long, PresentationDecision> BuildPresentationDecisions(
        IReadOnlyList<ScanNode> nodes,
        CancellationToken cancellationToken)
    {
        var children = nodes.GroupBy(n => n.ParentId ?? 0L).ToDictionary(g => g.Key, g => g.ToArray());
        var result = new Dictionary<long, PresentationDecision>();
        foreach (var directory in nodes.Where(n => n.NodeType == NodeType.Directory).OrderBy(n => n.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[directory.Id] = _treePolicy.Evaluate(children.GetValueOrDefault(directory.Id) ?? Array.Empty<ScanNode>());
        }

        return result;
    }

    private static IReadOnlyList<ScanWarning> CanonicalizeWarnings(IEnumerable<ScanWarning> warnings, Guid sessionId) =>
        warnings
            .OrderBy(w => w.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Path, StringComparer.Ordinal)
            .ThenBy(w => w.Operation, StringComparer.Ordinal)
            .ThenBy(w => w.Code, StringComparer.Ordinal)
            .Select((warning, index) => warning with { Id = index + 1, SessionId = sessionId })
            .ToArray();

    private static ScanMetrics BuildMetrics(IReadOnlyList<ScanNode> nodes, int warningCount)
    {
        var roots = nodes.Where(n => n.ParentId is null).ToArray();
        long logical = 0;
        long knownAllocated = 0;
        long knownUnique = 0;
        checked
        {
            foreach (var root in roots)
            {
                logical += root.RecursiveLogicalBytes;
                knownAllocated += root.NodeType == NodeType.Directory ? root.Aggregate.KnownAllocatedBytesRecursive : root.AllocatedBytes ?? 0;
                knownUnique += root.NodeType == NodeType.Directory ? root.Aggregate.KnownUniqueAllocatedBytesRecursive : root.UniqueAllocatedBytes ?? 0;
            }
        }

        var allocatedComplete = roots.All(r => r.RecursiveAllocatedBytes.HasValue);
        var uniqueComplete = roots.All(r => r.RecursiveUniqueAllocatedBytes.HasValue);
        var files = nodes.Where(n => n.NodeType == NodeType.File).ToArray();
        var confidence = uniqueComplete
            ? AccountingConfidence.ExactWithinCapturedMetadata
            : files.Length > 0 && files.All(f => f.AccountingConfidence == AccountingConfidence.Unsupported)
                ? AccountingConfidence.Unsupported
                : AccountingConfidence.Partial;
        var candidateTotals = files
            .Where(n => !n.Classification.IsProtected && n.UniqueAllocatedBytes.HasValue)
            .GroupBy(n => n.Classification.Category)
            .ToDictionary(g => g.Key, g => g.Aggregate(0L, (sum, node) => checked(sum + node.UniqueAllocatedBytes!.Value)));

        return new ScanMetrics(
            nodes.Count,
            files.Length,
            nodes.Count - files.Length,
            warningCount,
            logical,
            knownAllocated,
            allocatedComplete ? knownAllocated : null,
            knownUnique,
            uniqueComplete ? knownUnique : null,
            confidence,
            candidateTotals);
    }
}
