using SmartDiskCleaner.Domain;
using Xunit;

namespace SmartDiskCleaner.Domain.Tests;

public sealed class AdaptiveTreePolicyTests
{
    [Theory]
    [InlineData(5, 5, 5)]
    [InlineData(6, 5, 2)]
    [InlineData(10, 1, 1)]
    [InlineData(11, 0, 0)]
    [InlineData(10, 6, 3)]
    [InlineData(0, 11, 5)]
    [InlineData(0, 25, 10)]
    public void ExactThresholdTable(int fileCount, int directoryCount, int expectedExpanded)
    {
        var children = Enumerable.Range(0, fileCount)
            .Select(i => TestNodeFactory.File(1000 + i, $@"C:\root\file-{i}.bin"))
            .Concat(Enumerable.Range(0, directoryCount)
                .Select(i => TestNodeFactory.Directory(2000 + i, $@"C:\root\dir-{i}", i, i, i)))
            .ToArray();

        var decision = new AdaptiveTreePolicy().Evaluate(children);

        Assert.Equal(expectedExpanded, decision.AutoExpandDirectoryIds.Count);
        Assert.Equal(fileCount + directoryCount, decision.VisibleChildIds.Count);
        Assert.Equal(directoryCount, decision.AutoExpandDirectoryIds.Count + decision.CollapsedDirectoryIds.Count);
        Assert.Equal(fileCount + directoryCount > 10, decision.ThresholdTriggered);
    }

    [Fact]
    public void EqualSizes_UseCaseInsensitiveThenOrdinalPathTieBreaker()
    {
        var children = new[]
        {
            TestNodeFactory.Directory(5, @"C:\root\zeta", 10, 10, 10),
            TestNodeFactory.Directory(4, @"C:\root\Alpha", 10, 10, 10),
            TestNodeFactory.Directory(3, @"C:\root\beta", 10, 10, 10)
        }.Concat(Enumerable.Range(0, 8).Select(i => TestNodeFactory.File(100 + i, $@"C:\root\f{i}"))).ToArray();

        var decision = new AdaptiveTreePolicy().Evaluate(children);

        Assert.Equal(2, decision.AutoExpandDirectoryIds.Count);
        Assert.Equal(new long[] { 4, 3 }, decision.AutoExpandDirectoryIds);
    }

    [Fact]
    public void RankingFallsBackForTheWholeSiblingSet()
    {
        var children = new[]
        {
            TestNodeFactory.Directory(2, @"C:\root\logical-large", 100, 1, 1),
            TestNodeFactory.Directory(3, @"C:\root\unique-unknown", 10, 100, null),
            TestNodeFactory.Directory(4, @"C:\root\small", 1, 1, 1)
        }.Concat(Enumerable.Range(0, 8).Select(i => TestNodeFactory.File(100 + i, $@"C:\root\f{i}"))).ToArray();

        var decision = new AdaptiveTreePolicy().Evaluate(children);

        Assert.Equal(RankingMetricKind.AllocatedRecursive, decision.RankingMetric);
        Assert.Equal(new long[] { 3, 2 }, decision.AutoExpandDirectoryIds);
    }

    [Fact]
    public void LogicalFallbackAndManualVisibility_DoNotPruneCollapsedBranches()
    {
        var children = Enumerable.Range(0, 11)
            .Select(i => TestNodeFactory.Directory(i + 2, $@"C:\root\d{i:D2}", i, null, null))
            .ToArray();

        var decision = new AdaptiveTreePolicy().Evaluate(children);

        Assert.Equal(RankingMetricKind.LogicalRecursive, decision.RankingMetric);
        Assert.Equal(11, decision.VisibleChildIds.Count);
        Assert.Equal(5, decision.AutoExpandDirectoryIds.Count);
        Assert.Equal(6, decision.CollapsedDirectoryIds.Count);
        Assert.All(decision.CollapsedDirectoryIds, id => Assert.Contains(id, decision.VisibleChildIds));
    }
}
