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
