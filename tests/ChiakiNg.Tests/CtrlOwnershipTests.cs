using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP355: everything an outside caller allocates into the ctrl channel is freed at teardown.
/// </summary>
public class CtrlOwnershipTests
{
    /// <summary>
    /// THE CHECK: nothing handed over is left unreleased.
    ///
    /// Written as a symmetry over both fields rather than as a check on the queue. fini already
    /// freed the PIN, so teardown ownership had been thought about and one of two was missed - which
    /// is exactly the shape a third would take.
    /// </summary>
    [Fact]
    public void NothingHandedOverIsLeftUnreleased()
    {
        string? path = CtrlOwnership.Locate();
        if (path is null)
            return;

        string? fini = CtrlOwnership.FiniBody(path);
        Assert.NotNull(fini);

        IReadOnlyList<string> missed = CtrlOwnership.NotReleasedAtTeardown(fini);

        Assert.True(
            missed.Count == 0,
            "these are allocated by callers and never released at teardown:\n  "
                + string.Join("\n  ", missed));
    }

    /// <summary>Both fields are on the list, so it cannot quietly go empty.</summary>
    [Fact]
    public void BothHandedOverFieldsAreOnTheList()
    {
        Assert.Equal(["login_pin", "msg_queue"], CtrlOwnership.HandedOverByCallers);
    }

    /// <summary>And the reader finds the one that was missing, so the check means something.</summary>
    [Fact]
    public void TheReaderFindsTheQueueMissing()
    {
        const string asItWas = """
            	chiaki_stop_pipe_fini(&ctrl->stop_pipe);
            	chiaki_stop_pipe_fini(&ctrl->notif_pipe);
            	chiaki_mutex_fini(&ctrl->notif_mutex);
            	free(ctrl->login_pin);
            """;

        Assert.Equal(["msg_queue"], CtrlOwnership.NotReleasedAtTeardown(asItWas));
    }

    /// <summary>A fini that freed neither is two findings, not one.</summary>
    [Fact]
    public void TheReaderFindsBothWhereNeitherIsFreed()
    {
        const string neither = """
            	chiaki_mutex_fini(&ctrl->notif_mutex);
            """;

        Assert.Equal(["login_pin", "msg_queue"], CtrlOwnership.NotReleasedAtTeardown(neither));
    }

    /// <summary>
    /// AND THE LOOP STILL DRAINS RATHER THAN DISCARDS.
    ///
    /// The teardown free is a backstop. The loop drains because a stop should SEND what was queued -
    /// goto-bed pressed as the session ends is meant to reach the console - and freeing at teardown
    /// must not become the reason nobody notices the drain going away.
    /// </summary>
    [Fact]
    public void TheLoopStillSendsWhatWasQueuedBeforeFreeingIt()
    {
        string? path = CtrlOwnership.Locate();
        if (path is null)
            return;

        string? thread = CFunction.BodyIn(path, "static void *ctrl_thread_func");
        Assert.NotNull(thread);

        Assert.True(
            CtrlOwnership.TheLoopStillDrainsRatherThanDiscards(thread),
            "the loop no longer sends queued messages before freeing them");
    }
}
