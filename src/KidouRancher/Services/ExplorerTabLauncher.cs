using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KidouRancher.Services;

/// <summary>
/// 複数フォルダーを1つのエクスプローラーウィンドウのタブとして開く。
/// キーボード入力の送信は行わず、ウィンドウハンドルを直接指定して操作するため、
/// エクスプローラーが前面にあるか・最大化されているかに依存しない。
/// </summary>
public sealed class ExplorerTabLauncher
{
    private const uint WindowMessageCommand = 0x0111;
    // エクスプローラーの「新しいタブ」コマンドID（非公開だがWindows 11で広く使われている値）
    private static readonly IntPtr NewTabCommandId = new(0xA21B);
    private const uint GetAncestorRoot = 2;
    private const string ExplorerWindowClass = "CabinetWClass";
    private const string ExplorerTabClass = "ShellTabWindowClass";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TabTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NavigationVerifyTimeout = TimeSpan.FromSeconds(3);
    private static readonly Guid ShellBrowserId = new("000214E2-0000-0000-C000-000000000046");

    public async Task<LaunchResult> OpenFoldersAsync(IReadOnlyList<string> folders, CancellationToken cancellationToken)
    {
        if (folders.Count == 0)
        {
            return new LaunchResult(false, "起動できるフォルダーがありません。", []);
        }

        // 存在確認はドライブのスピンアップ等で時間がかかることがあるため、UIスレッド外で行う
        var checks = await Task.Run(() => folders.Select(folder => (Folder: folder, Exists: Directory.Exists(folder))).ToArray(), cancellationToken);
        var missing = checks.Where(check => !check.Exists).Select(check => check.Folder).ToArray();
        var existing = checks.Where(check => check.Exists).Select(check => check.Folder).ToArray();
        if (existing.Length == 0)
        {
            return new LaunchResult(false, "登録フォルダーが見つかりません。パスを確認してください。", missing);
        }

        var nextIndex = 0;
        try
        {
            var shell = CreateShell();
            var knownTabs = GetAllExplorerTabs();

            Process.Start(new ProcessStartInfo("explorer.exe", QuoteArgument(existing[0])) { UseShellExecute = true });
            nextIndex = 1;

            // 起動前に存在しなかったタブを探す（新規ウィンドウでも既存ウィンドウへのタブ追加でも検出できる）
            var firstTab = await WaitForAsync(
                () => GetAllExplorerTabs().FirstOrDefault(tab => !knownTabs.Contains(tab)),
                tab => tab != IntPtr.Zero,
                WindowTimeout,
                cancellationToken);
            if (firstTab == IntPtr.Zero)
            {
                return Failure("起動したエクスプローラーのウィンドウを検出できませんでした。", missing, existing, nextIndex);
            }

            var window = GetAncestor(firstTab, GetAncestorRoot);
            var openedTabs = new List<(object Browser, string Folder)>();
            var firstBrowser = await WaitForAsync(() => FindBrowser(shell, window, firstTab), browser => browser is not null, TabTimeout, cancellationToken);
            if (firstBrowser is not null)
            {
                openedTabs.Add((firstBrowser, existing[0]));
            }

            for (; nextIndex < existing.Length; nextIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tabsBefore = GetTabs(window);
                if (tabsBefore.Count == 0 || !IsWindow(window))
                {
                    return Failure("エクスプローラーのウィンドウが閉じられたため、タブ操作を中断しました。", missing, existing, nextIndex);
                }

                // 対象ウィンドウへ直接「新しいタブ」コマンドを送る（前面である必要はない）
                PostMessage(tabsBefore[0], WindowMessageCommand, NewTabCommandId, IntPtr.Zero);

                var newTab = await WaitForAsync(
                    () => GetTabs(window).FirstOrDefault(tab => !tabsBefore.Contains(tab)),
                    tab => tab != IntPtr.Zero,
                    TabTimeout,
                    cancellationToken);
                var browser = newTab == IntPtr.Zero
                    ? null
                    : await WaitForAsync(() => FindBrowser(shell, window, newTab), found => found is not null, TabTimeout, cancellationToken);
                if (browser is null)
                {
                    return Failure("新しいタブを準備できなかったため、タブ操作を中断しました。", missing, existing, nextIndex);
                }

                // アドレスバーへの文字入力ではなく、タブ自体に直接遷移を指示する
                Navigate(browser, existing[nextIndex]);
                openedTabs.Add((browser, existing[nextIndex]));
            }

            // ホームのまま残ったタブがあれば再度遷移させる
            await EnsureNavigatedAsync(openedTabs, cancellationToken);
            SetForegroundWindow(window);

            var message = existing.Length == 1
                ? "フォルダーをエクスプローラーで開きました。"
                : $"{existing.Length} 個のフォルダーをエクスプローラーのタブで開きました。";
            if (missing.Length > 0)
            {
                message += $" 見つからないフォルダー {missing.Length} 件はスキップしました。";
            }

            return new LaunchResult(true, message, missing);
        }
        catch (OperationCanceledException)
        {
            return Failure("フォルダーの起動を中断しました。", missing, existing, nextIndex);
        }
        catch (Exception exception)
        {
            return Failure($"エクスプローラーを操作できませんでした: {exception.Message}", missing, existing, nextIndex);
        }
    }

    public static void OpenIndividually(IEnumerable<string> folders)
    {
        foreach (var folder in folders.Where(Directory.Exists))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", QuoteArgument(folder)) { UseShellExecute = true });
        }
    }

    private static LaunchResult Failure(string message, string[] missing, string[] existing, int nextIndex) =>
        new(false, message, missing, existing.Skip(nextIndex).ToArray());

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static object CreateShell()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("Shell.Application を利用できません。");
        return Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Shell.Application を作成できません。");
    }

    private static async Task<T> WaitForAsync<T>(Func<T> probe, Func<T, bool> isReady, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var value = probe();
            if (isReady(value) || stopwatch.Elapsed >= timeout)
            {
                return value;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static async Task EnsureNavigatedAsync(List<(object Browser, string Folder)> tabs, CancellationToken cancellationToken)
    {
        var pending = tabs.ToList();
        var stopwatch = Stopwatch.StartNew();
        while (pending.Count > 0 && stopwatch.Elapsed < NavigationVerifyTimeout)
        {
            pending.RemoveAll(tab => HasLocation(tab.Browser));
            if (pending.Count == 0)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        foreach (var (browser, folder) in pending)
        {
            Navigate(browser, folder);
        }
    }

    private static bool HasLocation(object browser)
    {
        try
        {
            string? location = ((dynamic)browser).LocationURL;
            return !string.IsNullOrEmpty(location);
        }
        catch
        {
            // タブが閉じられた等で取得できない場合は確認対象から外す
            return true;
        }
    }

    private static void Navigate(object browser, string folder)
    {
        try
        {
            ((dynamic)browser).Navigate2(folder);
        }
        catch (COMException)
        {
            // タブが閉じられた場合などは無視する
        }
    }

    /// <summary>Shell.Application.Windows() の中から、指定タブに対応するブラウザーオブジェクトを探す。</summary>
    private static object? FindBrowser(object shell, IntPtr window, IntPtr tab)
    {
        dynamic windows = ((dynamic)shell).Windows();
        int count = windows.Count;
        for (var index = 0; index < count; index++)
        {
            try
            {
                object? item = windows.Item(index);
                if (item is null)
                {
                    continue;
                }

                long handle = Convert.ToInt64(((dynamic)item).HWND);
                if (new IntPtr(handle) == window && GetTabWindow(item) == tab)
                {
                    return item;
                }
            }
            catch (Exception exception) when (exception is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
            {
                // 列挙中に閉じられたウィンドウなどは読み飛ばす
            }
        }

        return null;
    }

    private static IntPtr GetTabWindow(object browser)
    {
        if (browser is not IComServiceProvider serviceProvider)
        {
            return IntPtr.Zero;
        }

        var service = ShellBrowserId;
        var interfaceId = ShellBrowserId;
        if (serviceProvider.QueryService(ref service, ref interfaceId, out var result) != 0 || result is not IShellBrowser shellBrowser)
        {
            return IntPtr.Zero;
        }

        try
        {
            return shellBrowser.GetWindow(out var handle) == 0 ? handle : IntPtr.Zero;
        }
        finally
        {
            Marshal.ReleaseComObject(shellBrowser);
        }
    }

    private static HashSet<IntPtr> GetAllExplorerTabs()
    {
        var tabs = new HashSet<IntPtr>();
        var window = IntPtr.Zero;
        while ((window = FindWindowEx(IntPtr.Zero, window, ExplorerWindowClass, null)) != IntPtr.Zero)
        {
            tabs.UnionWith(GetTabs(window));
        }

        return tabs;
    }

    private static List<IntPtr> GetTabs(IntPtr window)
    {
        var tabs = new List<IntPtr>();
        var tab = IntPtr.Zero;
        while ((tab = FindWindowEx(window, tab, ExplorerTabClass, null)) != IntPtr.Zero)
        {
            tabs.Add(tab);
        }

        return tabs;
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? result);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow::GetWindow（IShellBrowser の先頭メソッド）のみ使用する
        [PreserveSig]
        int GetWindow(out IntPtr windowHandle);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}

public sealed record LaunchResult(bool Success, string Message, IReadOnlyList<string> MissingFolders, IReadOnlyList<string>? RemainingFolders = null);