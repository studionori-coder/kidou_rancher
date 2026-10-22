using KidouRancher.Services;

namespace KidouRancher.Tests;

[TestClass]
public sealed class UpdateCheckerTests
{
    [TestMethod]
    [DataRow("v1.0.3", "1.0.2.0", true)]
    [DataRow("v1.1.0", "1.0.9.0", true)]
    [DataRow("v2.0.0", "1.9.9.0", true)]
    [DataRow("v1.0.2", "1.0.2.0", false)]
    [DataRow("1.0.2", "1.0.2", false)]
    [DataRow("v1.0.1", "1.0.2.0", false)]
    [DataRow("v1.0.10", "1.0.9.0", true)]
    public void IsNewer_ComparesReleaseTagWithCurrentVersion(string tag, string current, bool expected) =>
        Assert.AreEqual(expected, UpdateChecker.IsNewer(tag, Version.Parse(current)));

    [TestMethod]
    public void IsNewer_IgnoresMissingTag() =>
        Assert.IsFalse(UpdateChecker.IsNewer(null, new Version(1, 0, 0)));

    [TestMethod]
    [DataRow("")]
    [DataRow("latest")]
    [DataRow("release-2026")]
    public void IsNewer_IgnoresTagsThatAreNotVersions(string tag) =>
        Assert.IsFalse(UpdateChecker.IsNewer(tag, new Version(1, 0, 0)));
}
