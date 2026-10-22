using KidouRancher.Services;

namespace KidouRancher.Tests;

[TestClass]
public sealed class ListReorderTests
{
    [TestMethod]
    public void Move_MovesItemDown()
    {
        var list = new List<string> { "A", "B", "C", "D" };

        var newIndex = ListReorder.Move(list, [0], 3);

        CollectionAssert.AreEqual(new[] { "B", "C", "A", "D" }, list);
        Assert.AreEqual(2, newIndex);
    }

    [TestMethod]
    public void Move_MovesItemUpToTop()
    {
        var list = new List<string> { "A", "B", "C", "D" };

        var newIndex = ListReorder.Move(list, [3], 0);

        CollectionAssert.AreEqual(new[] { "D", "A", "B", "C" }, list);
        Assert.AreEqual(0, newIndex);
    }

    [TestMethod]
    public void Move_MovesItemToEnd()
    {
        var list = new List<string> { "A", "B", "C" };

        ListReorder.Move(list, [0], 3);

        CollectionAssert.AreEqual(new[] { "B", "C", "A" }, list);
    }

    [TestMethod]
    public void Move_DroppingOnItsOwnPositionKeepsOrder()
    {
        var list = new List<string> { "A", "B", "C" };

        ListReorder.Move(list, [1], 1);
        ListReorder.Move(list, [1], 2);

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, list);
    }

    [TestMethod]
    public void Move_MovesSeveralItemsAsBlockKeepingTheirOrder()
    {
        var list = new List<string> { "A", "B", "C", "D", "E" };

        var newIndex = ListReorder.Move(list, [3, 0], 2);

        CollectionAssert.AreEqual(new[] { "B", "A", "D", "C", "E" }, list);
        Assert.AreEqual(1, newIndex);
    }

    [TestMethod]
    public void Move_IgnoresOutOfRangeIndices()
    {
        var list = new List<string> { "A", "B" };

        Assert.AreEqual(-1, ListReorder.Move(list, [5], 0));
        CollectionAssert.AreEqual(new[] { "A", "B" }, list);
    }
}
