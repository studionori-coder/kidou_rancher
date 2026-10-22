using KidouRancher.Models;
using KidouRancher.Services;

namespace KidouRancher.Tests;

[TestClass]
public sealed class ProjectStoreTests
{
    [TestMethod]
    public void SaveAndLoad_PreservesProjectsAndFolders()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new ProjectStore(Path.Combine(temporaryDirectory.Path, "projects.json"));
        var settings = new LauncherSettings
        {
            Projects =
            [
                new LauncherProject
                {
                    Name = "Web",
                    Folders = [@"C:\work\web", @"D:\shared files"]
                }
            ]
        };

        store.Save(settings);

        var loaded = store.Load();
        Assert.AreEqual("Web", loaded.Projects.Single().Name);
        CollectionAssert.AreEqual(settings.Projects[0].Folders, loaded.Projects[0].Folders);
    }

    [TestMethod]
    public void SaveAndLoad_PreservesItemTitles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new ProjectStore(Path.Combine(temporaryDirectory.Path, "projects.json"));
        var project = new LauncherProject { Name = "Web", Folders = [@"C:\work\web", "https://example.com"] };
        project.SetTitle(@"C:\work\web", "制作データ");
        project.SetTitle("https://example.com", "  お客様サイト  ");

        store.Save(new LauncherSettings { Projects = [project] });

        var loaded = store.Load().Projects.Single();
        Assert.AreEqual("制作データ", loaded.GetTitle(@"c:\WORK\web"));
        Assert.AreEqual("お客様サイト", loaded.GetTitle("https://example.com"));
    }

    [TestMethod]
    public void Load_AcceptsSettingsFromOlderVersionsWithoutTitles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = Path.Combine(temporaryDirectory.Path, "projects.json");
        File.WriteAllText(filePath, """
            { "Projects": [ { "Id": "6d1f0a52-3c1e-4f7a-9b55-2f0d6c1a7e01", "Name": "Web", "Folders": [ "C:\\work" ] } ] }
            """);

        var project = new ProjectStore(filePath).Load().Projects.Single();
        Assert.AreEqual(string.Empty, project.GetTitle(@"C:\work"));
    }

    [TestMethod]
    public void Titles_CanBeClearedAndAreRemovedWithTheirItems()
    {
        var project = new LauncherProject { Folders = [@"C:\a", @"C:\b"] };
        project.SetTitle(@"C:\a", "A");
        project.SetTitle(@"C:\b", "B");

        project.SetTitle(@"C:\A", "  ");
        project.Folders.Remove(@"C:\b");
        project.RemoveUnusedItemSettings();

        Assert.AreEqual(0, project.Titles.Count);
    }

    [TestMethod]
    public void DisabledItems_RoundTripAndIgnoreCase()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new ProjectStore(Path.Combine(temporaryDirectory.Path, "projects.json"));
        var project = new LauncherProject { Name = "Web", Folders = [@"C:\a", @"C:\b"] };
        project.SetEnabled(@"C:\b", false);
        project.SetEnabled(@"C:\B", false);

        store.Save(new LauncherSettings { Projects = [project] });

        var loaded = store.Load().Projects.Single();
        Assert.IsTrue(loaded.IsEnabled(@"C:\a"));
        Assert.IsFalse(loaded.IsEnabled(@"c:\b"));
        Assert.AreEqual(1, loaded.DisabledItems.Count);

        loaded.SetEnabled(@"C:\b", true);
        Assert.IsTrue(loaded.IsEnabled(@"C:\b"));
    }

    [TestMethod]
    public void ReplaceInItems_MovesPathsAndKeepsTitlesAndSettings()
    {
        var project = new LauncherProject { Folders = [@"\\old-server\share\資料", @"\\OLD-SERVER\share\見積.xlsx", "https://example.com"] };
        project.SetTitle(@"\\old-server\share\資料", "資料");
        project.SetEnabled(@"\\OLD-SERVER\share\見積.xlsx", false);

        var count = project.ReplaceInItems(@"\\old-server\", @"\\192.168.1.20\");

        Assert.AreEqual(2, count);
        CollectionAssert.AreEqual(new[] { @"\\192.168.1.20\share\資料", @"\\192.168.1.20\share\見積.xlsx", "https://example.com" }, project.Folders);
        Assert.AreEqual("資料", project.GetTitle(@"\\192.168.1.20\share\資料"));
        Assert.IsFalse(project.IsEnabled(@"\\192.168.1.20\share\見積.xlsx"));
        Assert.AreEqual(1, project.Titles.Count);
    }

    [TestMethod]
    public void ReplaceInItems_MergesItemsThatBecomeTheSame()
    {
        var project = new LauncherProject { Folders = [@"\\old\share", @"\\new\share", @"C:\work"] };

        var count = project.ReplaceInItems(@"\\old\", @"\\new\");

        Assert.AreEqual(1, count);
        CollectionAssert.AreEqual(new[] { @"\\new\share", @"C:\work" }, project.Folders);
    }

    [TestMethod]
    public void ReplaceInItems_DoesNothingForEmptySearchText()
    {
        var project = new LauncherProject { Folders = [@"C:\work"] };

        Assert.AreEqual(0, project.ReplaceInItems("", "x"));
        CollectionAssert.AreEqual(new[] { @"C:\work" }, project.Folders);
    }

    [TestMethod]
    public void DisabledItems_AreRemovedWithTheirItems()
    {
        var project = new LauncherProject { Folders = [@"C:\a", @"C:\b"] };
        project.SetEnabled(@"C:\b", false);

        project.Folders.Remove(@"C:\b");
        project.RemoveUnusedItemSettings();

        Assert.AreEqual(0, project.DisabledItems.Count);
    }

    [TestMethod]
    public void Save_RejectsDuplicateProjectNames()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new ProjectStore(Path.Combine(temporaryDirectory.Path, "projects.json"));
        var settings = new LauncherSettings
        {
            Projects =
            [
                new LauncherProject { Name = "Work" },
                new LauncherProject { Name = "work" }
            ]
        };

        Assert.Throws<InvalidDataException>(() => store.Save(settings));
        Assert.IsFalse(File.Exists(store.FilePath));
    }

    [TestMethod]
    public void Backup_RoundTripsSettings()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = Path.Combine(temporaryDirectory.Path, "backup.json");
        var service = new BackupService();
        var settings = new LauncherSettings
        {
            Projects = [new LauncherProject { Name = "Tools", Folders = [@"C:\tools"] }]
        };

        service.Export(settings, filePath);

        var imported = service.Import(filePath);
        Assert.AreEqual("Tools", imported.Projects.Single().Name);
        CollectionAssert.AreEqual(settings.Projects[0].Folders, imported.Projects[0].Folders);
    }

    [TestMethod]
    public void Preferences_RoundTripsLastExportPath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new PreferencesStore(Path.Combine(temporaryDirectory.Path, "sub", "preferences.json"));

        store.Save(new AppPreferences { LastExportPath = @"D:\backup\my-backup.json" });

        Assert.AreEqual(@"D:\backup\my-backup.json", store.Load().LastExportPath);
    }

    [TestMethod]
    public void Preferences_RoundTripsLastSelectedProject()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new PreferencesStore(Path.Combine(temporaryDirectory.Path, "preferences.json"));
        var projectId = Guid.NewGuid();

        store.Save(new AppPreferences { LastExportPath = @"D:\backup\my-backup.json", LastSelectedProjectId = projectId });

        var loaded = store.Load();
        Assert.AreEqual(projectId, loaded.LastSelectedProjectId);
        Assert.AreEqual(@"D:\backup\my-backup.json", loaded.LastExportPath);
    }

    [TestMethod]
    public void Preferences_LoadsFilesFromOlderVersionsWithoutSelectedProject()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = Path.Combine(temporaryDirectory.Path, "preferences.json");
        File.WriteAllText(filePath, """{ "LastExportPath": "D:\\backup.json" }""");

        var loaded = new PreferencesStore(filePath).Load();
        Assert.IsNull(loaded.LastSelectedProjectId);
        Assert.AreEqual(@"D:\backup.json", loaded.LastExportPath);
    }

    [TestMethod]
    public void Preferences_ReturnsDefaultsWhenFileIsBroken()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = Path.Combine(temporaryDirectory.Path, "preferences.json");
        File.WriteAllText(filePath, "{ broken");

        Assert.IsNull(new PreferencesStore(filePath).Load().LastExportPath);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}