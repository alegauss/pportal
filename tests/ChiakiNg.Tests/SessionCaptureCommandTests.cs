using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP514: the flag that runs a session recording datagrams, and PP515: the length it carries.
///
/// PP514 shipped without an assertion naming it and the ratchet said so on the next run - which is
/// what the ratchet is for, and why this file exists rather than the claim staying in prose.
/// </summary>
public class SessionCaptureCommandTests
{
    /// <summary>
    /// PP514: every capture flag is declared, and the default kind leaves --capture-exchange alone.
    ///
    /// A parameter and not a second command, because ChiakiMessageTap.Install replaces: two
    /// recorders in one session is one recorder and a silence. The default is what makes the older
    /// flag's behaviour unchanged rather than merely intended to be.
    ///
    /// PP700: THREE NOW, and the third is not a recorder at all. --measure-decoder installs no tap
    /// - it attaches a decoder and counts what came out - so the "two recorders is a silence"
    /// argument does not reach it and it can share the run without replacing anything.
    /// </summary>
    [Fact]
    public void EveryCaptureIsDeclaredAndExchangeIsTheDefault()
    {
        string[] declared = [.. HostCommandLine.Flags.Select(f => f.Name)];

        Assert.Contains("--capture-exchange", declared);
        Assert.Contains("--capture-datagrams", declared);
        Assert.Contains("--measure-decoder", declared);

        Assert.Equal(SessionCaptureKind.Exchange, default(SessionCaptureKind));
        Assert.Equal(3, Enum.GetValues<SessionCaptureKind>().Length);
    }

    /// <summary>
    /// PP514: the two flags name different files, so one run cannot silently overwrite the other's.
    ///
    /// They share a session path and a --console argument; what they must not share is a default
    /// path, because a person running both in a row would otherwise keep only the second.
    /// </summary>
    [Fact]
    public void TheTwoCapturesDoNotShareADefaultName()
    {
        HostFlag exchange = HostCommandLine.Flags.Single(f => f.Name == "--capture-exchange");
        HostFlag datagrams = HostCommandLine.Flags.Single(f => f.Name == "--capture-datagrams");

        Assert.Equal("[path]", exchange.Argument);
        Assert.Equal("[path]", datagrams.Argument);
        Assert.NotEqual(exchange.Summary, datagrams.Summary);
    }

    /// <summary>
    /// PP515: a capture takes its length from beside the bytes, not from the bytes.
    ///
    /// The first real run recorded 18 for all two thousand of its datagrams, because the tap
    /// truncates to the head and a capture measuring what it was handed can only see the head. The
    /// length now arrives separately.
    /// </summary>
    [Fact]
    public void TheLengthComesFromBesideTheBytes()
    {
        var capture = new TakionTimingCapture();
        var head = new byte[TakionTimingCapture.HeadBytes];
        head[0] = (byte)TakionDispatch.Video;

        capture.Offer(head, arrivalMicroseconds: 0, datagramLength: 1300);

        CapturedDatagram taken = Assert.Single(capture.Datagrams);

        Assert.Equal(1300, taken.Length);
        Assert.Equal(TakionTimingCapture.HeadBytes, taken.Head.Length);
    }

    /// <summary>
    /// PP515: and a length shorter than what was handed over is refused rather than recorded.
    ///
    /// A datagram cannot be smaller than the bytes taken from it. Omitting the length keeps the old
    /// meaning - as long as what was handed over - which is what a caller holding a whole datagram
    /// should get.
    /// </summary>
    [Fact]
    public void ALengthShorterThanTheBytesIsNotBelieved()
    {
        var capture = new TakionTimingCapture();
        var head = new byte[TakionTimingCapture.HeadBytes];

        capture.Offer(head, 0, datagramLength: 4);
        capture.Offer(head, 1000);

        Assert.Equal(TakionTimingCapture.HeadBytes, capture.Datagrams[0].Length);
        Assert.Equal(TakionTimingCapture.HeadBytes, capture.Datagrams[1].Length);
    }
}
