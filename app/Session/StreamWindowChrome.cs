using System.Windows;
using System.Windows.Input;

namespace ChiakiNg.Session;

/// <summary>
/// PP836: which key toggles fullscreen, which is the half a Window is not needed to answer.
///
/// PP11 ported <c>fullscreenTime</c> and <c>normalTime</c> and nothing ever called them, because a
/// state machine with no key and no window is reachable only from a test. This is the key.
///
/// F11 IS THE CLIENT'S OWN AND IT CARRIES NO MODIFIER. QmlMainWindow::handleShortcut tests
/// <c>event->modifiers() == Qt::NoModifier</c> before the switch that reads Key_F11, so F11 with
/// control held is deliberately NOT the toggle there - control is the prefix for the five
/// shortcuts below it, and one of them is Ctrl+Q. Porting the key without the modifier test would
/// make Ctrl+F11 do something the client does not do.
///
/// ALT+ENTER IS AN ADDITION AND IS MARKED AS ONE. The client binds nothing to it; Windows does,
/// for every full-screen-capable application on the platform, and it is the combination a user of
/// this port looked for first. It is additive rather than a redesign: F11 still does exactly what
/// handleShortcut does, and the state machine underneath is untouched.
/// </summary>
public static class StreamWindowShortcut
{
    /// <summary>
    /// WPF delivers alt+enter as <see cref="Key.System"/> with the real key in SystemKey.
    ///
    /// The trap is that it is silent: a handler switching on <c>e.Key</c> alone sees System, falls
    /// through, and the combination reads as unbound rather than as mishandled - which is the same
    /// symptom as not having written the handler at all.
    /// </summary>
    /// <param name="key">KeyEventArgs.Key.</param>
    /// <param name="systemKey">KeyEventArgs.SystemKey, meaningful only where the first is System.</param>
    public static Key Resolve(Key key, Key systemKey) => key == Key.System ? systemKey : key;

    /// <summary>Whether this key and these modifiers are the fullscreen toggle.</summary>
    /// <param name="key">The key, already through <see cref="Resolve"/>.</param>
    /// <param name="modifiers">What was held down with it.</param>
    public static bool TogglesFullscreen(Key key, ModifierKeys modifiers) => key switch
    {
        // handleShortcut's own guard: no modifier, or it is not the shortcut.
        Key.F11 => modifiers == ModifierKeys.None,

        // And the platform's, which wants alt and nothing else - alt+shift+enter is not it.
        Key.Enter => modifiers == ModifierKeys.Alt,

        _ => false,
    };
}

/// <summary>
/// PP836: the ported state machine put onto a real window, which is what it never had.
///
/// <see cref="StreamWindowState"/> holds the three refusals PP11 found in the Qt client and says
/// nothing about WPF; this says what each of its fields means to a <see cref="Window"/>, and it is
/// the only place the two vocabularies meet.
///
/// THE ORDER IN <see cref="Apply"/> IS LOAD-BEARING AND IS WPF'S, not the client's. A window that
/// is Maximized keeps its maximized bounds when WindowStyle goes to None, so it covers the work
/// area and leaves the taskbar showing - the "fullscreen that isn't" every WPF application hits
/// once. Dropping to Normal first, restyling, and maximizing again is what actually covers the
/// screen, and the intermediate state is never painted because all three run in one handler.
/// </summary>
public static class StreamWindowChrome
{
    /// <summary>
    /// Put the state on the window.
    /// </summary>
    /// <param name="window">The stream's window.</param>
    /// <param name="state">The state machine, already toggled.</param>
    public static void Apply(Window window, StreamWindowState state)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(state);

        // The pinned minimum, which is the whole of PP11's point 2: set to the size on the way in
        // and back to zero on the way out, so a window cannot be left unable to shrink.
        window.MinWidth = state.MinimumSize.Width;
        window.MinHeight = state.MinimumSize.Height;

        if (state.Fullscreen)
        {
            window.WindowState = WindowState.Normal;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowState = WindowState.Maximized;

            return;
        }

        window.WindowStyle = WindowStyle.SingleBorderWindow;

        // PP11's point 3 reaches WPF here: Adjustable is false for a second after leaving, so the
        // window settles at its restored size before a drag can catch it mid-restore.
        window.ResizeMode = state.Adjustable ? ResizeMode.CanResize : ResizeMode.NoResize;
        window.WindowState = state.Maximized ? WindowState.Maximized : WindowState.Normal;
    }
}
