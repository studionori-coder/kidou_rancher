using System.Globalization;
using KidouRancher.Models;

namespace KidouRancher.Services;

/// <summary>
/// 検索ボックスの絞り込み。プロジェクト名・表題・パス／URL のどこかに検索語を含むものを残す。
/// 大文字と小文字、全角と半角、ひらがなとカタカナの違いは区別しない（「けいり」で「ケイリ」、「ｴｸｾﾙ」で「エクセル」も見つかる）。
/// </summary>
public static class SearchFilter
{
    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

    public static bool Contains(string? text, string query) =>
        query.Length == 0 || (!string.IsNullOrEmpty(text) && CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, query, Options) >= 0);

    public static bool ItemMatches(LauncherProject project, string item, string query) =>
        Contains(item, query) || Contains(project.GetTitle(item), query);

    /// <summary>プロジェクト名か、そのどれかの項目が検索語を含めば一覧に残す。</summary>
    public static bool ProjectMatches(LauncherProject project, string query) =>
        query.Length == 0 || Contains(project.Name, query) || project.Folders.Any(item => ItemMatches(project, item, query));

    /// <summary>右の一覧に出す項目。プロジェクト名で当たったときは全部、項目で当たったときはその項目だけ。</summary>
    public static IReadOnlyList<string> VisibleItems(LauncherProject project, string query) =>
        query.Length == 0 || Contains(project.Name, query)
            ? project.Folders
            : project.Folders.Where(item => ItemMatches(project, item, query)).ToList();
}
