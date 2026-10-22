using System.Text.Json;

namespace KidouRancher.Services;

/// <summary>
/// 画面操作の記憶（前回の書き出し先、選択していたプロジェクトなど）を保存する。
/// バックアップ対象のプロジェクト設定とは混ぜないよう、別ファイルで管理する。
/// </summary>
public sealed class AppPreferences
{
    public string? LastExportPath { get; set; }

    /// <summary>前回終了時に選択していたプロジェクト。次回起動時に選択状態で表示する。</summary>
    public Guid? LastSelectedProjectId { get; set; }
}

public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; }

    public PreferencesStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KidouRancher",
            "preferences.json");
    }

    /// <summary>読み込みに失敗しても動作に支障はないため、既定値を返す。</summary>
    public AppPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath), JsonOptions) ?? new AppPreferences();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new AppPreferences();
    }

    /// <summary>保存に失敗しても本来の操作は成功しているため、例外は無視する。</summary>
    public void Save(AppPreferences preferences)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(preferences, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
