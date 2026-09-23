using SmartDiskCleaner.Domain;
using Xunit;

namespace SmartDiskCleaner.Domain.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DomainAssembly_HasNoUiInfrastructureSqliteOrNativeDependency()
    {
        var references = typeof(ScanNode).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain(references, name => name is not null && name.Contains("PresentationFramework", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name is not null && name.Contains("Infrastructure", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name is not null && name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoreContracts_ExposeNoDeletionOrMutationService()
    {
        var publicTypeNames = typeof(ScanNode).Assembly.GetExportedTypes().Select(type => type.Name).ToArray();
        Assert.DoesNotContain(publicTypeNames, name =>
            name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Move", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("CleanupService", StringComparison.OrdinalIgnoreCase));
    }
}
