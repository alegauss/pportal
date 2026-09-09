using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP835: the picture does not wait for a controller, asked of the file that decides.
///
/// A user reported the window freezing when a pad is connected after the stream has started, and
/// two live runs on 2026-09-08 hold it. A pad plugged in first: SDL opens it before any frame
/// arrives, 298 states reach the console, 950 frames decode. One connected during the handshake: the
/// pad line prints AFTER decoding has started, 43 states go, and the session ends with
/// `StreamConnection run failed` at 741 arrivals against 8211.
///
/// WHAT THE ORDER COSTS is everything in between. PadFeed.Start is allowed ten seconds for the SDL
/// thread and five more to open a controller, and the draw loop used to begin only afterwards. The
/// receiver is on its own thread and does not stop, so the codec's buffer fills and frames are
/// dropped to make room - a picture that has stopped over a stream that has not.
///
/// ASKED AS A HAZARD. Any arrangement keeping the blocking open off the drawing thread passes; only
/// the one that cost a session fails, so this does not go stale when the fix is written differently.
/// </summary>
public class PadOpeningSourceTests(ITestOutputHelper output)
{
    /// <summary>
    /// A blocking open in front of the draw is named, and one taken off the thread is not.
    ///
    /// Driven over text this test writes, because a tree with the fix in it cannot demonstrate what
    /// happens to one without - the same reason the phase drivers are asked this way.
    /// </summary>
    [Fact]
    public void AnOpenInFrontOfTheDrawIsNamed()
    {
        const string Waiting =
            "using var pad = new PadFeed(phase.Pad); "
                + "Console.WriteLine(pad.Start() ? pad.PadName : \"none\"); "
                + "int drawn = Draw(decoder, model, window, quit, stopping);";

        Assert.False(PadOpeningSource.TheRunOpensThePadOffTheDrawingThread(Waiting));

        const string OffTheThread =
            "using var pad = new PadFeed(phase.Pad); "
                + "Task<bool> opening = Task.Run(() => { bool open = pad.Start(); return open; }); "
                + "int drawn = Draw(decoder, model, window, quit, stopping);";

        Assert.True(PadOpeningSource.TheRunOpensThePadOffTheDrawingThread(OffTheThread));

        // Handed a file with no pad, or one with no draw, it says NO rather than "nothing to worry
        // about" - the convention DriftReadsTheFileTests holds every reader in this tree to, and the
        // reason is that the alternative passes on a machine that could not read the source.
        Assert.False(PadOpeningSource.TheRunOpensThePadOffTheDrawingThread("int drawn = Draw(decoder);"));
        Assert.False(
            PadOpeningSource.TheRunOpensThePadOffTheDrawingThread("var pad = new PadFeed(sink); pad.Start();"));
    }

    /// <summary>
    /// A NAME IN A COMMENT IS NOT A CALL, which is PP735's trap and why this reads code.
    ///
    /// The fix's own comment explains what the blocking open used to do, in the very region this
    /// check reads. A reader keyed on flat text would find that sentence and call it the hazard, on
    /// the tree that no longer has it.
    /// </summary>
    [Fact]
    public void ASentenceAboutTheOpenIsNotTheOpen()
    {
        // Written over real lines, because a line comment runs to the end of ITS line - a fixture
        // squashed onto one would have the comment swallow the code after it and fail for that.
        const string Described =
            """
            using var pad = new PadFeed(phase.Pad);
            // The draw loop used to begin only after pad.Start() returned.
            Task<bool> opening = Task.Run(() => pad.Start());
            int drawn = Draw(decoder, model, window, quit, stopping);
            """;

        Assert.True(PadOpeningSource.TheRunOpensThePadOffTheDrawingThread(Described));

        // And the same text with the real open taken out is not a pass on the strength of prose.
        Assert.False(
            PadOpeningSource.TheRunOpensThePadOffTheDrawingThread(
                Described.Replace(
                    "Task<bool> opening = Task.Run(() => pad.Start());", string.Empty, StringComparison.Ordinal)));
    }

    /// <summary>
    /// AND THE TREE ITSELF, which is the assertion that fails without this task.
    ///
    /// An empty read is a machine with no checkout rather than a pass.
    /// </summary>
    [Fact]
    public void ThisRunDrawsWithoutWaitingForAPad()
    {
        if (PadOpeningSource.LocateRun() is not { } path)
            return;

        output.WriteLine(path);

        Assert.True(
            PadOpeningSource.TheRunOpensThePadOffTheDrawingThread(File.ReadAllText(path)),
            "StreamRun opens the pad on the thread that draws again, so a controller connected while "
                + "the session is coming up holds the picture for up to fifteen seconds while the "
                + "receiver keeps decoding into a codec whose buffer is already full");
    }
}
