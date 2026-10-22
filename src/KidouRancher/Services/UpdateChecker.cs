using System.Text.Json;

namespace KidouRancher.Services;

/// <summary>
/// GitHub の Releases に新しいバージョンが出ているかを確認する。
/// 通信できない・リポジトリが非公開などで確認できないときは、何も知らせない（null を返す）。
/// </summary>
public static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/studionori-coder/kidou_rancher/releases/latest";
    private const string LatestReleaseApi = "https://api.github.com/repos/studionori-coder/kidou_rancher/releases/latest";

    /// <summary>"v1.0.2" のようなタグをバージョンにする。比較のため「メジャー.マイナー.ビルド」の3つにそろえる。</summary>
    public static Version? ParseVersion(string? tag)
    {
        var text = tag?.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    public static bool IsNewer(string? latestTag, Version current) =>
        ParseVersion(latestTag) is { } latest && latest > Normalize(current);

    /// <summary>新しいバージョンがあればそのタグ（"v1.0.3" など）、無ければ null。</summary>
    public static async Task<string?> GetNewerVersionAsync(Version current, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KidouRancher");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await client.GetAsync(LatestReleaseApi, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var tag = document.RootElement.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;
            return IsNewer(tag, current) ? tag : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
