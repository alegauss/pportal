using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP395, PP23's fourth module: the stream connection gets a channel, and all four now have one.
///
/// PP323 put the tap at four sites, all in ctrl.c and session.c. PP393 measured what that left:
/// streamconnection.c and senkusha.c, the two modules PP23 still owed, had no channel and so no
/// recording could hold them. PP394 answered senkusha; this answers the last one.
///
/// THE BIG IS WHY THIS FILE NEEDED MORE THAN A COPY OF PP394. Eight of its nine sends are one
/// message each and go behind a chokepoint like senkusha's. The ninth is fragmented, and PP375
/// measured that the number of fragments follows the negotiated MTU - so a recording of fragments
/// would replay only against a run that measured the same link, which is the opposite of an oracle.
/// It taps itself, whole, above the loop.
///
/// WHAT IS STILL OWED IS A CAPTURE. All four channels exist; none of the two new ones has been
/// recorded, because that needs the console.
/// </summary>
public class StreamConnectionTapTests
{
    private static string? Stream() =>
        MessageTapSource.Locate(MessageTapSource.StreamSource) is { } path
            ? File.ReadAllText(path)
            : null;

    /// <summary>THE TASK. Every ordinary send goes through the one place that taps.</summary>
    [Fact]
    public void EveryOrdinarySendGoesThroughTheChokepoint()
    {
        if (Stream() is not { } stream)
            return;

        Assert.True(
            MessageTapSource.TheStreamSendsStillGoThroughOnePlace(stream),
            "a stream connection protobuf reaches takion without passing the tap");
    }

    /// <summary>
    /// And nothing bypasses it. The BIG's four fragment sends are excluded BY SHAPE - each carries
    /// a slice - so a ninth ordinary send would still be caught.
    /// </summary>
    [Fact]
    public void NothingBypassesTheChokepoint()
    {
        if (Stream() is not { } stream)
            return;

        Assert.Equal(0, MessageTapSource.OtherStreamSendsIn(stream));
    }

    /// <summary>
    /// THE BIG IS TAPPED WHOLE, which is the decision this file forced.
    ///
    /// Above the fragment loop, and with the encoded length rather than a fragment's - so what a
    /// recording holds is the message a participant would produce, not the slicing a link imposed.
    /// </summary>
    [Fact]
    public void TheBigIsTappedWholeAndNotPerFragment()
    {
        if (Stream() is not { } stream)
            return;

        Assert.True(
            MessageTapSource.TheBigIsStillTappedWholeBeforeItIsFragmented(stream),
            "the BIG is no longer tapped whole above its fragmentation");
    }

    /// <summary>
    /// The receive tap is ABOVE the state lock, so a recorder cannot widen the window PP366 says
    /// the run thread depends on.
    /// </summary>
    [Fact]
    public void TheReceiveIsTappedAboveTheStateLock()
    {
        if (Stream() is not { } stream)
            return;

        Assert.True(
            MessageTapSource.TheStreamReceiveIsStillTappedAboveTheLock(stream),
            "the stream receive tap is now inside the lock that spans the dispatch switch");
    }

    /// <summary>The channel name is the same string on both sides of the seam.</summary>
    [Fact]
    public void TheChannelNameAgreesAcrossTheSeam()
    {
        Assert.Equal("stream", ChiakiMessageTap.StreamChannel);

        string? header = MessageTapSource.Locate(MessageTapSource.TapHeader);
        if (header is null)
            return;

        Assert.Contains(
            $"#define CHIAKI_MESSAGE_TAP_CHANNEL_STREAM \"{ChiakiMessageTap.StreamChannel}\"",
            File.ReadAllText(header),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// PP23'S FOUR MODULES ALL HAVE A CHANNEL NOW, which is what PP393 said was missing.
    ///
    /// Asserted as the four names being distinct, because two channels sharing a spelling would
    /// interleave into one conversation in a recording and no replay could separate them - which is
    /// the cost PP323's own note says the channel field is the only defence against.
    /// </summary>
    [Fact]
    public void AllFourModulesHaveADistinctChannel()
    {
        string[] channels =
        [
            ChiakiMessageTap.SessionChannel,
            ChiakiMessageTap.CtrlChannel,
            ChiakiMessageTap.SenkushaChannel,
            ChiakiMessageTap.StreamChannel,
        ];

        Assert.Equal(4, channels.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// And every one of the four files emits, which is the other half of the same claim - a channel
    /// name with no site behind it names nothing.
    ///
    /// This replaces PP394's assertion that streamconnection had none. That one was written to turn
    /// red the day this landed, and it did.
    /// </summary>
    [Fact]
    public void EveryOneOfTheFourFilesEmits()
    {
        foreach (string relative in (string[])[
            MessageTapSource.SessionSource,
            MessageTapSource.CtrlSource,
            MessageTapSource.SenkushaSource,
            MessageTapSource.StreamSource])
        {
            string? path = MessageTapSource.Locate(relative);
            if (path is null)
                return;

            Assert.Contains(
                "chiaki_message_tap_emit", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    /// <summary>The readers read what they are given (PP272).</summary>
    [Fact]
    public void TheReadersReadTheFile()
    {
        Assert.False(MessageTapSource.TheStreamSendsStillGoThroughOnePlace(""));
        Assert.False(MessageTapSource.TheBigIsStillTappedWholeBeforeItIsFragmented(""));
        Assert.False(MessageTapSource.TheStreamReceiveIsStillTappedAboveTheLock(""));

        // A BIG tapped per fragment, which is the edit that would make the recording MTU-specific.
        const string PerFragment = """
            static ChiakiErrorCode stream_connection_send_big(ChiakiStreamConnection *stream_connection)
            {
            	while(first ? (mtu < total_size + 26) : (mtu < total_size + 25))
            	{
            		chiaki_message_tap_emit(CHIAKI_MESSAGE_TAP_SENT, CHIAKI_MESSAGE_TAP_CHANNEL_STREAM, 1, buf + buf_pos, buf_size);
            		err = chiaki_takion_send_message_data(&stream_connection->takion, 0, 1, buf + buf_pos, buf_size, NULL);
            	}
            }
            """;

        Assert.False(MessageTapSource.TheBigIsStillTappedWholeBeforeItIsFragmented(PerFragment));
    }
}
