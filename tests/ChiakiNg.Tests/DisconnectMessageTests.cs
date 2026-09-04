using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Google.Protobuf;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP687, under PP295: the disconnect read, and the bound that makes a long reason no disconnect.
///
/// PP686 made the message reachable and read nothing out of it. What is asserted here is the reason
/// coming back, and the case a tidier port would fail: the C's bounded read REFUSES a field past its
/// maximum, so a console hanging up with a long reason leaves this side never told.
/// </summary>
public class DisconnectMessageTests
{
    /// <summary>A disconnect carrying whatever reason a case wants.</summary>
    private static byte[] WithReason(string reason)
        => new Tkproto.TakionMessage
        {
            Type = Tkproto.TakionMessage.Types.PayloadType.Disconnect,
            DisconnectPayload = new Tkproto.DisconnectPayload { Reason = reason },
        }.ToByteArray();

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    private static string? Source()
        => DisconnectMessageSource.Locate(DisconnectMessageSource.RelativePath) is { } path
            ? File.ReadAllText(path)
            : null;

    /// <summary>
    /// THE ROUND TRIP the port can make on its own: its own disconnect, read back.
    ///
    /// PP684 holds those bytes against the ones a PS5 was sent, so reading them here is a reading of
    /// a real message rather than of a fixture - the same twenty characters, through the builder and
    /// out through the reader.
    /// </summary>
    [Fact]
    public void ThePortsOwnDisconnectReadsBack()
    {
        DisconnectReading reading = DisconnectMessage.Read(StreamMessages.Disconnect().Body);

        Assert.True(reading.Disconnected);
        Assert.False(reading.Undecodable);
        Assert.Equal(StreamMessages.DisconnectReason, reading.Reason);
    }

    /// <summary>And it is a disconnect, which is what the caller tests before calling the handler.</summary>
    [Fact]
    public void ThePortsOwnDisconnectIsOne()
    {
        Assert.True(DisconnectMessage.IsDisconnect(StreamMessages.Disconnect().Body));
        Assert.False(DisconnectMessage.IsDisconnect(StreamMessages.Heartbeat().Body));
        Assert.False(DisconnectMessage.IsDisconnect([0x7a, 0x7f, 0x01]));
    }

    /// <summary>
    /// THE BOUND: 255 is read and 256 is no message at all.
    ///
    /// One byte apart, and the difference is whether the client learns the session ended. A port that
    /// truncated at 255 would report a disconnect the C does not report, and would agree with the
    /// console's own client about neither.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(254)]
    [InlineData(255)]
    public void AReasonWithinTheBoundIsRead(int length)
    {
        string reason = new('x', length);
        DisconnectReading reading = DisconnectMessage.Read(WithReason(reason));

        Assert.True(reading.Disconnected);
        Assert.Equal(reason, reading.Reason);
    }

    /// <summary>And past it there is no disconnect to report.</summary>
    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(1024)]
    public void AReasonPastTheBoundIsNoDisconnectAtAll(int length)
    {
        DisconnectReading reading = DisconnectMessage.Read(WithReason(new string('x', length)));

        Assert.True(reading.Undecodable);
        Assert.False(reading.Disconnected);
        Assert.Null(reading.Reason);
    }

    /// <summary>
    /// The bound is in BYTES, not characters, which a reason with anything but ASCII in it decides.
    ///
    /// nanopb counts what is on the wire. A port measuring the string's length would accept a reason
    /// the C refuses, and the refusal is what a caller acts on.
    /// </summary>
    [Fact]
    public void TheBoundIsBytesAndNotCharacters()
    {
        // 128 characters that cost two bytes each: within the bound as characters, past it as bytes.
        string reason = new('é', 128);

        Assert.Equal(128, reason.Length);
        Assert.Equal(256, System.Text.Encoding.UTF8.GetByteCount(reason));
        Assert.True(DisconnectMessage.Read(WithReason(reason)).Undecodable);

        // And one character fewer fits.
        Assert.True(DisconnectMessage.Read(WithReason(new string('é', 127))).Disconnected);
    }

    /// <summary>
    /// An empty reason is a real answer and not an absent one: the field is required, so a console
    /// can send one of no characters and the C keeps it.
    /// </summary>
    [Fact]
    public void AnEmptyReasonIsStillAReason()
    {
        DisconnectReading reading = DisconnectMessage.Read(WithReason(string.Empty));

        Assert.True(reading.Disconnected);
        Assert.Equal(string.Empty, reading.Reason);
    }

    /// <summary>Bytes that are not a protobuf set nothing, which is the branch that returns early.</summary>
    [Fact]
    public void SomethingUndecodableSetsNothing()
    {
        DisconnectReading reading = DisconnectMessage.Read([0x7a, 0x7f, 0x01, 0x02]);

        Assert.True(reading.Undecodable);
        Assert.False(reading.Disconnected);
    }

    /// <summary>
    /// THE FINDING, held where it lives: the bounded read refuses rather than truncating.
    ///
    /// Three lines of pb_utils.h decide both of PP687's readers. A helper that clipped instead would
    /// make every caller's bound a truncation, and this port would be wrong the other way in two
    /// places at once.
    /// </summary>
    [Fact]
    public void TheBoundedReadStillRefusesRatherThanTruncating()
    {
        if (DisconnectMessageSource.Locate(DisconnectMessageSource.DecodeHelperRelativePath)
                is not { } path)
        {
            return;
        }

        Assert.True(
            DisconnectMessageSource.TheBoundedReadStillRefuses(File.ReadAllText(path)),
            "chiaki_pb_decode_buf no longer refuses a field past its maximum, so the two readers "
                + "that reproduce that refusal are now stricter than the C");
    }

    /// <summary>The reason's bound is still one less than its array, and the terminator still written.</summary>
    [Fact]
    public void TheReasonIsStillBoundedBelowItsArray()
    {
        if (Source() is not { } source
            || DisconnectMessageSource.HandlerBody(source) is not { } body)
        {
            return;
        }

        Assert.True(DisconnectMessageSource.TheReasonIsStillBoundedBelowItsArray(body));
    }

    /// <summary>And a failed decode still returns before the flag the session thread waits on.</summary>
    [Fact]
    public void AFailedDecodeStillReturnsBeforeTheFlag()
    {
        if (Source() is not { } source
            || DisconnectMessageSource.HandlerBody(source) is not { } body)
        {
            return;
        }

        Assert.True(
            DisconnectMessageSource.AFailedDecodeStillReturnsBeforeTheFlag(body),
            "the disconnect handler now sets remote_disconnected on a message it could not read");
    }

    /// <summary>PP272: the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(DisconnectMessageSource.HandlerBody(""));
        Assert.False(DisconnectMessageSource.TheBoundedReadStillRefuses(""));
        Assert.False(DisconnectMessageSource.TheReasonIsStillBoundedBelowItsArray(""));
        Assert.False(DisconnectMessageSource.AFailedDecodeStillReturnsBeforeTheFlag(""));
    }
}
