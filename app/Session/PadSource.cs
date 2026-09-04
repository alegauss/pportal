using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP701: the SDL-to-PlayStation mapping, held against the client that defines it.
///
/// Which SDL button is which PlayStation button is a fact about a wire, and the only statement of
/// it in this tree is <c>Controller::HandleButtonEvent</c>. A transcription nobody compares goes
/// wrong in the one way that is hardest to see: cross and circle swapped is a working stream where
/// every confirmation cancels, and it looks like a console setting rather than a defect here.
///
/// The pairs are read out of the switch rather than listed, so a case the client adds or moves is a
/// difference this reports instead of a line somebody remembers to update.
/// </summary>
public static partial class PadSource
{
    /// <summary>Where the client maps a pad, relative to the repository root.</summary>
    public const string RelativePath = @"gui\src\controllermanager.cpp";

    /// <summary>The client's file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Every <c>SDL_CONTROLLER_BUTTON_x</c> the client turns into a <c>CHIAKI_CONTROLLER_BUTTON_y</c>,
    /// as the pair of suffixes.
    ///
    /// A case that falls to something other than an assignment - MISC1 raising the microphone
    /// button, the paddles returning false - is not a mapping and is not reported as one.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MappedIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in ButtonCase().Matches(source))
            found[match.Groups[1].Value] = match.Groups[2].Value;

        return found;
    }

    /// <summary>
    /// Every axis the client assigns, as the suffix and the field it writes.
    ///
    /// The field matters as much as the axis: the triggers write l2_state and r2_state, which are
    /// the pressures, and a port that wrote the button bits instead would send a trigger twice.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AxesIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in AxisCase().Matches(source))
            found[match.Groups[1].Value] = match.Groups[2].Value;

        return found;
    }

    /// <summary>Whether the client still scales a trigger by shifting seven, which is 0..32767 to 0..255.</summary>
    public static bool TriggersShiftBySeven(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Contains("state.l2_state = (uint8_t)(event.value >> 7)", StringComparison.Ordinal)
            && source.Contains("state.r2_state = (uint8_t)(event.value >> 7)", StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"case\s+SDL_CONTROLLER_BUTTON_(\w+):\s*\n\s*ps_btn\s*=\s*CHIAKI_CONTROLLER_BUTTON_(\w+);",
        RegexOptions.Multiline)]
    private static partial Regex ButtonCase();

    [GeneratedRegex(
        @"case\s+SDL_CONTROLLER_AXIS_(\w+):\s*\n\s*state\.(\w+)\s*=",
        RegexOptions.Multiline)]
    private static partial Regex AxisCase();
}
