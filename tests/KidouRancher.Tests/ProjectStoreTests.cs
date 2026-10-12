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