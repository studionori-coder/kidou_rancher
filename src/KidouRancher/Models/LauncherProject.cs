namespace KidouRancher.Models;

public sealed class LauncherProject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 起動する項目（フォルダー・ファイルのパス、またはURL）。
    /// 以前のバージョンで書き出した設定を読み込めるよう、JSON上の名前は Folders のままにしている。
    /// </summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>
    /// 起動項目ごとの表題（一覧の一番左に表示する名前）。キーは Folders の値で、表題の無い項目は含めない。
    /// 以前のバージョンの設定には無い項目のため、無ければ空として扱う。
    /// </summary>
    public Dictionary<string, string> Titles
    {
        get => _titles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        set => _titles = value;
    }

    private Dictionary<string, string>? _titles;

    /// <summary>項目の表題を返す（無ければ空文字）。パスの大文字・小文字は区別しない。</summary>
    public string GetTitle(string item) =>
        Titles.FirstOrDefault(pair => string.Equals(pair.Key, item, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    /// <summary>項目の表題を設定する。空欄なら表題を消す。</summary>
    public void SetTitle(string item, string? title)
    {
        foreach (var key in Titles.Keys.Where(key => string.Equals(key, item, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            Titles.Remove(key);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            Titles[item] = title.Trim();
        }
    }

    /// <summary>
    /// 一括起動の対象から外している項目（一覧のチェックを外した項目）。値は Folders の値。
    /// 以前のバージョンの設定には無い項目のため、無ければすべて起動の対象として扱う。
    /// </summary>
    public List<string> DisabledItems
    {
        get => _disabledItems ??= [];
        set => _disabledItems = value;
    }

    private List<string>? _disabledItems;

    /// <summary>一括起動の対象かどうか（チェックが付いているか）。</summary>
    public bool IsEnabled(string item) => !DisabledItems.Contains(item, StringComparer.OrdinalIgnoreCase);

    public void SetEnabled(string item, bool enabled)
    {
        DisabledItems.RemoveAll(disabled => string.Equals(disabled, item, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
        {
            DisabledItems.Add(item);
        }
    }

    /// <summary>一覧から外した項目の表題と、起動しない設定を取り除く。</summary>
    public void RemoveUnusedItemSettings()
    {
        foreach (var key in Titles.Keys.Where(key => !Folders.Contains(key, StringComparer.OrdinalIgnoreCase)).ToArray())
        {
            Titles.Remove(key);
        }

        DisabledItems.RemoveAll(item => !Folders.Contains(item, StringComparer.OrdinalIgnoreCase));
    }

    public override string ToString() => Name;
}

public sealed class LauncherSettings
{
    public List<LauncherProject> Projects { get; set; } = [];
}