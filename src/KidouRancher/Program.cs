namespace KidouRancher;

static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception exception)
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KidouRancher");
            var logPath = Path.Combine(logDirectory, "startup.log");
            try
            {
                Directory.CreateDirectory(logDirectory);
                File.WriteAllText(logPath, exception.ToString());
            }
            catch
            {
                logPath = "起動ログを書き込めませんでした。";
            }

            MessageBox.Show(
                $"ランチャーを起動できませんでした。\n\n{exception.Message}\n\n詳細: {logPath}",
                "起動エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }    
}