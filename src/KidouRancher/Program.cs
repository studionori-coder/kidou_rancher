namespace KidouRancher;

static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();

            // 画面の操作中に起きた想定外のエラーは、英語の標準画面ではなく日本語で知らせ、詳細を記録に残す
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportUnexpectedError(e.Exception);

            Application.Run(new MainForm());
        }
        catch (Exception exception)
        {
            var logPath = WriteLog("startup.log", exception, append: false);
            MessageBox.Show(
                $"ランチャーを起動できませんでした。\n\n{exception.Message}\n\n詳細: {logPath}",
                "起動エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }

    private static void ReportUnexpectedError(Exception exception)
    {
        var logPath = WriteLog("error.log", exception, append: true);
        MessageBox.Show(
            $"予期しないエラーが発生しました。操作はそのまま続けられます。\n\n{exception.Message}\n\n詳細: {logPath}",
            "エラー",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    /// <summary>%LocalAppData%\KidouRancher にエラーの詳細を書き、そのパスを返す（書けなければその旨の文を返す）。</summary>
    private static string WriteLog(string fileName, Exception exception, bool append)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KidouRancher");
        var logPath = Path.Combine(logDirectory, fileName);
        try
        {
            Directory.CreateDirectory(logDirectory);
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}";
            if (append)
            {
                File.AppendAllText(logPath, entry);
            }
            else
            {
                File.WriteAllText(logPath, entry);
            }

            return logPath;
        }
        catch
        {
            return "記録を書き込めませんでした。";
        }
    }
}
