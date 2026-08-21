using System.Globalization;
using System.Text;

namespace ChiakiNg.Session;

/// <summary>
/// PP218: what `ChiakiNg.exe --controllers` prints.
///
/// The mapping screen needs a pad and PP18 says the pad cannot be stood in for. This is the
/// smallest thing that puts one on the record: what SDL sees, what it can map, and the mapping
/// string itself - which is the document the screen edits, so a session with a real DualSense
/// starts by reading it rather than by guessing at it.
///
/// The formatting is separated from the enumerating for the reason
/// <see cref="FocusChainBehavior.Decide"/> gives throughout this port: enumerating needs SDL up on
/// its own thread, and deciding what the output says needs nothing.
/// </summary>
public static class PadReport
{
    /// <summary>What is printed when SDL sees devices but can map none of them.</summary>
    public const string NoneMappable = "  (none SDL can map)";

    /// <summary>And when it sees nothing at all, which is ordinary rather than a failure.</summary>
    public const string NoDevices = "  (no devices)";

    /// <summary>
    /// The report.
    /// </summary>
    /// <param name="joysticks">
    /// What SDL_NumJoysticks answered. Reported beside the mappable count rather than derived from
    /// it: a device SDL sees and cannot map is exactly the case worth being able to tell apart
    /// from no device at all.
    /// </param>
    /// <param name="pads">The mappable ones.</param>
    /// <param name="sdlVersion">The SDL actually loaded, since the mappings ship with it.</param>
    public static string Format(int joysticks, IReadOnlyList<SdlPad> pads, Version sdlVersion)
    {
        ArgumentNullException.ThrowIfNull(pads);
        ArgumentNullException.ThrowIfNull(sdlVersion);

        var report = new StringBuilder();

        report.Append(CultureInfo.InvariantCulture, $"SDL {sdlVersion}");
        report.AppendLine();
        report.Append(CultureInfo.InvariantCulture, $"{joysticks} device(s), {pads.Count} mappable");
        report.AppendLine();

        if (joysticks == 0)
        {
            report.AppendLine(NoDevices);
            return report.ToString();
        }

        if (pads.Count == 0)
        {
            report.AppendLine(NoneMappable);
            return report.ToString();
        }

        foreach (SdlPad pad in pads)
        {
            report.AppendLine();
            report.Append(CultureInfo.InvariantCulture, $"  [{pad.Index}] {pad.Name}");
            report.AppendLine();

            // The whole string, unwrapped and unabridged. It is the input to
            // ControllerMappingDocument.Parse, so a report that elided any of it would be a report
            // nobody could act on.
            report.Append(CultureInfo.InvariantCulture, $"      {pad.Mapping}");
            report.AppendLine();
        }

        return report.ToString();
    }
}
