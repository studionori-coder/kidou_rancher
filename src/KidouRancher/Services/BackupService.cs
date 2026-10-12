using System.Text.Json;
using KidouRancher.Models;

namespace KidouRancher.Services;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Export(LauncherSettings settings, string filePath)
    {
        ProjectStore.Validate(settings);
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public LauncherSettings Import(string filePath)
    {
        var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(filePath), JsonOptions)
            ?? throw new InvalidDataException("バックアップの内容が空です。");
        ProjectStore.Validate(settings);
        return settings;
    }
}