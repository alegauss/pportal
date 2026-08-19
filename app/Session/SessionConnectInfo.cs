using ChiakiNg.Native;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>ChiakiCodec. The values are the wire's, and the strings are what the store holds.</summary>
public enum ChiakiCodec { H264 = 0, H265 = 1, H265Hdr = 2 }

/// <summary>Which renderer the settings screen selected, as settings/render_backend spells it.</summary>
public enum RenderBackend { Vulkan, OpenGL }

/// <summary>
/// The video profile a session is built with: a preset, plus the two overrides Settings applies
/// on top of it. Kept as the four decisions rather than as resolved pixels, because the pixels
/// and the preset bitrate are libchiaki's to produce and are read back from it.
/// </summary>
public readonly record struct VideoProfileChoice(
    ChiakiVideoResolution Resolution, ChiakiVideoFps Fps, uint BitrateOverride, ChiakiCodec Codec);

/// <summary>
/// PP5: the first piece of streamsession.cpp with no Qt in it.
///
/// StreamSessionConnectInfo's constructor is where a stored preference becomes a session
/// parameter, and it is 78 lines of QString and QMap around decisions that are not Qt's at all.
/// Transcribed here as functions of a preference reader, so that what a session starts with can be
/// asserted without a window, a console, or a Settings object.
///
/// Everything below reproduces gui/src/settings.cpp and gui/src/streamsession.cpp exactly,
/// including the parts that look like bugs. Where the two clients disagree the user sees a
/// different stream from the same store, and that is not a difference anybody would report as a
/// port defect - they would report it as "remote play got worse".
/// </summary>
public static class SessionConnectInfo
{
    /// <summary>
    /// isLocalAddress from streamsession.cpp, reproduced including what it does not catch.
    ///
    /// It is a string test and not an address test: only the RFC 1918 ranges written as literals
    /// count, so 127.0.0.1 is NOT local by this rule, nor is 169.254.x, nor is a bare hostname or
    /// an mDNS name with no dot. Every one of those takes the REMOTE profile - lower resolution
    /// and a lower bitrate - on a machine sitting next to the console.
    ///
    /// Left as it is on purpose. A port that "fixed" this would hand a user a different stream
    /// from the same settings, and the non-goal that binds here is "no redesign while porting";
    /// the fix belongs in a line of its own, argued where a user can see the before and after.
    /// </summary>
    public static bool IsLocalAddress(string? host)
    {
        if (string.IsNullOrEmpty(host))
            return false;

        if (host.Contains('.', StringComparison.Ordinal))
        {
            if (host.StartsWith("10.", StringComparison.Ordinal))
                return true;
            if (host.StartsWith("192.168.", StringComparison.Ordinal))
                return true;

            // 172.16. through 172.31., spelled out one at a time the way Qt spells it. A range
            // test on the parsed octet would also accept "172.016." and "172.16abc.", which this
            // does not, and the two differ on exactly the input a typo produces.
            for (int j = 16; j < 32; j++)
            {
                if (host.StartsWith($"172.{j}.", StringComparison.Ordinal))
                    return true;
            }
        }
        else if (host.Contains(':', StringComparison.Ordinal))
        {
            // The IPv6 unique-local block, matched case-insensitively as Qt does.
            if (host.StartsWith("FC", StringComparison.OrdinalIgnoreCase))
                return true;
            if (host.StartsWith("FD", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Which of the four profile groups a connection reads, which is the decision the whole
    /// resolution and bitrate choice hangs off.
    ///
    /// A non-empty duid means the session goes through PSN's relay, and that is remote whatever
    /// the address looks like - the address in that case is not the console's.
    /// </summary>
    public static bool IsLocalConnection(string? host, string? duid)
        => string.IsNullOrEmpty(duid) && IsLocalAddress(host);

    /// <summary>settings/render_backend, whose default is vulkan.</summary>
    public static RenderBackend GetRenderBackend(IPreferences prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return prefs.GetString("settings/render_backend") == "opengl" ? RenderBackend.OpenGL : RenderBackend.Vulkan;
    }

    /// <summary>
    /// clampCodecForBackend: an OpenGL window cannot present HDR, so h265_hdr becomes h265 there.
    ///
    /// Applied to the PS5 codecs and NOT to the PS4 one, which is settings.cpp's own asymmetry -
    /// GetCodecPS4 returns the stored value unclamped. It costs nothing today because the PS4
    /// default is h264, and it would cost a user who set h265_hdr for a PS4 on an OpenGL backend.
    /// </summary>
    public static ChiakiCodec ClampCodecForBackend(RenderBackend backend, ChiakiCodec codec)
        => backend == RenderBackend.OpenGL && codec == ChiakiCodec.H265Hdr ? ChiakiCodec.H265 : codec;

    /// <summary>
    /// The whole of StreamSessionConnectInfo's video profile decision: which group, then the
    /// preset, then the two overrides.
    /// </summary>
    public static VideoProfileChoice VideoProfile(IPreferences prefs, bool ps5, string? host, string? duid)
    {
        ArgumentNullException.ThrowIfNull(prefs);

        bool local = IsLocalConnection(host, duid);
        string suffix = (ps5, local) switch
        {
            (true, true) => "local_ps5",
            (true, false) => "remote_ps5",
            (false, true) => "local_ps4",
            (false, false) => "remote_ps4",
        };

        // The PS4 codec is one key for both connections; the PS5 codec is two. Reproduced rather
        // than regularised - a single key would read a value the Qt client never wrote.
        string codecKey = ps5 ? $"settings/codec_{(local ? "local_ps5" : "remote_ps5")}" : "settings/codec_ps4";
        ChiakiCodec codec = ParseCodec(prefs.GetString(codecKey), ps5 ? ChiakiCodec.H265 : ChiakiCodec.H264);
        if (ps5)
            codec = ClampCodecForBackend(GetRenderBackend(prefs), codec);

        return new VideoProfileChoice(
            ParseResolution(prefs.GetString($"settings/resolution_{suffix}"), DefaultResolution(ps5, local)),
            ParseFps(prefs.GetInt($"settings/fps_{suffix}")),
            prefs.GetUInt($"settings/bitrate_{suffix}"),
            codec);
    }

    /// <summary>
    /// Puts the choice into a connect info the way settings.cpp does: preset first, then the
    /// bitrate when it is not zero, then the codec always.
    ///
    /// The codec is not optional. chiaki_connect_video_profile_preset writes H264 into every
    /// preset it fills, so a caller that stopped after the preset would stream H264 on a PS5
    /// whose default is H265 - a working stream at the wrong codec, which no error reports.
    /// </summary>
    public static void Apply(ChiakiConnectInfo info, VideoProfileChoice choice)
    {
        ArgumentNullException.ThrowIfNull(info);

        info.SetVideoPreset(choice.Resolution, choice.Fps);
        if (choice.BitrateOverride != 0)
            info.Bitrate = choice.BitrateOverride;
        info.Codec = (int)choice.Codec;
    }

    /// <summary>
    /// A dpad touch shortcut, as StreamSessionConnectInfo converts it: the settings screen stores
    /// a one-based index and the session wants the bit, so n becomes 1 &lt;&lt; (n - 1) and zero
    /// stays zero. Off-by-one here is a shortcut that fires on the neighbouring button.
    /// </summary>
    public static uint DpadTouchShortcutBit(uint stored) => stored > 0 ? 1u << (int)(stored - 1) : 0u;

    /// <summary>
    /// settings/dpad_touch_increment, gated by settings/dpad_touch_enabled: the increment is zero
    /// when the feature is off, which is how the session is told it is off at all.
    /// </summary>
    public static ushort DpadTouchIncrement(IPreferences prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return prefs.GetBool("settings/dpad_touch_enabled")
            ? (ushort)prefs.GetUInt("settings/dpad_touch_increment")
            : (ushort)0;
    }

    private static ChiakiVideoResolution DefaultResolution(bool ps5, bool local)
        => ps5
            ? (local ? ChiakiVideoResolution.P1080 : ChiakiVideoResolution.P720)
            : ChiakiVideoResolution.P720;

    /// <summary>
    /// An unrecognised string reads as the default, which is QMap::key's behaviour and not a
    /// leniency taken here: a store written by a newer client is a value this one has no name for.
    /// </summary>
    private static ChiakiVideoResolution ParseResolution(string? stored, ChiakiVideoResolution fallback)
        => stored switch
        {
            "360p" => ChiakiVideoResolution.P360,
            "540p" => ChiakiVideoResolution.P540,
            "720p" => ChiakiVideoResolution.P720,
            "1080p" => ChiakiVideoResolution.P1080,
            _ => fallback,
        };

    /// <summary>30 and 60 are the only two; anything else is the 60 default.</summary>
    private static ChiakiVideoFps ParseFps(int stored)
        => stored == 30 ? ChiakiVideoFps.Fps30 : ChiakiVideoFps.Fps60;

    private static ChiakiCodec ParseCodec(string? stored, ChiakiCodec fallback)
        => stored switch
        {
            "h264" => ChiakiCodec.H264,
            "h265" => ChiakiCodec.H265,
            "h265_hdr" => ChiakiCodec.H265Hdr,
            _ => fallback,
        };
}
