using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP229: a press reaching the document and the grid, with no pad and no window.
///
/// PP227 drives the real window and stops one step short - it opens a capture and closes it, and
/// never presses anything, because a press comes from a device. This is that last link, and it does
/// not need one: PP8 built <see cref="Gamepads.PushEvent"/> so the pump could be exercised with no
/// controller, and its events go through SDL and back out rather than to a callback directly.
///
/// What the capture reads is an event's TYPE and INDEX. The `which` field SDL rewrites on the way
/// through is the one thing it never looks at, which is what makes a pushed event a press to
/// everything above the queue.
///
/// WHAT THIS DOES NOT CLAIM is the device. A real DualSense producing these events at these indices
/// is PP219 and PP220's evidence, measured through the pad; this is everything that happens after
/// one arrives. Neither substitutes for the other.
///
/// Marked Interaction because it starts SDL, which is a subsystem and a thread rather than a
/// calculation - not a thing the gate should carry.
/// </summary>
[Trait("Category", "Interaction")]
public class PressReachesTheGridTests(ITestOutputHelper output)
{
    private const int Cross = 1 << 0;

    /// <summary>A mapping with Cross on b0, so a press onto b7 is visibly a change.</summary>
    private const string Mapping = "030057564c05,*,a:b0,b:b1,";

    /// <summary>
    /// The whole chain: SDL's queue, the polling thread, the capture, the session, the document and
    /// the rows the screen binds to.
    /// </summary>
    [Fact]
    public void APressReachesTheDocumentAndTheRows()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse(Mapping, "DualSense Wireless Controller")!;

        // The marshal runs the work where it is handed over. In the application this is the
        // dispatcher; here it is this thread, which is what makes the assertion follow the press.
        var marshalled = new List<Action>();
        var session = new ControllerMappingSession(document, marshalled.Add);

        using var sdl = new SdlThread(session.OnSdlEvent);
        Assert.Equal(SdlStart.Started, sdl.Start(TimeSpan.FromSeconds(10)));

        Assert.Equal("b0", document.Physical("a").FirstOrDefault());

        session.OpenCapture(Cross, buttonIndex: 0, mappingIndex: 0);
        Assert.True(session.Armed);

        // A press, from SDL's own queue. Pushed ON the SDL thread, because the queue belongs to
        // whichever thread initialised the subsystem - the same rule everything else here follows.
        bool pushed = false;
        sdl.Invoke(
            () => pushed = Gamepads.PushEvent(Gamepads.EventType.JoyButtonDown, which: 0, index: 7),
            TimeSpan.FromSeconds(5));

        Assert.True(pushed, "SDL refused the pushed event");

        // The poll interval is four milliseconds, so this is a bound rather than a wait.
        Assert.True(
            SpinFor(() => marshalled.Count > 0, TimeSpan.FromSeconds(5)),
            "the pushed press never reached the session");

        Assert.False(session.Armed, "the capture should disarm itself on the press it took");

        // Nothing has touched the document yet: the token is waiting on the marshal, which in the
        // application is the dispatcher and here is this list.
        Assert.Equal("b0", document.Physical("a").FirstOrDefault());

        foreach (Action work in marshalled)
            work();

        output.WriteLine($"Cross is now bound to {string.Join(", ", document.Physical("a"))}");

        // b7 in front, because index zero PREPENDS - which is the document's rule, exercised here
        // by a press rather than by a call.
        Assert.Equal("b7", document.Physical("a").FirstOrDefault());

        MappingRowView cross = Assert.Single(session.Screen.Rows, row => row.Value == Cross);
        Assert.Equal("b7", cross.First);
        Assert.True(session.Screen.Altered);
        Assert.True(session.Screen.CanApply);

        sdl.Stop(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// And an event nobody armed for changes nothing, which is what says the assertion above is
    /// about the capture rather than about anything that moves.
    /// </summary>
    [Fact]
    public void AnUnarmedPressChangesNothing()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse(Mapping, "DualSense Wireless Controller")!;

        var marshalled = new List<Action>();
        var session = new ControllerMappingSession(document, marshalled.Add);

        using var sdl = new SdlThread(session.OnSdlEvent);
        Assert.Equal(SdlStart.Started, sdl.Start(TimeSpan.FromSeconds(10)));

        bool pushed = false;
        sdl.Invoke(
            () => pushed = Gamepads.PushEvent(Gamepads.EventType.JoyButtonDown, which: 0, index: 7),
            TimeSpan.FromSeconds(5));

        Assert.True(pushed, "SDL refused the pushed event");

        // Given the same bound the other test passes inside, and nothing arrives.
        Assert.False(
            SpinFor(() => marshalled.Count > 0, TimeSpan.FromSeconds(2)),
            "an unarmed capture took a press");

        Assert.Equal("b0", document.Physical("a").FirstOrDefault());
        Assert.False(session.Screen.Altered);

        sdl.Stop(TimeSpan.FromSeconds(5));
    }

    /// <summary>Waits for a condition, and answers whether it came true rather than throwing.</summary>
    private static bool SpinFor(Func<bool> until, TimeSpan bound)
    {
        DateTime deadline = DateTime.UtcNow + bound;
        while (DateTime.UtcNow < deadline)
        {
            if (until())
                return true;

            Thread.Sleep(20);
        }

        return until();
    }
}
