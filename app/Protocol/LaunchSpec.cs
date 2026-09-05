using System.Globalization;
using System.Text;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// ChiakiLaunchSpec, reduced to what the format actually substitutes.
///
/// The C struct also carries the handshake key, which is passed separately here because it is the
/// one field that is not a number and the one that goes through base64 on its way in.
/// </summary>
/// <param name="Width">The video profile's width.</param>
/// <param name="Height">Its height.</param>
/// <param name="MaxFps">Its frame rate.</param>
/// <param name="BwKbpsSent">Its bitrate, in kbps, as bwKbpsSent.</param>
/// <param name="Mtu">session->mtu_in.</param>
/// <param name="Rtt">session->rtt_us / 1000, so the spec carries MILLISECONDS.</param>
/// <param name="Target">Which console, which decides whether the three extras are written.</param>
/// <param name="Codec">Which decides what two of those three say.</param>
public readonly record struct LaunchSpecFields(
    uint Width,
    uint Height,
    uint MaxFps,
    uint BwKbpsSent,
    uint Mtu,
    uint Rtt,
    ChiakiTarget Target,
    ChiakiCodec Codec);

/// <summary>
/// PP726, under PP707: launchspec.c - the JSON this client states its stream in.
///
/// It goes into the BIG encrypted and base64'd, and it is the only place the client says what it
/// wants: a resolution, a frame rate, a bitrate, an MTU and a round trip. Almost all of the rest is
/// FIXED - a session id of sessionId4321, ports 53 and 2053, an extTitleId of ps3, a model of
/// bravia_tv - and none of it is derived from anything this port knows.
///
/// SO THE DELIVERABLE IS THE STRING. A port that emitted equivalent JSON with the keys in another
/// order, or that dropped a field it could see no use for, would be sending a console something no
/// client has sent it. <see cref="Template"/> is therefore a copy of the C's format, held against
/// the C's own text by <see cref="LaunchSpecSource"/> rather than against a recorded session.
///
/// THE THREE PS5 EXTRAS CARRY THEIR OWN PUNCTUATION, and it is not the same punctuation. The first
/// opens with a comma and goes inside requestGameSpecification, after the last field there; the
/// other two END with a comma and sit before handshakeKey. A rewrite that normalised them into one
/// list would produce JSON with a comma in the wrong place on one of the two paths.
///
/// A PS4 GETS NONE OF THEM, which is three empty strings rather than a second template - and the
/// C's own TODO says it is unsure whether a PS4 should have the first. Reproduced as it stands.
/// </summary>
public static class LaunchSpec
{
    /// <summary>LAUNCH_SPEC_JSON_BUF_SIZE: what the C formats into, and refuses to overrun.</summary>
    public const int JsonBufferSize = 1024;

    /// <summary>The base64 buffer is twice that, and is what the encoded spec goes into.</summary>
    public const int Base64BufferSize = JsonBufferSize * 2;

    /// <summary>adaptiveStreamMode, inside requestGameSpecification and behind a comma.</summary>
    public const string AdaptiveStreamMode = ",\"adaptiveStreamMode\": \"resize\"";

    /// <summary>The codec, as the spec names it rather than as the enum does.</summary>
    public const string VideoCodecHevc = "\"videoCodec\":\"hevc\",";

    /// <inheritdoc cref="VideoCodecHevc"/>
    public const string VideoCodecAvc = "\"videoCodec\":\"avc\",";

    /// <summary>And the range, which follows from the same enum value.</summary>
    public const string DynamicRangeHdr = "\"dynamicRange\":\"HDR\",";

    /// <inheritdoc cref="DynamicRangeHdr"/>
    public const string DynamicRangeSdr = "\"dynamicRange\":\"SDR\",";

    /// <summary>
    /// launchspec_fmt, joined from the literals the C writes it as.
    ///
    /// Held here as one string because that is what it IS - the C splits it across lines to put a
    /// comment beside each placeholder, and the compiler joins them back. The order of the ten
    /// substitutions is the order below and is what <see cref="Fill"/> walks.
    /// </summary>
    public const string Template =
        "{"
        + "\"sessionId\":\"sessionId4321\","
        + "\"streamResolutions\":["
        + "{"
        + "\"resolution\":"
        + "{"
        + "\"width\":%u,"
        + "\"height\":%u"
        + "},"
        + "\"maxFps\":%u,"
        + "\"score\":10"
        + "}"
        + "],"
        + "\"network\":{"
        + "\"bwKbpsSent\":%u,"
        + "\"bwLoss\":0.001000,"
        + "\"mtu\":%u,"
        + "\"rtt\":%u,"
        + "\"ports\":[53,2053]"
        + "},"
        + "\"slotId\":1,"
        + "\"appSpecification\":{"
        + "\"minFps\":30,"
        + "\"minBandwidth\":0,"
        + "\"extTitleId\":\"ps3\","
        + "\"version\":1,"
        + "\"timeLimit\":1,"
        + "\"startTimeout\":100,"
        + "\"afkTimeout\":100,"
        + "\"afkTimeoutDisconnect\":100"
        + "},"
        + "\"konan\":{"
        + "\"ps3AccessToken\":\"accessToken\","
        + "\"ps3RefreshToken\":\"refreshToken\""
        + "},\"requestGameSpecification\":{"
        + "\"model\":\"bravia_tv\","
        + "\"platform\":\"android\","
        + "\"audioChannels\":\"5.1\","
        + "\"language\":\"sp\","
        + "\"acceptButton\":\"X\","
        + "\"connectedControllers\":[\"xinput\",\"ds3\",\"ds4\"],"
        + "\"yuvCoefficient\":\"bt601\","
        + "\"videoEncoderProfile\":\"hw4.1\","
        + "\"audioEncoderProfile\":\"audio1\""
        + "%s"
        + "},"
        + "\"userProfile\":{"
        + "\"onlineId\":\"psnId\","
        + "\"npId\":\"npId\","
        + "\"region\":\"US\","
        + "\"languagesUsed\":[\"en\",\"jp\"]"
        + "},"
        + "%s"
        + "%s"
        + "\"handshakeKey\":\"%s\""
        + "}";

    /// <summary>
    /// The three extras, in the order the template takes them - which is NOT the order they read in.
    /// </summary>
    /// <param name="target">A PS5 gets all three; anything else gets three empty strings.</param>
    /// <param name="codec">Decides what the second and third say.</param>
    public static (string Adaptive, string VideoCodec, string DynamicRange) Extras(
        ChiakiTarget target, ChiakiCodec codec)
        => !RpVersion.IsPs5(target)
            ? ("", "", "")
            : (AdaptiveStreamMode,
                codec is ChiakiCodec.H265 or ChiakiCodec.H265Hdr ? VideoCodecHevc : VideoCodecAvc,
                codec == ChiakiCodec.H265Hdr ? DynamicRangeHdr : DynamicRangeSdr);

    /// <summary>
    /// chiaki_launchspec_format, from the raw handshake key - which is the C's own signature.
    /// </summary>
    /// <param name="fields">The six numbers and what decides the extras.</param>
    /// <param name="handshakeKey">CHIAKI_HANDSHAKE_KEY_SIZE bytes.</param>
    /// <returns>The JSON, or null where it would not fit the C's buffer.</returns>
    public static string? Format(LaunchSpecFields fields, ReadOnlySpan<byte> handshakeKey)
        => handshakeKey.Length != GkDerivation.HandshakeKeySize
            ? throw new ArgumentException(
                $"a handshake key is {GkDerivation.HandshakeKeySize} bytes", nameof(handshakeKey))
            : Format(fields, Convert.ToBase64String(handshakeKey));

    /// <summary>
    /// The same, over a handshake key this side has already base64'd.
    /// </summary>
    /// <param name="fields">The six numbers and what decides the extras.</param>
    /// <param name="handshakeKeyBase64">The key, encoded - 24 characters for the C's sixteen bytes.</param>
    /// <returns>The JSON, or null where it would not fit the C's buffer.</returns>
    public static string? Format(LaunchSpecFields fields, string handshakeKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(handshakeKeyBase64);

        (string adaptive, string videoCodec, string dynamicRange) = Extras(fields.Target, fields.Codec);

        string json = Fill(
            Template,
            Number(fields.Width),
            Number(fields.Height),
            Number(fields.MaxFps),
            Number(fields.BwKbpsSent),
            Number(fields.Mtu),
            Number(fields.Rtt),
            adaptive,
            videoCodec,
            dynamicRange,
            handshakeKeyBase64);

        // snprintf refuses at the buffer's size INCLUDING its terminator, and the C treats that
        // refusal as a failure of the whole BIG rather than as a truncated spec.
        return json.Length >= JsonBufferSize ? null : json;
    }

    /// <summary>
    /// Substitute the template's ten placeholders, in order, with no interpretation of the rest.
    ///
    /// %u and %s are both taken as "the next argument", because the C passes them positionally and
    /// the widths are the caller's. Anything else - and this template has nothing else - is text.
    /// </summary>
    public static string Fill(string format, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(arguments);

        var built = new StringBuilder(format.Length + 64);
        var next = 0;

        for (var at = 0; at < format.Length; at++)
        {
            if (format[at] != '%' || at + 1 >= format.Length)
            {
                built.Append(format[at]);
                continue;
            }

            char kind = format[at + 1];
            if (kind is not ('u' or 's'))
            {
                built.Append(format[at]);
                continue;
            }

            if (next >= arguments.Length)
                throw new ArgumentException($"the template takes more than {arguments.Length}", nameof(arguments));

            built.Append(arguments[next++]);
            at++;
        }

        return next == arguments.Length
            ? built.ToString()
            : throw new ArgumentException($"the template takes {next} and not {arguments.Length}", nameof(arguments));
    }

    private static string Number(uint value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// PP726: the C's own template, read out of launchspec.c so the copy above cannot drift.
///
/// Stronger than a recorded spec, which is what an oracle would otherwise be here: a recording
/// agrees with the session that produced it, and a field added upstream would arrive in a stream
/// that will not start rather than in a red test.
/// </summary>
public static class LaunchSpecSource
{
    /// <summary>Where the format is.</summary>
    public const string RelativePath = @"lib\src\launchspec.c";

    /// <summary>Where the buffer size is, which is not the same file.</summary>
    public const string BufferRelativePath = @"lib\src\streamconnection.c";

    /// <summary>What the declaration opens with.</summary>
    public const string Declaration = "static const char launchspec_fmt[] =";

    /// <summary>launchspec.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? LocateBuffer() => SanitizerSource.LocateRelative(BufferRelativePath);

    /// <summary>
    /// The template, joined from the string literals of the declaration - or null where it is gone.
    ///
    /// A literal scanner rather than a line reader, because a `// 0` sits outside the quotes on
    /// nine of the lines and a naive read would take the comment as part of the JSON.
    /// </summary>
    public static string? TemplateIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int start = source.IndexOf(Declaration, StringComparison.Ordinal);
        if (start < 0)
            return null;

        int end = source.IndexOf(';', start);
        if (end < 0)
            return null;

        var built = new StringBuilder();
        var inside = false;

        for (int at = start + Declaration.Length; at < end; at++)
        {
            char one = source[at];

            if (!inside)
            {
                if (one == '"')
                    inside = true;

                continue;
            }

            if (one == '\\' && at + 1 < end)
            {
                // The only escape this template uses is a quote, and it stands for one character.
                built.Append(source[++at]);
                continue;
            }

            if (one == '"')
            {
                inside = false;
                continue;
            }

            built.Append(one);
        }

        return built.ToString();
    }

    /// <summary>
    /// The `#define NAME value` streamconnection.c gives the buffer, or null.
    /// </summary>
    public static string? BufferSizeIn(string streamSource)
    {
        ArgumentNullException.ThrowIfNull(streamSource);

        const string define = "#define LAUNCH_SPEC_JSON_BUF_SIZE ";
        int at = streamSource.IndexOf(define, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int from = at + define.Length;
        int end = streamSource.IndexOfAny(['\r', '\n'], from);

        return (end < 0 ? streamSource[from..] : streamSource[from..end]).Trim();
    }

    /// <summary>
    /// Whether the extras are still written for a PS5 alone, and emptied for anything else.
    ///
    /// The else arm assigns all three at once. A port that filled them per target would be making a
    /// decision the C makes once, and the C's TODO beside the guard says it is not sure the line is
    /// right - which is a reason to reproduce it exactly rather than to improve it.
    /// </summary>
    public static bool TheExtrasAreStillPs5Only(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int guard = source.IndexOf("chiaki_target_is_ps5(launch_spec->target)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        return source.IndexOf("extras[0] = extras[1] = extras[2] = \"\";", guard, StringComparison.Ordinal)
            > guard;
    }
}
