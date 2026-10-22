namespace KidouRancher.Services;

public sealed record TargetStatus(LaunchTargetKind Kind, bool IsValid, string Text);

/// <summary>
/// 登録項目が有効かどうかを確認する。フォルダー・ファイルは存在確認、
/// http/https のURLは実際にアクセスして応答を確認する。
/// </summary>
public static class TargetStatusChecker
{
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>フォルダー・ファイルの存在確認。ディスクやネットワークへのアクセスを伴うためUIスレッド外で呼ぶ。</summary>
    public static TargetStatus CheckPath(string value)
    {
        var kind = LaunchTarget.Classify(value);
        if (kind != LaunchTargetKind.Missing)
        {
            return new TargetStatus(kind, true, "有効");
        }

        // ドライブやファイルサーバー自体に届かないのか、その中に項目が無いのかを区別する
        var root = Path.GetPathRoot(value);
        if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
        {
            var text = root.StartsWith(@"\\", StringComparison.Ordinal) ? "サーバーに接続できません" : "ドライブが見つかりません";
            return new TargetStatus(kind, false, text);
        }

        return new TargetStatus(kind, false, "見つかりません");
    }

    public static async Task<TargetStatus> CheckUrlAsync(string url, CancellationToken cancellationToken)
    {
        // mailto: など http/https 以外は形式のみ確認する
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new TargetStatus(LaunchTargetKind.Url, true, "有効");
        }

        try
        {
            // HEAD に対応しないサイトが多いため、ヘッダーだけ受け取る GET で確認する
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var (isValid, text) = DescribeHttpStatus((int)response.StatusCode);
            return new TargetStatus(LaunchTargetKind.Url, isValid, text);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var text = exception is TaskCanceledException ? "応答がありません" : "接続できません";
            return new TargetStatus(LaunchTargetKind.Url, false, text);
        }
    }

    public static (bool IsValid, string Text) DescribeHttpStatus(int statusCode) => statusCode switch
    {
        < 400 => (true, "有効"),
        401 or 403 => (true, "有効（ログインが必要）"),
        405 or 429 => (true, "有効"),
        404 or 410 => (false, $"ページがありません ({statusCode})"),
        >= 500 => (false, $"サーバーエラー ({statusCode})"),
        _ => (false, $"エラー ({statusCode})")
    };

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) KidouRancher");
        return client;
    }
}
