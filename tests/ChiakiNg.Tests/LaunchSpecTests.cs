using System.Text;
using System.Text.Json;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP726: launchspec.c - the JSON this client states its stream in.
///
/// It is the payload under the BIG, which is the last of the four subsystems PP712's census owed
/// PP707's host. Nothing here sends one; what these hold is the STRING, because a launch spec whose
/// keys are equivalent and differently ordered is not the message a console has ever been sent.
///
/// THE ORACLE IS THE C'S OWN TEMPLATE. A recorded spec would agree with the session that produced
/// it, so a field arriving upstream would first be noticed by a stream that will not start. The
/// format is a concatenation of literals in one file and can be read out and compared directly.
/// </summary>
public class LaunchSpecTests(ITestOutputHelper output)
{
    private static readonly byte[] Key =
        [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
         0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff];

    private static LaunchSpecFields Fields(ChiakiTarget target, ChiakiCodec codec)
        => new(1920, 1080, 60, 15000, 1454, 12, target, codec);

    private static string? Read(string relativePath)
    {
        string? path = SanitizerSource.LocateRelative(relativePath);

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE TEMPLATE IS THE C'S, byte for byte, joined from the literals it is written as.
    ///
    /// The whole of this task in one assertion. A key renamed or a field added upstream lands here
    /// rather than in a console that refuses the stream.
    /// </summary>
    [Fact]
    public void TheTemplateIsTheOneLaunchspecDeclares()
    {
        if (Read(LaunchSpecSource.RelativePath) is not { } source)
            return;

        string? theirs = LaunchSpecSource.TemplateIn(source);

        Assert.NotNull(theirs);
        output.WriteLine($"{theirs.Length} character(s) of template");

        Assert.Equal(LaunchSpec.Template, theirs);
    }

    /// <summary>And the buffer it is formatted into is the one streamconnection.c declares.</summary>
    [Fact]
    public void TheBufferSizeIsTheOneTheSenderDeclares()
    {
        if (Read(LaunchSpecSource.BufferRelativePath) is not { } source)
            return;

        Assert.Equal(
            LaunchSpec.JsonBufferSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LaunchSpecSource.BufferSizeIn(source));
    }

    /// <summary>
    /// The base64 this side uses is the C's, which is the one field of the spec that is encoded.
    ///
    /// A differential rather than a claim. The handshake key goes into the JSON as text, so a
    /// padding difference between the two encoders would be a different spec on the wire and
    /// nothing else in the port would notice.
    /// </summary>
    [Fact]
    public void TheHandshakeKeysEncodingIsTheCs()
    {
        var theirs = new byte[LaunchSpec.Base64BufferSize];

        if (NativeBase64.Encode(Key, theirs) != NativeBase64.Success)
            return;

        string decoded = Encoding.ASCII.GetString(theirs, 0, Array.IndexOf(theirs, (byte)0));

        output.WriteLine($"C: {decoded}");

        Assert.Equal(Convert.ToBase64String(Key), decoded);
    }

    /// <summary>A PS5 gets all three extras; anything else gets three empty strings.</summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_10, false)]
    [InlineData(ChiakiTarget.Ps5Unknown, true)]
    [InlineData(ChiakiTarget.Ps5_1, true)]
    public void OnlyAPs5GetsTheThreeExtras(ChiakiTarget target, bool expected)
    {
        (string adaptive, string videoCodec, string dynamicRange) =
            LaunchSpec.Extras(target, ChiakiCodec.H265);

        bool got = adaptive.Length > 0 && videoCodec.Length > 0 && dynamicRange.Length > 0;

        Assert.Equal(expected, got);
    }

    /// <summary>
    /// The codec decides two of the three, and H265Hdr is the only one that says HDR.
    ///
    /// The two questions are not the same question - "is it h265" and "is it hdr" - and both are
    /// answered off one enum value, so a port that folded them would send SDR on the hevc path or
    /// HDR on both of them.
    /// </summary>
    [Theory]
    [InlineData(ChiakiCodec.H264, "avc", "SDR")]
    [InlineData(ChiakiCodec.H265, "hevc", "SDR")]
    [InlineData(ChiakiCodec.H265Hdr, "hevc", "HDR")]
    public void TheCodecDecidesTheOtherTwo(ChiakiCodec codec, string named, string range)
    {
        (_, string videoCodec, string dynamicRange) = LaunchSpec.Extras(ChiakiTarget.Ps5Unknown, codec);

        Assert.Equal($"\"videoCodec\":\"{named}\",", videoCodec);
        Assert.Equal($"\"dynamicRange\":\"{range}\",", dynamicRange);
    }

    /// <summary>The six numbers land where the template puts them, in the units the C hands over.</summary>
    [Fact]
    public void TheSixNumbersLandWhereTheTemplatePutsThem()
    {
        string? json = LaunchSpec.Format(Fields(ChiakiTarget.Ps5Unknown, ChiakiCodec.H265), Key);
        Assert.NotNull(json);

        Assert.Contains("\"width\":1920,", json, StringComparison.Ordinal);
        Assert.Contains("\"height\":1080}", json, StringComparison.Ordinal);
        Assert.Contains("\"maxFps\":60,", json, StringComparison.Ordinal);
        Assert.Contains("\"bwKbpsSent\":15000,", json, StringComparison.Ordinal);
        Assert.Contains("\"mtu\":1454,", json, StringComparison.Ordinal);

        // Milliseconds: the sender divides session->rtt_us by a thousand on its way in.
        Assert.Contains("\"rtt\":12,", json, StringComparison.Ordinal);

        Assert.Contains($"\"handshakeKey\":\"{Convert.ToBase64String(Key)}\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE PUNCTUATION: each extra carries its own, and they sit at two different depths.
    ///
    /// The first opens with a comma and closes requestGameSpecification; the other two end with one
    /// and sit at the top level before handshakeKey. A rewrite that gave all three the same shape
    /// produces a comma in the wrong place on one of the two paths, and the console parses.
    /// </summary>
    [Fact]
    public void EachExtraCarriesItsOwnComma()
    {
        string? json = LaunchSpec.Format(Fields(ChiakiTarget.Ps5Unknown, ChiakiCodec.H265Hdr), Key);
        Assert.NotNull(json);

        Assert.Contains(
            "\"audioEncoderProfile\":\"audio1\",\"adaptiveStreamMode\": \"resize\"},",
            json,
            StringComparison.Ordinal);

        Assert.Contains(
            "},\"videoCodec\":\"hevc\",\"dynamicRange\":\"HDR\",\"handshakeKey\":\"",
            json,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Both shapes are JSON a parser accepts - which is what the three empty strings have to leave.
    ///
    /// The PS4 path is the one where it could go wrong: two of the extras are removed from between
    /// a `},` and a `"handshakeKey"`, and only their own trailing commas going with them keeps it
    /// well formed.
    /// </summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_10)]
    [InlineData(ChiakiTarget.Ps5Unknown)]
    public void BothShapesParse(ChiakiTarget target)
    {
        string? json = LaunchSpec.Format(Fields(target, ChiakiCodec.H265Hdr), Key);
        Assert.NotNull(json);

        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement resolution = parsed.RootElement
            .GetProperty("streamResolutions")[0]
            .GetProperty("resolution");

        Assert.Equal(1920u, resolution.GetProperty("width").GetUInt32());
        Assert.Equal(1080u, resolution.GetProperty("height").GetUInt32());
        Assert.Equal(1454u, parsed.RootElement.GetProperty("network").GetProperty("mtu").GetUInt32());

        // And the extras are present exactly on the PS5 path.
        Assert.Equal(
            RpVersion.IsPs5(target),
            parsed.RootElement.TryGetProperty("dynamicRange", out _));
    }

    /// <summary>
    /// The longest spec still fits the C's buffer, with the margin written down.
    ///
    /// snprintf refuses at the buffer's size and the C treats that as a failure of the whole BIG, so
    /// how much room is left is worth a number rather than a hope: the PS5 HDR path is the longest,
    /// and every number in it can grow.
    /// </summary>
    [Fact]
    public void TheLongestSpecFitsTheBufferAndTheMarginIsKnown()
    {
        var widest = new LaunchSpecFields(
            uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue,
            ChiakiTarget.Ps5Unknown,
            ChiakiCodec.H265Hdr);

        string? ordinary = LaunchSpec.Format(Fields(ChiakiTarget.Ps5Unknown, ChiakiCodec.H265Hdr), Key);
        string? longest = LaunchSpec.Format(widest, Key);

        Assert.NotNull(ordinary);
        Assert.NotNull(longest);

        output.WriteLine(
            $"ordinary {ordinary.Length}, widest {longest.Length}, buffer {LaunchSpec.JsonBufferSize}");

        Assert.True(
            longest.Length < LaunchSpec.JsonBufferSize,
            $"a spec with every number at its widest is {longest.Length} bytes and the buffer is "
                + $"{LaunchSpec.JsonBufferSize}");
    }

    /// <summary>A spec that would not fit answers null rather than a truncated one.</summary>
    [Fact]
    public void ASpecThatWouldNotFitIsRefused()
    {
        // The key is the only field with no width limit of its own, so it is what can overrun.
        string overlong = new('A', LaunchSpec.JsonBufferSize);

        Assert.Null(LaunchSpec.Format(Fields(ChiakiTarget.Ps5Unknown, ChiakiCodec.H265), overlong));
    }

    /// <summary>Fill takes the arguments positionally and refuses a count that does not match.</summary>
    [Fact]
    public void FillRefusesTheWrongNumberOfArguments()
    {
        Assert.Equal("a1b2c", LaunchSpec.Fill("a%ub%sc", "1", "2"));
        Assert.Throws<ArgumentException>(() => LaunchSpec.Fill("a%ub%sc", "1"));
        Assert.Throws<ArgumentException>(() => LaunchSpec.Fill("a%ub%sc", "1", "2", "3"));

        // A percent that is not one of the two is text, which the template has none of.
        Assert.Equal("100%!", LaunchSpec.Fill("100%!"));
    }

    /// <summary>And the extras are still a PS5-only decision, made once in the C.</summary>
    [Fact]
    public void TheExtrasAreStillPs5Only()
    {
        if (Read(LaunchSpecSource.RelativePath) is not { } source)
            return;

        Assert.True(
            LaunchSpecSource.TheExtrasAreStillPs5Only(source),
            "the extras are no longer emptied in one assignment for everything that is not a PS5");
    }
}
