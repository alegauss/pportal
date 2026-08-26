using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP350, under PP294: the ctrl channel's two pipes, and which caller pokes which.
///
/// Stated nowhere in the tree, and the two failures of getting it wrong are opposite: one pipe for
/// both jobs either cancels every send or never wakes for a queued message.
/// </summary>
public class CtrlPipesTests
{
    /// <summary>
    /// Each pipe does one job, and neither does the other's.
    /// </summary>
    [Fact]
    public void EachPipeDoesOneJob()
    {
        Assert.True(CtrlPipes.WakesTheSelect(CtrlPipe.Notify));
        Assert.False(CtrlPipes.WakesTheSelect(CtrlPipe.Stop));

        Assert.True(CtrlPipes.CancelsASendInProgress(CtrlPipe.Stop));
        Assert.False(CtrlPipes.CancelsASendInProgress(CtrlPipe.Notify));
    }

    /// <summary>
    /// A STOP POKES BOTH, because it has to reach either wait.
    ///
    /// This is the one place the two overlap, and the first note written about it had it wrong -
    /// which is why it is asserted against the source below rather than only described.
    /// </summary>
    [Fact]
    public void AStopPokesBoth()
    {
        Assert.Equal([CtrlPipe.Stop, CtrlPipe.Notify], CtrlPipes.Stopping);
    }

    /// <summary>Everything else pokes only the pipe the select is on.</summary>
    [Fact]
    public void EverythingElsePokesOnlyTheNotifyPipe()
    {
        Assert.Equal([CtrlPipe.Notify], CtrlPipes.Queueing);
        Assert.Equal([CtrlPipe.Notify], CtrlPipes.HandingOverAPin);
    }

    /// <summary>
    /// And ctrl.c still pokes what this says it pokes, function by function.
    ///
    /// Read out of the source rather than trusted, because the note about it was wrong once.
    /// </summary>
    [Fact]
    public void CtrlStillPokesWhatEachCallerShould()
    {
        string? path = CtrlPipesSource.Locate();
        if (path is null)
            return;

        Assert.Equal(
            CtrlPipes.Stopping,
            CtrlPipesSource.PipesPokedBy(path, "chiaki_ctrl_stop"));

        Assert.Equal(
            CtrlPipes.Queueing,
            CtrlPipesSource.PipesPokedBy(path, "chiaki_ctrl_send_message"));

        Assert.Equal(
            CtrlPipes.HandingOverAPin,
            CtrlPipesSource.PipesPokedBy(path, "chiaki_ctrl_set_login_pin"));
    }

    /// <summary>And the two waits are still given the pipe each needs.</summary>
    [Fact]
    public void TheTwoWaitsStillGetTheRightPipe()
    {
        string? path = CtrlPipesSource.Locate();
        if (path is null)
            return;

        string? thread = ChiakiNg.Session.CFunction.BodyIn(path, "static void *ctrl_thread_func");
        string? send = ChiakiNg.Session.CFunction.BodyIn(path, "static ChiakiErrorCode ctrl_message_send(");

        Assert.NotNull(thread);
        Assert.NotNull(send);

        Assert.True(
            CtrlPipesSource.TheSelectStillWaitsOnTheNotifyPipe(thread),
            "the loop's select is no longer on the notify pipe, so queued work would never wake it");
        Assert.True(
            CtrlPipesSource.ASendIsStillGivenTheStopPipe(send),
            "a blocking send is no longer given the stop pipe, so a stop mid-write would not reach it");
    }
}
