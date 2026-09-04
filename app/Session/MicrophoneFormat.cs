using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>The four numbers the C announces a microphone with, in the C's own parameter order.</summary>
/// <param name="Channels">One. The port announced sixteen once, which is PP422's whole story.</param>
/// <param name="Bits">Sixteen, so a sample is a signed short.</param>
/// <param name="Rate">48000 Hz.</param>
/// <param name="FrameSize">480 frames to a unit, which is ten milliseconds at that rate.</param>
public readonly record struct MicrophoneAnnouncement(int Channels, int Bits, int Rate, int FrameSize);

/// <summary>
/// PP652: what a capture device has to produce, read out of the C that announces it.
///
/// PP32 asked whether this host captures a microphone. <see cref="MicrophoneSurface"/> answered that
/// four subsystems already assume it does and nothing opens a device. This is the first thing any
/// capture path needs and the one nobody had written down: the format, which is not a choice.
///
/// THE C ANNOUNCES IT, SO THE C IS WHERE IT IS READ FROM. streamconnection.c calls
/// chiaki_audio_header_set with one channel, sixteen bits, 48000 and 480, and that call goes out to
/// the console in the STREAMINFO message. A capture producing anything else is producing something
/// the console was not told about. Parsed rather than transcribed, for PP666's reason - a table
/// copied out of a source is a claim that stops being checked the moment the source moves - and
/// because this exact call has been wrong before: PP422 found the port passing (16, 1) into a
/// (channels, bits) parameter list, announcing sixteen channels at one bit.
///
/// gui/src/streamsession.cpp passes 2 then 16 to the same function. Two call sites, opposite orders,
/// and the C comment at the site says only one of them can be right about what a microphone is. This
/// reads streamconnection.c's, because that is the one the managed path replaces.
///
/// WHAT IS DERIVED AND WHAT IS NOT. The rate, channels, bits and frame size are read. The unit's
/// byte count and its duration are arithmetic on those four, which is why they are computed here
/// rather than being four more numbers to keep true.
/// </summary>
public static partial class MicrophoneFormat
{
    /// <summary>The file that makes the announcement.</summary>
    public const string AnnouncerRelativePath = @"lib\src\streamconnection.c";

    /// <summary>The call that carries it.</summary>
    public const string AnnouncingCall = "chiaki_audio_header_set";

    /// <summary>The header's setter, whose parameter order is the thing PP422 got wrong.</summary>
    public const string SetterRelativePath = @"lib\src\audio.c";

    /// <summary>What streamconnection.c announces, as this port models it.</summary>
    public static MicrophoneAnnouncement Announced { get; } = new(Channels: 1, Bits: 16, Rate: 48000, FrameSize: 480);

    /// <summary>Either file, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// The announcement a C source makes, or null where it makes none.
    ///
    /// The first call in the file, comments stripped first - the site carries a long comment naming
    /// the wrong order and the right one, and a reader that saw those digits would find whichever
    /// numbers the comment happened to mention.
    /// </summary>
    public static MicrophoneAnnouncement? AnnouncementIn(string cSource)
    {
        ArgumentNullException.ThrowIfNull(cSource);

        Match call = SetCall().Match(CCall.Code(cSource));
        if (!call.Success)
            return null;

        return new MicrophoneAnnouncement(
            int.Parse(call.Groups["channels"].Value),
            int.Parse(call.Groups["bits"].Value),
            int.Parse(call.Groups["rate"].Value),
            int.Parse(call.Groups["frame"].Value));
    }

    /// <summary>
    /// The parameter order the setter declares, which is what makes the four numbers mean anything.
    ///
    /// PP422's defect was not a wrong number; it was the right numbers in the wrong holes. So the
    /// order is read too, and a setter that changed it fails rather than being reinterpreted.
    /// </summary>
    public static IReadOnlyList<string> ParameterOrderIn(string cSource)
    {
        ArgumentNullException.ThrowIfNull(cSource);

        Match declaration = SetDeclaration().Match(CCall.Code(cSource));
        if (!declaration.Success)
            return [];

        return [.. declaration.Groups["params"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(one => one.Split([' ', '\t', '*'], StringSplitOptions.RemoveEmptyEntries)[^1])];
    }

    /// <summary>The order those four parameters are in, which the numbers above are given in.</summary>
    public static IReadOnlyList<string> ExpectedParameterOrder { get; } =
        ["channels", "bits", "rate", "frame_size"];

    /// <summary>Bytes in one sample of one channel.</summary>
    public static int BytesPerSample(MicrophoneAnnouncement announced) => announced.Bits / 8;

    /// <summary>
    /// Bytes in one unit, which is what a capture buffer hands on.
    ///
    /// 960 for the announced format: 480 frames of one 16-bit channel.
    /// </summary>
    public static int BytesPerUnit(MicrophoneAnnouncement announced)
        => announced.FrameSize * announced.Channels * BytesPerSample(announced);

    /// <summary>
    /// How long one unit lasts, in milliseconds.
    ///
    /// Ten for the announced format, which is what makes 480 the frame size rather than a buffer
    /// choice: it is Opus's ten-millisecond frame at 48 kHz.
    /// </summary>
    public static double UnitMilliseconds(MicrophoneAnnouncement announced)
        => announced.Rate <= 0 ? 0.0 : announced.FrameSize * 1000.0 / announced.Rate;

    /// <summary>Units in one second, which is the rate a capture callback is expected at.</summary>
    public static double UnitsPerSecond(MicrophoneAnnouncement announced)
    {
        double each = UnitMilliseconds(announced);
        return each <= 0.0 ? 0.0 : 1000.0 / each;
    }

    [GeneratedRegex(
        @"chiaki_audio_header_set\s*\([^,]+,\s*(?<channels>\d+)\s*,\s*(?<bits>\d+)\s*,\s*(?<rate>\d+)\s*,\s*(?<frame>\d+)\s*\)")]
    private static partial Regex SetCall();

    [GeneratedRegex(@"void\s+chiaki_audio_header_set\s*\((?<params>[^)]*)\)")]
    private static partial Regex SetDeclaration();
}
