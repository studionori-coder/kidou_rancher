using KidouRancher.Models;
using KidouRancher.Services;

namespace KidouRancher.Tests;

[TestClass]
public sealed class SearchFilterTests
{
    private static LauncherProject CreateProject()
    {
        var project = new LauncherProject { Name = "ケイリ", Folders = [@"\\fileserver\share\請求書", @"C:\work\エクセル集計.xlsx", "https://example.com/report"] };
        project.SetTitle("https://example.com/report", "月次レポート");
        return project;
    }

    [TestMethod]
    [DataRow("けいり")]
    [DataRow("ｹｲﾘ")]
    [DataRow("ケイリ")]
    public void ProjectMatches_IgnoresKanaTypeAndWidthInTheName(string query)
    {
        var project = CreateProject();

        Assert.IsTrue(SearchFilter.ProjectMatches(project, query));
        Assert.AreEqual(3, SearchFilter.VisibleItems(project, query).Count);
    }

    [TestMethod]
    public void VisibleItems_ShowsOnlyMatchingItemsWhenTheNameDoesNotMatch()
    {
        var project = CreateProject();

        CollectionAssert.AreEqual(new[] { @"C:\work\エクセル集計.xlsx" }, SearchFilter.VisibleItems(project, "ｴｸｾﾙ").ToArray());
        CollectionAssert.AreEqual(new[] { @"\\fileserver\share\請求書" }, SearchFilter.VisibleItems(project, "FILESERVER").ToArray());
        CollectionAssert.AreEqual(new[] { "https://example.com/report" }, SearchFilter.VisibleItems(project, "月次").ToArray());
    }

    [TestMethod]
    public void ProjectMatches_RejectsProjectsWithoutTheText()
    {
        Assert.IsFalse(SearchFilter.ProjectMatches(CreateProject(), "見積"));
        Assert.AreEqual(0, SearchFilter.VisibleItems(CreateProject(), "見積").Count);
    }

    [TestMethod]
    public void EmptyQuery_KeepsEverything()
    {
        var project = CreateProject();

        Assert.IsTrue(SearchFilter.ProjectMatches(project, ""));
        Assert.AreEqual(3, SearchFilter.VisibleItems(project, "").Count);
    }
}
