using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One recorded packet off a real stream, and the NALU it decrypts to.</summary>
/// <param name="Packet">The bytes as they came off the wire, header and encrypted payload.</param>
/// <param name="Nalu">What the payload has to be after decryption.</param>
public readonly record struct VideoPacketCase(byte[] Packet, byte[] Nalu);

/// <summary>
/// PP123: test/takion_av_packet_parse_real_video.inl, read rather than transcribed.
///
/// Twenty-four packets off a real PlayStation stream, each with the NALU it decrypts to, and the
/// handshake key and ECDH secret that session actually used. It is the only end-to-end oracle in
/// this tree: everything else records one function's answer, and this records what the wire
/// carried and what a decoder was supposed to see.
///
/// That makes it the case where a wrong key POSITION is caught. A wrong key fails everywhere at
/// once and is easy; a position off by one block decrypts to plausible garbage, which a decoder
/// reports as a corrupt frame - and a corrupt frame is what a lossy network produces too, so the
/// port would be indistinguishable from bad wifi.
///
/// The file is generated C, and it is parsed the same way every other vector here is: read out of
/// the source at run time, never copied. A copy would agree with itself long after either agreed
/// with a console.
/// </summary>
public static partial class VideoStreamVectors
{
    /// <summary>The generated vector file.</summary>
    public const string RelativePath = @"test\takion_av_packet_parse_real_video.inl";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The session keys the recording was made under, and the crypt index for the video stream.
    ///
    /// The index is 3 and not a detail: gkcrypt derives a different stream per index, so a port
    /// that used the audio one would decrypt every video packet to garbage with no error at all.
    /// </summary>
    public static (byte[] HandshakeKey, byte[] EcdhSecret, byte Index) Session(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Match index = IndexRegex().Match(text);
        return (
            ByteArray(text, "handshake_key"),
            ByteArray(text, "ecdh_secret"),
            index.Success ? byte.Parse(index.Groups[1].Value) : throw new InvalidDataException(
                "no crypt_index in " + RelativePath));
    }

    /// <summary>
    /// Every (packet, NALU) pair, in the order the stream carried them. Order matters: the key
    /// position advances through the session, so a case read out of sequence decrypts wrongly.
    /// </summary>
    public static IReadOnlyList<VideoPacketCase> Parse(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        string text = File.ReadAllText(filePath);
        var packets = new Dictionary<int, byte[]>();
        var nalus = new Dictionary<int, byte[]>();

        foreach (Match m in Base64Regex().Matches(text))
        {
            int n = int.Parse(m.Groups["n"].Value);
            byte[] bytes = Convert.FromBase64String(m.Groups["b64"].Value);
            (m.Groups["kind"].Value == "packet" ? packets : nalus)[n] = bytes;
        }

        // Paired by index rather than by position in the file, and only where both halves are
        // present: a packet with no NALU is a case this cannot check, and silently pairing it
        // with the next one would compare a frame against its neighbour and call it a failure.
        return [.. packets.Keys.Where(nalus.ContainsKey).Order()
            .Select(n => new VideoPacketCase(packets[n], nalus[n]))];
    }

    private static byte[] ByteArray(string text, string name)
    {
        Match m = Regex.Match(text, @"\b" + Regex.Escape(name) + @"\[\]\s*=\s*\{([^}]*)\}");
        if (!m.Success)
            throw new InvalidDataException($"no {name} in {RelativePath}");

        return [.. m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(v => (byte)Convert.ToInt32(v, v.StartsWith("0x", StringComparison.Ordinal) ? 16 : 10))];
    }

    [GeneratedRegex(@"crypt_index\s*=\s*(\d+)")]
    private static partial Regex IndexRegex();

    [GeneratedRegex(@"\b(?<kind>packet|nalu)_(?<n>\d+)\[\d*\].*?chiaki_base64_decode\(""(?<b64>[A-Za-z0-9+/=]+)""")]
    private static partial Regex Base64Regex();
}
