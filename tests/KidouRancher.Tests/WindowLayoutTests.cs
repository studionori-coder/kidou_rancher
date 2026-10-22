using System.Drawing;
using KidouRancher.Services;

namespace KidouRancher.Tests;

[TestClass]
public sealed class WindowLayoutTests
{
    private static readonly Rectangle[] Screens = [new Rectangle(0, 0, 1920, 1040), new Rectangle(1920, 0, 1280, 984)];

    [TestMethod]
    public void IsVisibleOnScreens_AcceptsWindowsOnAnyScreen()
    {
        Assert.IsTrue(WindowLayout.IsVisibleOnScreens(new Rectangle(100, 100, 940, 620), Screens));
        Assert.IsTrue(WindowLayout.IsVisibleOnScreens(new Rectangle(2000, 50, 940, 620), Screens));
    }

    [TestMethod]
    public void IsVisibleOnScreens_RejectsWindowsLeftOnARemovedScreen()
    {
        Assert.IsFalse(WindowLayout.IsVisibleOnScreens(new Rectangle(3500, 100, 940, 620), Screens));
        Assert.IsFalse(WindowLayout.IsVisibleOnScreens(new Rectangle(100, -500, 940, 620), Screens));
    }

    [TestMethod]
    public void IsVisibleOnScreens_RejectsTinyWindows() =>
        Assert.IsFalse(WindowLayout.IsVisibleOnScreens(new Rectangle(100, 100, 20, 20), Screens));
}
