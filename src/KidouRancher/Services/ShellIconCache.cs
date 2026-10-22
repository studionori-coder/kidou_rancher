using System.Runtime.InteropServices;

namespace KidouRancher.Services;

/// <summary>
/// 一覧に出すアイコン（フォルダー・ファイルの種類・URL）を Windows から取り出して覚えておく。
/// 実際のファイルには触れず、種類（フォルダー／拡張子）だけでアイコンを決めるため、
/// 見つからない項目や応答の遅いファイルサーバーでも待たされない。
/// </summary>
public sealed class ShellIconCache : IDisposable
{
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiSmallIcon = 0x1;
    private const uint ShgfiUseFileAttributes = 0x10;

    private readonly Dictionary<string, Bitmap?> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// アイコンの種類を決める。URL は "url"、フォルダーは "folder"、ファイルは拡張子（".xlsx" など）。
    /// 種類が分からないとき（確認中・見つからない）は、パスに拡張子があればファイル、無ければフォルダーとみなす。
    /// </summary>
    public static string KeyFor(string item, LaunchTargetKind? kind)
    {
        if (kind == LaunchTargetKind.Url || (kind is null or LaunchTargetKind.Missing && LaunchTarget.IsUrl(item)))
        {
            return "url";
        }

        if (kind == LaunchTargetKind.Folder)
        {
            return "folder";
        }

        var extension = Path.GetExtension(item.TrimEnd('\\', '/'));
        if (kind == LaunchTargetKind.File)
        {
            return extension.Length > 0 ? extension : ".file";
        }

        // 拡張子らしいもの（1〜8文字）がある場合だけファイルとみなす（"v1.0" のようなフォルダー名を避けるため上限を設ける）
        return extension.Length is > 1 and <= 9 ? extension : "folder";
    }

    /// <summary>16×16 のアイコン。取り出せなければ null（UIスレッドから呼ぶ）。</summary>
    public Bitmap? Get(string item, LaunchTargetKind? kind)
    {
        var key = KeyFor(item, kind);
        if (!_icons.TryGetValue(key, out var icon))
        {
            icon = Load(key);
            _icons[key] = icon;
        }

        return icon;
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values)
        {
            icon?.Dispose();
        }

        _icons.Clear();
    }

    private static Bitmap? Load(string key)
    {
        // URL は既定のブラウザーのアイコン（.html に関連付けられたアプリ）を使う
        var (path, attributes) = key switch
        {
            "folder" => ("folder", FileAttributeDirectory),
            "url" => ("page.html", FileAttributeNormal),
            _ => ("file" + key, FileAttributeNormal)
        };

        try
        {
            var info = new ShFileInfo();
            SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);
            if (info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                using var icon = Icon.FromHandle(info.hIcon);
                return icon.ToBitmap();
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch (Exception exception) when (exception is ExternalException or ArgumentException or DllNotFoundException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
