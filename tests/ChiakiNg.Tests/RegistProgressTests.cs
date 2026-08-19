using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP37's fourth example: a dialog that stays open after the operation it waited on failed.
///
/// It does - and so does the one that succeeded, which is the part that makes this worth
/// asserting rather than assuming.
/// </summary>
public class RegistProgressTests
{
    private static RegistProgressViewModel Running()
    {
        var model = new RegistProgressViewModel();
        model.Start(accepted: true);
        return model;
    }

    /// <summary>
    /// A refused request shows no dialog. registerHost returns a bool before any callback
    /// arrives, so the alternative is an empty dialog that never fills and cannot be closed.
    /// </summary>
    [Fact]
    public void ARefusedRequestOpensNothing()
    {
        var model = new RegistProgressViewModel();
        model.Start(accepted: false);

        Assert.False(model.IsOpen);
        Assert.Equal("", model.Log);
    }

    [Fact]
    public void AnAcceptedRequestOpensAndIsNotClosableYet()
    {
        RegistProgressViewModel model = Running();

        Assert.True(model.IsOpen);
        Assert.False(model.IsClosable);
    }

    /// <summary>Messages accumulate, newline-terminated, in the order they arrived.</summary>
    [Fact]
    public void MessagesAccumulateInOrder()
    {
        RegistProgressViewModel model = Running();

        model.Progress("connecting");
        model.Progress("sending request");

        Assert.Equal("connecting\nsending request\n", model.Log);
    }

    /// <summary>
    /// The whole finding: finishing does not close the dialog, and does not branch on how it
    /// went. The Qt callback takes an `ok` and never reads it, so success and failure leave the
    /// same state - open, closable, log intact.
    /// </summary>
    [Fact]
    public void FinishingLeavesItOpenAndMerelyClosable()
    {
        RegistProgressViewModel model = Running();
        model.Progress("it did not work");
        model.Finished();

        Assert.True(model.IsOpen);
        Assert.True(model.IsClosable);
        Assert.Equal("it did not work\n", model.Log);
    }

    /// <summary>
    /// Return closes it only once it is closable - pressing it during a registration does nothing
    /// rather than confirming something that has not finished.
    /// </summary>
    [Fact]
    public void ReturnClosesItOnlyOnceItIsClosable()
    {
        RegistProgressViewModel model = Running();

        model.Confirm();
        Assert.True(model.IsOpen);

        model.Finished();
        model.Confirm();
        Assert.False(model.IsOpen);
    }

    /// <summary>
    /// Escape closes it WHENEVER, including mid-registration - `Keys.onEscapePressed:
    /// logDialog.close()` has no guard where the Return handler has one.
    ///
    /// That asymmetry is the rule: the dialog cannot be confirmed away early but can always be
    /// dismissed, which makes it modal-with-an-exit rather than a trap. Guard both and a user is
    /// left watching a registration they cannot leave.
    /// </summary>
    [Fact]
    public void EscapeClosesItEvenMidRegistration()
    {
        RegistProgressViewModel model = Running();
        model.Progress("halfway");

        Assert.False(model.IsClosable);
        model.Dismiss();

        Assert.False(model.IsOpen);
    }

    /// <summary>A second attempt does not show the first one's messages.</summary>
    [Fact]
    public void ASecondAttemptStartsWithAnEmptyLog()
    {
        RegistProgressViewModel model = Running();
        model.Progress("first attempt");
        model.Finished();
        model.Confirm();

        model.Start(accepted: true);

        Assert.Equal("", model.Log);
        Assert.False(model.IsClosable);
    }

    /// <summary>Messages arriving while nothing is open are dropped rather than banked.</summary>
    [Fact]
    public void MessagesOutsideAnAttemptAreDropped()
    {
        var model = new RegistProgressViewModel();
        model.Progress("stray");

        Assert.Equal("", model.Log);
    }

    /// <summary>
    /// Both halves are still the QML's: finishing only makes it closable, and the outcome
    /// argument is still unread. The second is the one a port "fixes" into a branch.
    /// </summary>
    [Fact]
    public void TheCallbackIsStillTheQmlsOwn()
    {
        string? file = RegistDialogSource.Locate();
        if (file is null)
            return;

        string qml = File.ReadAllText(file);

        Assert.True(RegistProgressSource.FinishingOnlyMakesItClosable(qml), "closable, not closed");
        Assert.True(RegistProgressSource.OutcomeIsIgnored(qml), "the outcome is still unread");
        Assert.True(RegistProgressSource.ConfirmIsGuardedAndDismissIsNot(qml), "return guarded, escape not");
    }
}
