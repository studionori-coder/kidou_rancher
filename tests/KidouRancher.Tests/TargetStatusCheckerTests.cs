using KidouRancher.Services;

namespace KidouRancher.Tests;

[TestClass]
public sealed class TargetStatusCheckerTests
{
    [TestMethod]
    [DataRow(200)]
    [DataRow(301)]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(429)]
    public void DescribeHttpStatus_TreatsRespondingPagesAsValid(int statusCode) =>
        Assert.IsTrue(TargetStatusChecker.DescribeHttpStatus(statusCode).IsValid);

    [TestMethod]
    [DataRow(404)]
    [DataRow(410)]
    [DataRow(500)]
    [DataRow(503)]
    public void DescribeHttpStatus_TreatsMissingPagesAndServerErrorsAsInvalid(int statusCode) =>
        Assert.IsFalse(TargetStatusChecker.DescribeHttpStatus(statusCode).IsValid);

    [TestMethod]
    public void CheckPath_ReportsExistingAndMissingPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "memo.txt");
            File.WriteAllText(file, "memo");

            Assert.AreEqual(new TargetStatus(LaunchTargetKind.Folder, true, "有効"), TargetStatusChecker.CheckPath(directory));
            Assert.AreEqual(new TargetStatus(LaunchTargetKind.File, true, "有効"), TargetStatusChecker.CheckPath(file));
            Assert.IsFalse(TargetStatusChecker.CheckPath(Path.Combine(directory, "none.txt")).IsValid);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CheckUrlAsync_AcceptsNonHttpUrlsWithoutAccess()
    {
        var status = await TargetStatusChecker.CheckUrlAsync("mailto:someone@example.com", CancellationToken.None);
        Assert.IsTrue(status.IsValid);
    }
}
