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

    public override string ToString() => Name;
}

public sealed class LauncherSettings
{
    public List<LauncherProject> Projects { get; set; } = [];
}