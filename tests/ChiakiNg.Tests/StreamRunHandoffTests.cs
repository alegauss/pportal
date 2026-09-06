using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP759, under PP696: the handoff's mechanism, decided before the commit that has to write it.
///
/// PP752 named the call PP696 replaces and stopped there. What replaces it could not be written at
/// all: the handover and the runner are chiaki-shim exports, chiaki-shim links chiaki-lib one way,
/// and the session carries no hook of the shape the other two callbacks use.
///
/// SO THE COMMIT WOULD HAVE INVENTED A SIGNATURE AND TESTED IT IN THE SAME BREATH, which is exactly
/// what PP623's shape forbids - and a wrong one compiles. These hold the four decisions that a
/// plausible-looking signature gets wrong: who owns the reason, whether the socket crosses, that the
/// wait is a loop, and that the stop still has somewhere to go.
/// </summary>
public class StreamRunHandoffTests(ITestOutputHelper output)
{
    private static string? Header()
        => StreamRunHandoffSource.LocateHeader() is { } path ? File.ReadAllText(path) : null;

    private static string? Session()
        => FramePathConsumers.Locate(FramePathConsumers.SessionRelativePath) is { } path
            ? File.ReadAllText(path)
            : null;

    /// <summary>
    /// THE SETTER IS WRITTEN BESIDE TWO THAT EXIST, and those two are read rather than remembered.
    ///
    /// The claim this contract rests on is that the session already takes host-installed callbacks
    /// in one shape. If the two of them moved or changed, the third would be written to match a
    /// pattern that had gone - and nothing else in the port would have noticed.
    /// </summary>
    [Fact]
    public void TheShapeItIsWrittenBesideIsStillTheSessionsShape()
    {
        if (Header() is not { } header)
            return;

        Assert.True(
            StreamRunHandoffSource.TheTwoSettersAreStillThere(header),
            "session.h no longer declares the two callback setters this one is modelled on");

        Assert.Equal(2, StreamRunHandoff.SettersBesideIt.Count);
    }

    /// <summary>
    /// THE REASON IS BORROWED, and session.c's own strdup is what makes that true.
    ///
    /// A callback handing back owned memory would leak one string per session that ended with a
    /// remote disconnect - the quietest possible leak, on the path nobody runs twice in a sitting.
    /// The evidence is read out of the C rather than asserted as a preference.
    /// </summary>
    [Fact]
    public void TheReasonIsBorrowedBecauseTheSessionCopiesIt()
    {
        Assert.True(StreamRunHandoff.TheReasonIsBorrowed);

        if (Session() is not { } session)
            return;

        Assert.True(
            StreamRunHandoffSource.TheSessionStillCopiesTheReason(session),
            "session.c no longer copies the disconnect reason, so borrowing it is no longer safe");
    }

    /// <summary>
    /// THE WAIT IS A LOOP, because the export refuses to be asked for an unbounded one.
    ///
    /// Asserted against the built shim rather than against the C's text: a negative timeout is
    /// rejected, so "block until the session ends" is not a thing the trampoline can ask for once.
    /// A commit that asked once would end every stream at whatever number it picked.
    /// </summary>
    [Fact]
    public void TheWaitIsSlicedBecauseThereIsNoUnboundedOne()
    {
        using var handover = new StreamHandover();

        Assert.Equal(ChiakiError.InvalidData, handover.AwaitFinish(-1));

        // And a slice that has not been finished answers TIMEOUT, which is what makes it a loop
        // rather than an answer.
        Assert.Equal(ChiakiError.Timeout, handover.AwaitFinish(1));

        output.WriteLine($"slice {StreamRunHandoff.WaitSliceMs}ms");
        Assert.True(StreamRunHandoff.WaitSliceMs > 0);
    }

    /// <summary>
    /// AND THE STOP STILL GOES SOMEWHERE. PP758's after-flip list is the three wake-ups that are
    /// left, which is a floor - the reader takes them in order and allows more. This says the fourth
    /// is replaced rather than dropped.
    ///
    /// PP338 is what makes it matter: stopping is four pokes and not a flag, because the thread can
    /// be blocked in a condition wait, a socket select, or down in the run. A session that stopped
    /// poking the third of those hangs exactly when somebody quits a live stream.
    /// </summary>
    [Fact]
    public void TheFourthWakeUpIsReplacedRatherThanDropped()
    {
        Assert.Equal(4, SessionLifecycle.StopWakesUp.Count);
        Assert.Equal(3, SessionLifecycle.StopWakesUpAfterTheFlip.Count);

        // The one that leaves is the stream connection's, and the three that stay are the others.
        Assert.DoesNotContain(
            SessionLifecycle.StopWakesUpAfterTheFlip,
            one => one.Contains("stream_connection", StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(StreamRunHandoff.StopBecomes));
        Assert.StartsWith("chiaki_shim_", StreamRunHandoff.StopBecomes, StringComparison.Ordinal);
    }

    /// <summary>
    /// The socket crosses even though nothing on the far side reads it.
    ///
    /// The managed runner opens its own through the host it builds - it takes a builder and no
    /// socket, which is read here rather than claimed. Passing it anyway keeps the signature the one
    /// a runner that wanted it could use, instead of a change every caller has to be found for.
    /// </summary>
    [Fact]
    public void TheSocketCrossesEvenThoughTheRunnerOpensItsOwn()
    {
        Assert.True(StreamRunHandoff.TheSocketCrossesUnused);

        // The runner's only constructor takes how to build a host, and nothing else.
        Assert.Equal(
            typeof(Func<ManagedStreamRunHost>),
            typeof(ManagedStreamRunner).GetConstructors().Single().GetParameters().Single().ParameterType);
    }

    /// <summary>
    /// Every addition names a file, says what it writes and says why, and the three files are the
    /// ones PP696's own section names.
    /// </summary>
    [Fact]
    public void EveryAdditionSaysWhereItGoesAndWhy()
    {
        output.WriteLine(string.Join("\n", StreamRunHandoff.Additions.Select(one => one.Text)));

        Assert.All(
            StreamRunHandoff.Additions,
            one =>
            {
                Assert.False(string.IsNullOrWhiteSpace(one.Text));
                Assert.False(string.IsNullOrWhiteSpace(one.Why));
            });

        // The header, session.c and the shim - and nothing in test/, which is the rule PP623 gives
        // the one commit that edits the C.
        Assert.Equal(
            [StreamRunHandoff.HeaderRelativePath, StreamRunHandoff.SessionRelativePath, StreamRunHandoff.ShimRelativePath],
            StreamRunHandoff.Touches);

        Assert.DoesNotContain(StreamRunHandoff.Touches, one => one.StartsWith("test", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The setter is not there yet, which is what tells this tree from the one PP696 leaves.
    ///
    /// Asserted in both directions, as every two-shape reader in this tree is: once the hook is in,
    /// what this file describes has been carried out, and the check that would notice it being torn
    /// back out is this one.
    /// </summary>
    [Fact]
    public void TheHookIsWhereverTheTreeIsAndTheContractSaysWhich()
    {
        if (Header() is not { } header)
            return;

        bool installed = StreamRunHandoffSource.TheHookIsInstalled(header);
        output.WriteLine(installed ? "the hook is in" : "the hook is still owed");

        // Whichever it is, the header is the session's - which an empty string would not be.
        Assert.True(StreamRunHandoffSource.TheTwoSettersAreStillThere(header));

        Assert.Equal(
            installed,
            FramePathConsumers.SessionShape() != ConsumerShape.Asking
                || header.Contains(StreamRunHandoff.Setter, StringComparison.Ordinal));
    }
}
