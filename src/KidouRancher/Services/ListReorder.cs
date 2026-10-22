namespace KidouRancher.Services;

public static class ListReorder
{
    /// <summary>
    /// 指定位置の要素を、並び順を保ったまま insertIndex（移動前の一覧での挿入位置。0〜Count）へまとめて移動する。
    /// ドラッグ＆ドロップでの並び替えに使い、移動後の先頭要素の位置を返す。
    /// </summary>
    public static int Move<T>(List<T> list, IReadOnlyCollection<int> indices, int insertIndex)
    {
        var ordered = indices.Distinct().Where(index => index >= 0 && index < list.Count).Order().ToArray();
        if (ordered.Length == 0)
        {
            return -1;
        }

        insertIndex = Math.Clamp(insertIndex, 0, list.Count);
        var moving = ordered.Select(index => list[index]).ToList();

        // 挿入位置より前にある移動対象の数だけ、取り除いた後の挿入位置が前にずれる
        var adjustedInsertIndex = insertIndex - ordered.Count(index => index < insertIndex);
        for (var i = ordered.Length - 1; i >= 0; i--)
        {
            list.RemoveAt(ordered[i]);
        }

        list.InsertRange(adjustedInsertIndex, moving);
        return adjustedInsertIndex;
    }
}
