using SmartDiskCleaner.Domain;

namespace SmartDiskCleaner.Application;

public interface IScanPipeline
{
    Task<ScanPipelineResult> RunAsync(
        string volumeRoot,
        string optionsVersion,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

