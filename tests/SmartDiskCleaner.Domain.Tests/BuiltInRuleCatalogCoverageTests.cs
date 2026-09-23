using SmartDiskCleaner.Domain;
using SmartDiskCleaner.Rules;
using Xunit;

namespace SmartDiskCleaner.Domain.Tests;

public sealed class BuiltInRuleCatalogCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly ClassificationService Service = new(BuiltInRuleSetFactory.Create(new ResolvedKnownPaths(
        @"C:\", @"C:\Windows", @"C:\Windows\System32", @"C:\Users\me",
        @"C:\Users\me\AppData\Local", @"C:\Users\me\AppData\Roaming",
        @"C:\Users\me\AppData\Local\Temp", @"C:\Users\me\AppData\Local\SmartDiskCleaner")));

    public static TheoryData<string, string, string, bool, long, bool> Cases => new()
    {
        { "PROTECT-WINDOWS-WINSXS-001", @"C:\Windows\WinSxS\x.dll", @"C:\Windows\WinSxS-backup\x.dll", false, 1, false },
        { "PROTECT-WINDOWS-INSTALLER-001", @"C:\Windows\Installer\x.msi", @"C:\Windows\Installer-old\x.msi", false, 1, false },
        { "PROTECT-WINDOWS-SYSTEM32-001", @"C:\Windows\System32\x.dll", @"C:\Windows\System32-old\x.dll", false, 1, false },
        { "PROTECT-SYSTEM-DIRECTORY-001", @"C:\Windows\System32\drivers\x.sys", @"C:\Windows\SystemFiles\x.sys", false, 1, false },
        { "PROTECT-SYSTEM-VOLUME-001", @"C:\System Volume Information\x", @"C:\System Volume Information-old\x", false, 1, false },
        { "PROTECT-BOOT-001", @"C:\Boot\BCD", @"C:\Boot-old\BCD", false, 1, false },
        { "PROTECT-EFI-001", @"C:\EFI\boot.bin", @"C:\EFI-old\boot.bin", false, 1, false },
        { "PROTECT-RECOVERY-001", @"C:\Recovery\image.wim", @"C:\Recovery-old\image.wim", false, 1, false },
        { "PROTECT-ROOT-PAGEFILE-SYS-001", @"C:\pagefile.sys", @"C:\pagefile.sys.bak", false, 1, false },
        { "PROTECT-ROOT-SWAPFILE-SYS-001", @"C:\swapfile.sys", @"C:\swapfile.sys.bak", false, 1, false },
        { "PROTECT-ROOT-HIBERFIL-SYS-001", @"C:\hiberfil.sys", @"C:\hiberfil.sys.bak", false, 1, false },
        { "EXCLUDE-APPDATA-OWNED-001", @"C:\Users\me\AppData\Local\SmartDiskCleaner\scan.db", @"C:\Users\me\AppData\Local\Other\scan.db", false, 1, false },
        { "TEMP-USER-001", @"C:\Users\me\AppData\Local\Temp\x.dat", @"C:\Project\Temp\x.dat", false, 1, false },
        { "TEMP-WINDOWS-001", @"C:\Windows\Temp\x.dat", @"C:\Windows\Temp-old\x.dat", false, 1, false },
        { "CACHE-CHROMIUM-001", @"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache\data", @"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Login Data", false, 1, false },
        { "CACHE-CHROMIUM-EDGE-001", @"C:\Users\me\AppData\Local\Microsoft\Edge\User Data\Default\GPUCache\data", @"C:\Users\me\AppData\Local\Microsoft\Edge\User Data\Default\History", false, 1, false },
        { "CACHE-THUMBNAIL-001", @"C:\Users\me\AppData\Local\Microsoft\Windows\Explorer\thumbcache_32.db", @"C:\Project\thumbcache_32.db", false, 1, false },
        { "CRASH-LOCALDUMP-001", @"C:\Users\me\AppData\Local\CrashDumps\app.dmp", @"C:\Project\CrashDumps\app.dmp", false, 1, false },
        { "RECYCLE-BIN-001", @"C:\$Recycle.Bin\S-1\item", @"C:\Recycle.Bin\item", false, 1, false },
        { "DEV-GRADLE-CACHE-001", @"C:\Users\me\.gradle\caches\data", @"C:\Project\.gradle\data", false, 1, false },
        { "DEV-NUGET-CACHE-001", @"C:\Users\me\.nuget\packages\x\1\x.dll", @"C:\Project\.nuget\x.dll", false, 1, false },
        { "DEV-NODE-MODULES-001", @"C:\Project\node_modules", @"C:\Project\node_modules_backup", true, 1, false },
        { "GENERIC-TMP-REVIEW-001", @"C:\Project\scratch.tmp", @"C:\Project\scratch.tmpx", false, 1, false },
        { "GENERIC-OLD-LOG-REVIEW-001", @"C:\Project\old.log", @"C:\Project\fresh.log", false, 1, true },
        { "LARGE-FILE-INFO-001", @"C:\Project\large.iso", @"C:\Project\small.iso", false, 2L * 1024 * 1024 * 1024, false }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryBuiltInRule_HasPositiveAndNearMissCoverage(
        string ruleId,
        string positivePath,
        string nearMissPath,
        bool directory,
        long logicalBytes,
        bool ageSensitive)
    {
        var positive = CreateNode(positivePath, directory, logicalBytes, ageSensitive ? Now.AddDays(-60) : Now);
        var nearMissLogical = ruleId == "LARGE-FILE-INFO-001" ? 100 : logicalBytes;
        var nearMissModified = ruleId == "GENERIC-OLD-LOG-REVIEW-001" ? Now.AddDays(-1) : Now;
        var nearMiss = CreateNode(nearMissPath, directory, nearMissLogical, nearMissModified);

        Assert.Contains(ruleId, Service.Classify(positive, Now).MatchedRuleIds);
        Assert.DoesNotContain(ruleId, Service.Classify(nearMiss, Now).MatchedRuleIds);
    }

    [Fact]
    public void EveryEnabledBuiltInRule_IsCoveredByTheCatalogTable()
    {
        var rules = BuiltInRuleSetFactory.Create(new ResolvedKnownPaths(
            @"C:\", @"C:\Windows", @"C:\Windows\System32", @"C:\Users\me",
            @"C:\Users\me\AppData\Local", @"C:\Users\me\AppData\Roaming",
            @"C:\Users\me\AppData\Local\Temp", @"C:\Users\me\AppData\Local\SmartDiskCleaner"));
        var covered = Cases.Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(rules.Rules.Select(rule => rule.RuleId).Order(StringComparer.Ordinal), covered.Order(StringComparer.Ordinal));
    }

    private static ScanNode CreateNode(string path, bool directory, long logicalBytes, DateTimeOffset modified)
    {
        if (!directory) return TestNodeFactory.File(2, path, logicalBytes, logicalBytes, modified: modified);
        var node = TestNodeFactory.Directory(2, path, logicalBytes, logicalBytes, logicalBytes);
        return node with { ModifiedUtc = modified };
    }
}

