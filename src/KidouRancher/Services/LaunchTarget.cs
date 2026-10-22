using System.Diagnostics;

namespace KidouRancher.Services;

public enum LaunchTargetKind
{
    Folder,
    File,
    Url,
    Missing
}

/// <summary>
/// 登録項目（フォルダー・ファイルのパス、またはURL）の種類を判定し、既定のアプリで開く。
/// </summary>
public sealed record LaunchPlan(
    IReadOnlyList<string> Folders,
    IReadOnlyList<(string Path, LaunchTargetKind Kind)> Others,
    IReadOnlyList<string> Missing)
{
    public bool IsEmpty => Folders.Count == 0 && Others.Count == 0;
}

public static class LaunchTarget
{
    public static bool IsUrl(string value)
    {
        // C:\... や \\server\share もURIとして解釈できるため、ファイル系のURIは除外する
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && !uri.IsFile
            && !uri.IsUnc
            && uri.Scheme.Length > 1;
    }

    /// <summary>ディスクへのアクセスを伴うため、多数の項目はUIスレッド外で判定する。</summary>
    public static LaunchTargetKind Classify(string value)
    {
        if (IsUrl(value))
        {
            return LaunchTargetKind.Url;
        }

        if (Directory.Exists(value))
        {
            return LaunchTargetKind.Folder;
        }

        return File.Exists(value) ? LaunchTargetKind.File : LaunchTargetKind.Missing;
    }

    /// <summary>入力されたURLを整える。スキームが無ければ https:// を補い、URLとして不正なら null を返す。</summary>
    public static string? NormalizeUrl(string input)
    {
        var value = input.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (!value.Contains("://", StringComparison.Ordinal) && !value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        return IsUrl(value) ? value : null;
    }

    /// <summary>ドライブ（C:\...）またはファイルサーバー（\\server\share\...）の表記かどうか。</summary>
    public static bool IsLocalOrNetworkPath(string value) =>
        value.StartsWith(@"\\", StringComparison.Ordinal)
        || value.StartsWith("//", StringComparison.Ordinal)
        || (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/');

    /// <summary>
    /// 入力・貼り付けされたパスまたはURLを登録用に整える。
    /// 前後の引用符（エクスプローラーの「パスのコピー」）を外し、file:// や smb://、/ 区切りの表記は Windows のパスに直す。
    /// completeUrlScheme が true のときは、スキームの無い入力を https:// のURLとして扱う。
    /// </summary>
    public static string? NormalizeInput(string input, bool completeUrlScheme = true)
    {
        var value = input.Trim().Trim('"').Trim();
        if (value.Length == 0)
        {
            return null;
        }

        // smb://server/share（Mac などの表記）はファイルサーバーのパス \\server\share に直す。IPアドレスでも同じ
        if (value.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
        {
            var rest = Uri.UnescapeDataString(value["smb://".Length..]).Replace('/', '\\').TrimEnd('\\');
            return rest.Length == 0 ? null : @"\\" + rest;
        }

        if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var fileUri) && fileUri.IsFile ? fileUri.LocalPath : null;
        }

        if (IsLocalOrNetworkPath(value))
        {
            return value.Replace('/', '\\');
        }

        if (IsUrl(value))
        {
            return value;
        }

        return completeUrlScheme ? NormalizeUrl(value) : null;
    }

    /// <summary>
    /// 判定済みの項目を起動用に振り分ける。kind を指定するとその種類だけを対象にする（null ならすべて）。
    /// フォルダーはタブでまとめて開くため別にし、見つからない項目は Missing に分ける。並び順は保つ。
    /// </summary>
    public static LaunchPlan Plan(IEnumerable<(string Path, LaunchTargetKind Kind)> targets, LaunchTargetKind? kind = null)
    {
        var folders = new List<string>();
        var others = new List<(string Path, LaunchTargetKind Kind)>();
        var missing = new List<string>();
        foreach (var (path, targetKind) in targets)
        {
            if (targetKind == LaunchTargetKind.Missing)
            {
                missing.Add(path);
            }
            else if (kind is not null && targetKind != kind)
            {
                continue;
            }
            else if (targetKind == LaunchTargetKind.Folder)
            {
                folders.Add(path);
            }
            else
            {
                others.Add((path, targetKind));
            }
        }

        return new LaunchPlan(folders, others, missing);
    }

    public static string DisplayName(LaunchTargetKind kind) => kind switch
    {
        LaunchTargetKind.Folder => "フォルダー",
        LaunchTargetKind.File => "ファイル",
        LaunchTargetKind.Url => "URL",
        _ => "不明"
    };

    /// <summary>フォルダーはエクスプローラー、ファイルは関連付けられたアプリ、URLは既定のブラウザーで開く。</summary>
    public static void Open(string value, LaunchTargetKind kind)
    {
        switch (kind)
        {
            case LaunchTargetKind.Folder:
                ExplorerTabLauncher.OpenIndividually([value]);
                break;
            case LaunchTargetKind.File:
            case LaunchTargetKind.Url:
                Process.Start(new ProcessStartInfo(value.Trim()) { UseShellExecute = true });
                break;
        }
    }
}
