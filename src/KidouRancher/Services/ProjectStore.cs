using System.Text.Json;
using KidouRancher.Models;

namespace KidouRancher.Services;

public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; }

    public ProjectStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KidouRancher",
            "projects.json");
    }

    public LauncherSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new LauncherSettings();
        }

        var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath), JsonOptions)
            ?? throw new InvalidDataException("設定ファイルの内容が空です。");
        Validate(settings);
        return settings;
    }

    public void Save(LauncherSettings settings)
    {
        Validate(settings);
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("設定ファイルの保存先が不正です。");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static void Validate(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Projects is null)
        {
            throw new InvalidDataException("プロジェクト一覧がありません。");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>();
        foreach (var project in settings.Projects)
        {
            if (project is null || project.Id == Guid.Empty || string.IsNullOrWhiteSpace(project.Name))
            {
                throw new InvalidDataException("プロジェクトのIDと名前は必須です。");
            }

            if (!names.Add(project.Name.Trim()) || !ids.Add(project.Id))
            {
                throw new InvalidDataException("プロジェクト名またはIDが重複しています。");
            }

            if (project.Folders is null || project.Folders.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException($"「{project.Name}」のフォルダー一覧に空のパスがあります。");
            }

            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (project.Folders.Any(folder => !folders.Add(folder.Trim())))
            {
                throw new InvalidDataException($"「{project.Name}」に重複したフォルダーがあります。");
            }
        }
    }
}