using System.IO;
using System.Windows.Input;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP836: the key that toggles fullscreen, and the window that has one at last.
///
/// PP11 ported the state machine and asserted every one of its refusals; what none of those tests
/// could say is that anything CALLS it. These are the other half - the shortcut's own decisions,
/// which need no window, and the join in the run, read out of the source the way PP808's is.
/// </summary>
public class StreamWindowChromeTests(ITestOutputHelper output)
{
    /// <summary>
    /// F11 carries no modifier, which is handleShortcut's own guard and not a tidy-up.
    ///
    /// The client tests <c>event->modifiers() == Qt::NoModifier</c> before it reads the key, and
    /// control is the prefix for the five shortcuts underneath - Ctrl+Q closes the window. A port
    /// that accepted Ctrl+F11 would be inventing a binding, and it would be inventing one next to
    /// a shortcut that quits.
    /// </summary>
    [Theory]
    [InlineData(Key.F11, ModifierKeys.None, true)]
    [InlineData(Key.F11, ModifierKeys.Control, false)]
    [InlineData(Key.F11, ModifierKeys.Alt, false)]
    [InlineData(Key.F11, ModifierKeys.Shift, false)]
    public void FullscreenIsF11WithNothingHeldDown(Key key, ModifierKeys held, bool toggles)
        => Assert.Equal(toggles, StreamWindowShortcut.TogglesFullscreen(key, held));

    /// <summary>
    /// And alt+enter, which is the platform's and is an addition to the client's set.
    ///
    /// Alt and nothing else: alt+shift+enter is a different combination and this port does not get
    /// to claim it. Enter on its own is emphatically NOT the toggle - the client maps Key_Return to
    /// CHIAKI_CONTROLLER_BUTTON_CROSS, so swallowing a bare Enter would eat the button a person
    /// presses to get through a game's menus.
    /// </summary>
    [Theory]
    [InlineData(Key.Enter, ModifierKeys.Alt, true)]
    [InlineData(Key.Enter, ModifierKeys.None, false)]
    [InlineData(Key.Enter, ModifierKeys.Alt | ModifierKeys.Shift, false)]
    [InlineData(Key.Enter, ModifierKeys.Control, false)]
    public void AltEnterTogglesAndABareEnterIsStillTheCrossButton(
        Key key, ModifierKeys held, bool toggles)
        => Assert.Equal(toggles, StreamWindowShortcut.TogglesFullscreen(key, held));

    /// <summary>
    /// WPF hands alt+enter over as System, and a handler reading Key alone never sees it.
    ///
    /// This is the failure that looks like an unbound key rather than a bug, which is why it is
    /// asserted rather than commented: Resolve is the line between "alt+enter does nothing because
    /// nobody wrote the handler" and "alt+enter does nothing because the handler read the wrong
    /// field", and those are indistinguishable from in front of the window.
    /// </summary>
    [Fact]
    public void AltEnterArrivesAsSystemAndIsResolvedBackToEnter()
    {
        Assert.Equal(Key.Enter, StreamWindowShortcut.Resolve(Key.System, Key.Enter));

        // And an ordinary key is passed straight through, SystemKey being meaningless there.
        Assert.Equal(Key.F11, StreamWindowShortcut.Resolve(Key.F11, Key.None));

        // The whole path, as the handler runs it.
        Assert.True(
            StreamWindowShortcut.TogglesFullscreen(
                StreamWindowShortcut.Resolve(Key.System, Key.Enter), ModifierKeys.Alt));
    }

    /// <summary>
    /// The toggle is the state machine's, and it keeps the guard that protects WasMaximized.
    ///
    /// handleShortcut chooses between fullscreenTime and normalTime on the window state; this
    /// asserts the port chooses the same way and that a maximized window still comes back maximized
    /// after a round trip, which is the thing PP11's early-return guard exists for.
    /// </summary>
    [Fact]
    public void TogglingGoesInAndOutAndRestoresWhatTheWindowWas()
    {
        var state = new StreamWindowState();
        state.Resize(1600, 900);
        state.SetMaximized(true);

        Assert.True(state.Toggle());
        Assert.True(state.Fullscreen);
        Assert.True(state.WasMaximized);
        Assert.Equal((1600, 900), state.MinimumSize);
        Assert.False(state.Adjustable);

        Assert.False(state.Toggle());
        Assert.False(state.Fullscreen);

        // Back to maximized, and the pinned minimum released so the window can shrink again.
        Assert.True(state.Maximized);
        Assert.Equal((0, 0), state.MinimumSize);

        // And still not adjustable: that comes back a second later and not with the restore.
        Assert.False(state.Adjustable);
        Assert.Equal(StreamWindowState.AdjustableDelay, state.AdjustableAfter);

        state.AdjustableDelayElapsed();
        Assert.True(state.Adjustable);

        output.WriteLine($"maximized={state.Maximized} adjustable={state.Adjustable}");
    }

    /// <summary>
    /// PP836: and the run binds it, which is the line whose absence was the whole defect.
    ///
    /// Read out of the source for PP808's reason - the window is built inside a method that shows a
    /// dialog, so the join is reachable only from a run somebody is watching. Anchored on the
    /// handler's own call rather than on the type: StreamWindowState has been in this tree since
    /// PP11 and a census counting types was green the entire time it had no caller.
    /// </summary>
    [Fact]
    public void TheRunBindsTheShortcutToTheWindow()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\StreamRun.cs")
            ?? throw new InvalidOperationException("the run is not in this checkout."));

        // A key handler on the window at all.
        Assert.Matches(@"(?m)^\s*window\.PreviewKeyDown\s*\+=", text);

        // That asks this shortcut, and applies the result to the window.
        Assert.Contains("StreamWindowShortcut.TogglesFullscreen", text, StringComparison.Ordinal);
        Assert.Contains("StreamWindowChrome.Apply(window, chrome)", text, StringComparison.Ordinal);
    }
}
