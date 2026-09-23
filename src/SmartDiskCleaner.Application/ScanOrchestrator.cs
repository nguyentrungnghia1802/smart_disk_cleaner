namespace SmartDiskCleaner.Application;

using SmartDiskCleaner.Domain;

public sealed record ScanExecutionResult(
    ScanSession Session,
    ScanSnapshot? Snapshot,
    bool SavedToRepository,
    string? PersistenceError = null)
{
    public bool IsSuccess => Session.Status == SessionStatus.Completed && Snapshot != null;
}

public interface IScanOrchestrator
{
    Task<ScanExecutionResult> ExecuteScanAsync(
        string volumeRoot,
        string optionsVersion,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);

    Task<ScanSnapshot?> LoadSnapshotAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScanSession>> GetRecentSessionsAsync(int maxCount = 20, CancellationToken cancellationToken = default);
    Task<bool> RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

public sealed class ScanOrchestrator : IScanOrchestrator
{
    private readonly IScanPipeline _pipeline;
    private readonly IScanSnapshotRepository _repository;
    private readonly IVolumeInfoProvider _volumeInfoProvider;
    private readonly IAppLogger _logger;

    public ScanOrchestrator(
        IScanPipeline pipeline,
        IScanSnapshotRepository repository,
        IVolumeInfoProvider volumeInfoProvider,
        IAppLogger logger)
    {
        _pipeline = pipeline;
        _repository = repository;
        _volumeInfoProvider = volumeInfoProvider;
        _logger = logger;
    }

    public async Task<ScanExecutionResult> ExecuteScanAsync(
        string volumeRoot,
        string optionsVersion,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("ScanStart", $"Starting scan for volume root '{volumeRoot}'");

        try
        {
            var volume = _volumeInfoProvider.ValidateAndGet(volumeRoot);
            if (!volume.IsReady)
            {
                _logger.LogWarning("VolumeCheck", $"Volume is not ready: '{volumeRoot}'", volumeRoot);
                throw new ScanValidationException("VOLUME_NOT_READY", $"Volume '{volumeRoot}' is not ready for scanning.");
            }
        }
        catch (Exception ex) when (ex is not ScanValidationException)
        {
            _logger.LogError("VolumeValidation", $"Failed validating volume root '{volumeRoot}'", ex);
            throw new ScanValidationException("VOLUME_ACCESS_FAILED", $"Could not access volume '{volumeRoot}': {ex.Message}", ex);
        }

        var pipelineResult = await _pipeline.RunAsync(volumeRoot, optionsVersion, progress, cancellationToken)
            .ConfigureAwait(false);

        if (!pipelineResult.IsCompleted || pipelineResult.Snapshot == null)
        {
            _logger.LogInformation("ScanFinished", $"Scan ended with status '{pipelineResult.Session.Status}'", pipelineResult.Session.Id);
            return new ScanExecutionResult(pipelineResult.Session, null, false);
        }

        // Save snapshot to repository
        var saved = false;
        string? persistError = null;
        try
        {
            _logger.LogInformation("Persistence", "Persisting snapshot to repository...", pipelineResult.Session.Id);
            await _repository.SaveSnapshotAsync(pipelineResult.Snapshot, cancellationToken).ConfigureAwait(false);
            saved = true;
            _logger.LogInformation("Persistence", "Snapshot saved successfully.", pipelineResult.Session.Id);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Persistence", "Snapshot persistence was canceled.", null, pipelineResult.Session.Id);
            persistError = "Persistence canceled.";
        }
        catch (Exception ex)
        {
            _logger.LogError("Persistence", "Failed to persist snapshot.", ex, pipelineResult.Session.Id);
            persistError = $"Failed to save snapshot: {ex.Message}";
        }

        return new ScanExecutionResult(pipelineResult.Session, pipelineResult.Snapshot, saved, persistError);
    }

    public Task<ScanSnapshot?> LoadSnapshotAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("LoadSnapshot", $"Loading snapshot from repository for session {sessionId}", sessionId);
        return _repository.LoadSnapshotAsync(sessionId, cancellationToken);
    }

    public Task<IReadOnlyList<ScanSession>> GetRecentSessionsAsync(int maxCount = 20, CancellationToken cancellationToken = default)
    {
        return _repository.GetRecentSessionsAsync(maxCount, cancellationToken);
    }

    public Task<bool> RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RemoveSession", $"Removing session {sessionId} from repository database", sessionId);
        return _repository.RemoveSessionAsync(sessionId, cancellationToken);
    }
}
