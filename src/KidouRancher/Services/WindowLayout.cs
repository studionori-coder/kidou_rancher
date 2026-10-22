namespace KidouRancher.Services;

public static class WindowLayout
{
    /// <summary>
    /// 保存しておいたウィンドウの位置が、今つながっているどれかの画面に十分（タイトルバーを掴める程度）見えているか。
    /// モニターを外した・解像度を変えたあとに、画面外へ開いてしまうのを防ぐ。
    /// </summary>
    public static bool IsVisibleOnScreens(Rectangle bounds, IEnumerable<Rectangle> workingAreas)
    {
        if (bounds.Width < 100 || bounds.Height < 100)
        {
            return false;
        }

        // タイトルバー付近の帯が、どこかの画面と幅 100・高さ 20 以上重なっていればよい
        var titleBar = new Rectangle(bounds.Left, bounds.Top, bounds.Width, 30);
        return workingAreas.Any(area =>
        {
            var overlap = Rectangle.Intersect(area, titleBar);
            return overlap.Width >= 100 && overlap.Height >= 20;
        });
    }
}
