using NUnit.Framework;

/// <summary>
/// When a hiding stay is an escape (plan R1, D28): not at the door, only once the player has been
/// out for a while, uncaught and unchased; and one outlasted search per hunt.
/// </summary>
public class HidingStayBookTests
{
    private const float Confirm = HidingStayBook.DefaultConfirmSeconds;

    private HidingStayBook book;

    [SetUp]
    public void SetUp() => book = new HidingStayBook();

    [Test]
    public void AStayTheNemesisNeverHuntedNear_IsNoEscape()
    {
        book.Enter("locker_01", out _);
        book.Exit(true, 0f);

        Assert.IsFalse(book.HasPendingEscape);
        Assert.IsFalse(book.TryConfirm(Confirm + 1f, false, out _));
    }

    [Test]
    public void AHuntedStay_IsConfirmedOnlyAfterTheWindow()
    {
        book.Enter("locker_01", out _);
        book.MarkHunted();
        book.Exit(true, 10f);

        Assert.IsTrue(book.HasPendingEscape);
        Assert.IsFalse(book.TryConfirm(10f + Confirm - 0.1f, false, out _));

        Assert.IsTrue(book.TryConfirm(10f + Confirm, false, out HidingStayBook.Escape escape));
        Assert.AreEqual("locker_01", escape.SpotKey);
        Assert.IsFalse(book.HasPendingEscape);
    }

    [Test]
    public void CaughtRightAfterGettingOut_IsNoEscape()
    {
        book.Enter("locker_01", out _);
        book.NoteSearchOutlasted(1);
        book.Exit(true, 0f);

        Assert.IsTrue(book.CancelPending());
        Assert.IsFalse(book.TryConfirm(Confirm + 1f, false, out _));
    }

    [Test]
    public void AChaseAfterGettingOut_HoldsTheEscapeUntilItEnds()
    {
        book.Enter("locker_01", out _);
        book.MarkHunted();
        book.Exit(true, 0f);

        Assert.IsFalse(book.TryConfirm(30f, true, out _));
        Assert.IsTrue(book.TryConfirm(31f, false, out _));
    }

    [Test]
    public void AWayOutThePlayerDidNotChoose_LeavesNothing()
    {
        book.Enter("locker_01", out _);
        book.NoteSearchOutlasted(1);
        book.Exit(false, 0f);

        Assert.IsFalse(book.HasPendingEscape);
        Assert.IsNull(book.StayKey);
    }

    [Test]
    public void SearchesOutlasted_CountOncePerHunt()
    {
        book.Enter("locker_01", out _);

        Assert.IsTrue(book.NoteSearchOutlasted(7));
        Assert.IsFalse(book.NoteSearchOutlasted(7));
        Assert.IsTrue(book.NoteSearchOutlasted(8));
        Assert.AreEqual(2, book.SearchesOutlasted);

        book.Exit(true, 0f);
        Assert.IsTrue(book.TryConfirm(Confirm, false, out HidingStayBook.Escape escape));
        Assert.AreEqual(2, escape.SearchesOutlasted);
    }

    [Test]
    public void AnOutlastedSearch_CountsAsHuntingNearby()
    {
        book.Enter("table_02", out _);
        book.NoteSearchOutlasted(1);

        Assert.IsTrue(book.IsHunted);
    }

    [Test]
    public void ANewStay_StartsClean_EvenInTheSameHunt()
    {
        book.Enter("locker_01", out _);
        book.NoteSearchOutlasted(3);
        book.Exit(false, 0f);

        book.Enter("locker_01", out _);
        Assert.IsFalse(book.IsHunted);
        Assert.AreEqual(0, book.SearchesOutlasted);
        Assert.IsTrue(book.NoteSearchOutlasted(3));
    }

    [Test]
    public void HidingAgain_ConfirmsThePendingEscape()
    {
        book.Enter("locker_01", out _);
        book.MarkHunted();
        book.Exit(true, 0f);

        Assert.IsTrue(book.Enter("table_02", out HidingStayBook.Escape confirmed));
        Assert.AreEqual("locker_01", confirmed.SpotKey);
        Assert.IsFalse(book.HasPendingEscape);
        Assert.AreEqual("table_02", book.StayKey);
    }

    [Test]
    public void OutsideAStay_NothingCounts()
    {
        Assert.IsFalse(book.NoteSearchOutlasted(1));
        book.MarkHunted();
        book.Exit(true, 0f);

        Assert.IsFalse(book.IsHunted);
        Assert.IsFalse(book.HasPendingEscape);
    }

    [Test]
    public void Clear_ForgetsThePendingEscape()
    {
        book.Enter("locker_01", out _);
        book.MarkHunted();
        book.Exit(true, 0f);

        book.Clear();

        Assert.IsFalse(book.HasPendingEscape);
        Assert.IsFalse(book.TryConfirm(Confirm + 1f, false, out _));
    }

    [Test]
    public void Clear_ForgetsTheStayInProgress()
    {
        book.Enter("table_02", out _);
        book.NoteSearchOutlasted(1);

        book.Clear();

        Assert.IsNull(book.StayKey);
        Assert.IsFalse(book.IsHunted);
        Assert.AreEqual(0, book.SearchesOutlasted);
    }
}
