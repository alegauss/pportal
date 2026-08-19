using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP93: the one place this port answers "how big is the touchpad".
///
/// The Qt client answers it three times. streamsession.cpp picks per console - 1920x942 for a PS4,
/// 1919x1079 for a PS5, which are a DualShock 4 and a DualSense - and controllermanager.h defines
/// PS_TOUCHPAD_MAXX 1920 and PS_TOUCHPAD_MAXY 1079, which is each axis's LARGER value and therefore
/// neither pad. The dpad-touch path and the SDL touchpad path both take that third pair whichever
/// console is connected, because the SDL layer has no session to ask.
///
/// The port has the same structural gap - SdlThread and TouchpadTracker know nothing about a
/// target either - so the fix is not to find a session there. It is that the answer lives in ONE
/// type, every path is handed a value of that type, and the third pair exists here as a named,
/// asserted fact about the Qt client rather than as a number something scales by. PP93's landed
/// half already moved the dpad path onto the per-console pair; this is what stops the SDL path
/// being a second place where 1079 could be typed.
///
/// The error in the third pair is always OUTWARD, which is why nobody has noticed it: on a PS4,
/// dpad-down walks to y=1079 on a pad ending at 942, and on a PS5 dpad-right reaches x=1920 on a
/// pad ending at 1919. The gesture keeps working and stops near the edge instead of at it. Whether
/// the console clamps what it is sent is unmeasured and needs a PS4 as well as a PS5, so that half
/// of PP93 stays open - but "one client, one answer" does not depend on it.
/// </summary>
public readonly record struct TouchpadExtents(ushort MaxX, ushort MaxY)
{
    /// <summary>A DualShock 4's pad, which is the shorter of the two.</summary>
    public static TouchpadExtents Ps4 => new(1920, 942);

    /// <summary>A DualSense's pad, one narrower and a third taller.</summary>
    public static TouchpadExtents Ps5 => new(1919, 1079);

    /// <summary>
    /// controllermanager.h's PS_TOUCHPAD_MAXX/MAXY: each axis's larger value, so it is a pad that
    /// does not exist. Here to be named and asserted about, and deliberately not returned by
    /// <see cref="For(ChiakiTarget)"/> - nothing in this port scales by it.
    /// </summary>
    public static TouchpadExtents QtMacros => new(1920, 1079);

    /// <summary>
    /// chiaki_target_is_ps5, which is `target >= CHIAKI_TARGET_PS5_UNKNOWN` and lives as a static
    /// inline in common.h - so it exports no symbol and the port has to hold the comparison rather
    /// than call it. A threshold and not a set: a PS5 target this build has never heard of is still
    /// a PS5, which is what the inline says and what a switch over known values would get wrong.
    /// </summary>
    public static bool IsPs5(ChiakiTarget target) => target >= ChiakiTarget.Ps5Unknown;

    /// <summary>The connected console's own pad.</summary>
    public static TouchpadExtents For(ChiakiTarget target) => For(IsPs5(target));

    /// <summary>The same, where the caller already reduced the target to the one bit that matters.</summary>
    public static TouchpadExtents For(bool ps5) => ps5 ? Ps5 : Ps4;

    /// <summary>
    /// SDL reports a touch as a fraction of the pad; the console wants its own units. Truncating,
    /// not rounding, because that is what the C++ multiplication into a uint16 does.
    /// </summary>
    public (ushort X, ushort Y) Scale(float normX, float normY)
        => ((ushort)(normX * MaxX), (ushort)(normY * MaxY));

    /// <summary>
    /// Whether this pair lies outside the given one on at least one axis, and inside it on none.
    ///
    /// The property the section claims about the Qt macros and the reason the bug is invisible: an
    /// outward error overshoots, so the gesture still works. An INWARD one would stop the finger
    /// short of an edge the pad really has, which a user would report.
    /// </summary>
    public bool IsOutwardOf(TouchpadExtents pad)
        => (MaxX > pad.MaxX || MaxY > pad.MaxY) && MaxX >= pad.MaxX && MaxY >= pad.MaxY;
}

/// <summary>
/// PP93: the three answers where the Qt client states them, so the finding cannot go stale.
/// </summary>
public static partial class TouchpadExtentsSource
{
    /// <summary>Where the per-console pair is chosen.</summary>
    public const string StreamSessionRelativePath = @"gui\src\streamsession.cpp";

    /// <summary>Where the third pair is defined.</summary>
    public const string ControllerHeaderRelativePath = @"gui\include\controllermanager.h";

    /// <summary>Where the SDL touchpad path scales by it.</summary>
    public const string ControllerCppRelativePath = @"gui\src\controllermanager.cpp";

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>The PS_TOUCHPAD_MAXX/MAXY the header defines, or null where it defines neither.</summary>
    public static TouchpadExtents? MacroPair(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        Match x = MacroXRegex().Match(header);
        Match y = MacroYRegex().Match(header);
        if (!x.Success || !y.Success)
            return null;

        return new TouchpadExtents(ushort.Parse(x.Groups[1].Value), ushort.Parse(y.Groups[1].Value));
    }

    /// <summary>
    /// The two per-console pairs streamsession.cpp defines, or null where it stopped defining all
    /// four. They are floats there - 942.0f - so the fractional part is read and discarded rather
    /// than assumed absent.
    /// </summary>
    public static (TouchpadExtents Ps4, TouchpadExtents Ps5)? PerConsolePairs(string streamSession)
    {
        ArgumentNullException.ThrowIfNull(streamSession);

        ushort? ps4X = Define(streamSession, "PS4_TOUCHPAD_MAX_X");
        ushort? ps4Y = Define(streamSession, "PS4_TOUCHPAD_MAX_Y");
        ushort? ps5X = Define(streamSession, "PS5_TOUCHPAD_MAX_X");
        ushort? ps5Y = Define(streamSession, "PS5_TOUCHPAD_MAX_Y");

        if (ps4X is null || ps4Y is null || ps5X is null || ps5Y is null)
            return null;

        return (new TouchpadExtents(ps4X.Value, ps4Y.Value), new TouchpadExtents(ps5X.Value, ps5Y.Value));
    }

    private static ushort? Define(string text, string name)
    {
        Match m = Regex.Match(
            text, @"#define\s+" + Regex.Escape(name) + @"\s+(\d+)(?:\.\d+)?f?",
            RegexOptions.None, TimeSpan.FromSeconds(1));

        return m.Success ? ushort.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// Whether the SDL touchpad path still scales by the macros rather than by a per-console pair.
    ///
    /// This is the half PP93 left open, and it is asserted as STILL TRUE rather than fixed: the
    /// port diverges here, and a divergence nobody re-reads is indistinguishable from a mistake.
    /// </summary>
    public static bool SdlPathStillUsesTheMacros(string controllerCpp)
    {
        ArgumentNullException.ThrowIfNull(controllerCpp);
        return SdlScaleRegex().Matches(controllerCpp).Count >= 2;
    }

    [GeneratedRegex(@"#define\s+PS_TOUCHPAD_MAXX\s+(\d+)")]
    private static partial Regex MacroXRegex();

    [GeneratedRegex(@"#define\s+PS_TOUCHPAD_MAXY\s+(\d+)")]
    private static partial Regex MacroYRegex();

    [GeneratedRegex(@"event\.x \* PS_TOUCHPAD_MAXX, event\.y \* PS_TOUCHPAD_MAXY")]
    private static partial Regex SdlScaleRegex();
}
