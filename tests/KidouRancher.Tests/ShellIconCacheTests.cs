using KidouRancher.Services;

namespace KidouRancher.Tests;

[TestClass]
public sealed class ShellIconCacheTests
{
    [TestMethod]
    [DataRow(@"C:\work", LaunchTargetKind.Folder, "folder")]
    [DataRow(@"C:\work\見積.xlsx", LaunchTargetKind.File, ".xlsx")]
    [DataRow(@"C:\work\README", LaunchTargetKind.File, ".file")]
    [DataRow("https://example.com", LaunchTargetKind.Url, "url")]
    public void KeyFor_UsesTheCheckedKind(string item, LaunchTargetKind kind, string expected) =>
        Assert.AreEqual(expected, ShellIconCache.KeyFor(item, kind));

    [TestMethod]
    [DataRow(@"D:\共有\提案書.pptx", ".pptx")]
    [DataRow(@"\\fileserver\share\資料", "folder")]
    [DataRow(@"D:\releases\v1.0.2-final-version", "folder")]
    [DataRow("https://example.com/page", "url")]
    public void KeyFor_GuessesWhileUnknownOrMissing(string item, string expected)
    {
        Assert.AreEqual(expected, ShellIconCache.KeyFor(item, null));
        Assert.AreEqual(expected, ShellIconCache.KeyFor(item, LaunchTargetKind.Missing));
    }
}
