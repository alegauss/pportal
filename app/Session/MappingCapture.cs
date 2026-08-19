using System.Globalization;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP126: what a button press becomes while the mapping screen is waiting for one.
///
/// The mapping screen arms a capture, the user presses something, and what is recorded is a token
/// naming the physical control: `b3` for a button, `a0` for an axis, `h0.1` for a hat direction.
/// Those tokens go into the controller_mappings the Qt client stores (PP81 reads them), so a port
/// that spelled them differently would write mappings the other client cannot use - and read the
/// user's existing ones as gibberish.
///
/// Three rules live here, and every one of them is a decision rather than a detail:
///
///   an axis is captured only when analog mapping was asked for. Otherwise a stick resting off
///   centre - which is most sticks - captures the binding before the user has touched anything;
///
///   a button is captured on the way DOWN and never on the way up. controllermanager.cpp returns
///   on SDL_JOYBUTTONUP without looking, so a capture takes the press and the release that follows
///   it belongs to nobody;
///
///   and one arming captures one control. The capture disarms itself, so the events that keep
///   arriving while a finger is still on the pad do not overwrite what was just recorded.
///
/// <see cref="MappingSource"/> holds the token formats against the Qt client's own.
/// </summary>
public sealed class MappingCapture
{
    private bool armed;

    /// <summary>Whether an analog stick may be captured. False is the Qt client's default.</summary>
    public bool AllowAnalogStick { get; set; }

    /// <summary>Whether a capture is waiting for a control.</summary>
    public bool IsArmed => armed;

    /// <summary>Arms one capture. The next control that qualifies takes it and disarms.</summary>
    public void Arm() => armed = true;

    /// <summary>Cancels a waiting capture - the screen closing, or the user pressing escape.</summary>
    public void Disarm() => armed = false;

    /// <summary>
    /// The token for an event, or null when it captures nothing - which is most events, including
    /// every one that arrives while nothing is armed.
    /// </summary>
    public string? Offer(SdlEvent ev)
    {
        if (!armed)
            return null;

        string? token = ev.Type switch
        {
            Gamepads.EventType.JoyAxisMotion when AllowAnalogStick =>
                "a" + ev.Index.ToString(CultureInfo.InvariantCulture),
            Gamepads.EventType.JoyButtonDown =>
                "b" + ev.Index.ToString(CultureInfo.InvariantCulture),
            Gamepads.EventType.JoyHatMotion =>
                "h" + ev.Index.ToString(CultureInfo.InvariantCulture)
                    + "." + ev.Value.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

        if (token is not null)
            armed = false;

        return token;
    }
}

/// <summary>
/// PP126: the token formats, read out of controllermanager.cpp rather than remembered.
///
/// These strings are a wire format between two clients that share one settings store. A port that
/// wrote `axis0` instead of `a0` would produce mappings the Qt client cannot read, and the only
/// symptom is a controller that does nothing on one of them.
/// </summary>
public static partial class MappingSource
{
    /// <summary>The Qt client's mapping code.</summary>
    public const string RelativePath = @"gui\src\controllermanager.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The Qt format strings for the three tokens, as they appear in the emit calls - "a%1",
    /// "b%1" and "h%1.%2". Extracted rather than compared literally so that a reordering of the
    /// switch does not read as a difference.
    /// </summary>
    public static IReadOnlySet<string> TokenFormats(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return FormatRegex().Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether an axis is still captured only behind the analog-stick opt-in.
    ///
    /// The guard and not the token: `enable_analog_stick_mapping` is the reason a resting stick
    /// does not eat the binding, and losing it would leave a mapping screen that works on a pad
    /// with perfectly centred sticks and on no other.
    /// </summary>
    public static bool AxisIsBehindTheAnalogOptIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return AxisGuardRegex().IsMatch(text);
    }

    /// <summary>
    /// Whether a button release is still ignored. It is a bare `return` in its own case, and a
    /// rewrite that folded it in with the press would capture twice per press.
    /// </summary>
    public static bool ButtonUpCapturesNothing(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ButtonUpRegex().IsMatch(text);
    }

    [GeneratedRegex(@"NewButtonMapping\(QString\(""([^""]+)""\)")]
    private static partial Regex FormatRegex();

    [GeneratedRegex(@"case SDL_JOYAXISMOTION:\s*\r?\n\s*if\(updating_mapping_button && enable_analog_stick_mapping\)")]
    private static partial Regex AxisGuardRegex();

    [GeneratedRegex(@"case SDL_JOYBUTTONUP:\s*\r?\n\s*return;")]
    private static partial Regex ButtonUpRegex();
}
