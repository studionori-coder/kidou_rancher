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

    /// <summary>前回終了時のウィンドウの位置と大きさ（最大化していたときは元の大きさ）。</summary>
    public int[]? WindowBounds { get; set; }

    public bool WindowMaximized { get; set; }

    /// <summary>左右の一覧の境目の位置（左の一覧の幅）。</summary>
    public int? SplitterDistance { get; set; }

    /// <summary>起動項目一覧の列の幅（表題・種類・状態。パスの列は残りの幅に合わせる）。</summary>
    public int[]? ColumnWidths { get; set; }
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
