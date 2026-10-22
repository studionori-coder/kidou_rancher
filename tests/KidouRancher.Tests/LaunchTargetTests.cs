using KidouRancher.Services;

namespace KidouRancher.Tests;

[TestClass]
public sealed class LaunchTargetTests
{
    [TestMethod]
    [DataRow("https://example.com")]
    [DataRow("http://localhost:3000/path?q=1")]
    [DataRow("mailto:someone@example.com")]
    public void IsUrl_AcceptsUrls(string value) => Assert.IsTrue(LaunchTarget.IsUrl(value));

    [TestMethod]
    [DataRow(@"C:\work\web")]
    [DataRow(@"D:\shared files\report.xlsx")]
    [DataRow(@"\\server\share\folder")]
    [DataRow(@"relative\path")]
    [DataRow("")]
    public void IsUrl_RejectsPaths(string value) => Assert.IsFalse(LaunchTarget.IsUrl(value));

    [TestMethod]
    public void NormalizeUrl_AddsHttpsWhenSchemeIsMissing() =>
        Assert.AreEqual("https://example.com/page", LaunchTarget.NormalizeUrl("  example.com/page "));

    [TestMethod]
    public void NormalizeUrl_KeepsExistingScheme() =>
        Assert.AreEqual("http://localhost:8080", LaunchTarget.NormalizeUrl("http://localhost:8080"));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not a url")]
    public void NormalizeUrl_ReturnsNullForInvalidInput(string value) => Assert.IsNull(LaunchTarget.NormalizeUrl(value));

    [TestMethod]
    public void Classify_DetectsFoldersFilesUrlsAndMissingPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "memo.txt");
            File.WriteAllText(file, "memo");

            Assert.AreEqual(LaunchTargetKind.Folder, LaunchTarget.Classify(directory));
            Assert.AreEqual(LaunchTargetKind.File, LaunchTarget.Classify(file));
            Assert.AreEqual(LaunchTargetKind.Url, LaunchTarget.Classify("https://example.com"));
            Assert.AreEqual(LaunchTargetKind.Missing, LaunchTarget.Classify(Path.Combine(directory, "none.txt")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(@"\\fileserver\share\資料", @"\\fileserver\share\資料")]
    [DataRow("//fileserver/share/資料", @"\\fileserver\share\資料")]
    [DataRow(@"""\\fileserver\share\見積.xlsx""", @"\\fileserver\share\見積.xlsx")]
    [DataRow(@"  ""C:\work\memo.txt""  ", @"C:\work\memo.txt")]
    [DataRow("C:/work/web", @"C:\work\web")]
    [DataRow("file://fileserver/share/a.txt", @"\\fileserver\share\a.txt")]
    [DataRow("file:///C:/work/a%20b.txt", @"C:\work\a b.txt")]
    [DataRow("https://example.com", "https://example.com")]
    [DataRow("example.com", "https://example.com")]
    public void NormalizeInput_AcceptsPathsInVariousNotations(string input, string expected) =>
        Assert.AreEqual(expected, LaunchTarget.NormalizeInput(input));

    [TestMethod]
    public void NormalizeInput_DoesNotGuessUrlsWhenSchemeCompletionIsOff()
    {
        Assert.IsNull(LaunchTarget.NormalizeInput("hello", completeUrlScheme: false));
        Assert.AreEqual("https://example.com", LaunchTarget.NormalizeInput("https://example.com", completeUrlScheme: false));
        Assert.AreEqual(@"\\server\share", LaunchTarget.NormalizeInput(@"\\server\share", completeUrlScheme: false));
    }

    [TestMethod]
    [DataRow(@"\\fileserver\share\資料")]
    [DataRow(@"\\fileserver\share\見積.xlsx")]
    public void IsUrl_RejectsFileServerPaths(string value) => Assert.IsFalse(LaunchTarget.IsUrl(value));
}
