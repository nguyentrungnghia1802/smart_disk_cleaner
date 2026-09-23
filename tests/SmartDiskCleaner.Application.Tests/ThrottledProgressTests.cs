namespace SmartDiskCleaner.Application.Tests;

using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;
using Xunit;

public sealed class ThrottledProgressTests
{
    [Fact]
    public void ThrottledProgress_PassesStageTransitionsImmediately()
    {
        var reports = new List<ScanProgress>();
        var throttled = new ThrottledProgress<ScanProgress>(
            p => reports.Add(p),
            TimeSpan.FromSeconds(10), // large throttle interval
            alwaysReportPredicate: p => p.Stage == ScanProgressStage.Completed);

        throttled.Report(new ScanProgress(ScanProgressStage.Scanning, 10, 0));
        throttled.Report(new ScanProgress(ScanProgressStage.Scanning, 20, 0));
        throttled.Report(new ScanProgress(ScanProgressStage.Completed, 100, 0));

        Assert.Equal(2, reports.Count); // First one passed (since last was MinValue), second was throttled, third passed because of predicate
        Assert.Equal(ScanProgressStage.Scanning, reports[0].Stage);
        Assert.Equal(ScanProgressStage.Completed, reports[1].Stage);
    }

    [Fact]
    public void ThrottledProgress_Flush_EmitsLastValue()
    {
        var reports = new List<int>();
        var throttled = new ThrottledProgress<int>(
            val => reports.Add(val),
            TimeSpan.FromSeconds(10));

        throttled.Report(1); // emitted
        throttled.Report(2); // throttled
        throttled.Report(3); // throttled

        Assert.Single(reports);
        Assert.Equal(1, reports[0]);

        throttled.Flush();
        Assert.Equal(2, reports.Count);
        Assert.Equal(3, reports[1]);
    }
}
