using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP394, PP23's third module: senkusha gets a channel, so a recording of it can exist.
///
/// PP393 measured the obstacle. PP323 put the tap at four sites, all in ctrl.c and session.c - two
/// of PP23's four untested modules, and the two PP391 and PP392 replayed. streamconnection.c and
/// senkusha.c had no emit site, so no capture could hold them and a participant written against
/// either had nothing to be judged by.
///
/// THE CHOKEPOINT IS MADE RATHER THAN FOUND, which is the difference from PP323's four. ctrl.c had
/// its window in ctrl_message_send; senkusha spread the same window over six call sites, which is
/// exactly why PP393 said the site was not obvious. senkusha_send_data is that window introduced.
///
/// THIS IS HALF THE ANSWER AND SAYS SO. The emit sites are code and are here; the capture that
/// follows needs the console, and cannot be taken from a test.
/// </summary>
public class SenkushaTapTests
{
    private static string? Senkusha() =>
        MessageTapSource.Locate(MessageTapSource.SenkushaSource) is { } path
            ? File.ReadAllText(path)
            : null;

    /// <summary>THE TASK. Every protobuf send goes through the one place that taps.</summary>
    [Fact]
    public void EverySendGoesThroughTheChokepoint()
    {
        if (Senkusha() is not { } senkusha)
            return;

        Assert.True(
            MessageTapSource.TheSenkushaSendsStillGoThroughOnePlace(senkusha),
            "a senkusha protobuf send reaches takion without passing the tap");
    }

    /// <summary>
    /// And nothing bypasses it, counted rather than inferred.
    ///
    /// A seventh send written straight onto the transport would be a message no recording holds,
    /// and the replay would diverge on an absence rather than on a disagreement.
    /// </summary>
    [Fact]
    public void NothingBypassesTheChokepoint()
    {
        if (Senkusha() is not { } senkusha)
            return;

        Assert.Equal(0, MessageTapSource.OtherTransportSendsIn(senkusha));
    }

    /// <summary>
    /// The received protobuf is tapped before the decode, which is PP323's rule for ctrl.c read
    /// across: above it the bytes are what arrived, below it the message is a struct.
    /// </summary>
    [Fact]
    public void TheReceiveIsTappedBeforeTheDecode()
    {
        if (Senkusha() is not { } senkusha)
            return;

        Assert.True(
            MessageTapSource.TheSenkushaReceiveIsStillTappedBeforeTheDecode(senkusha),
            "the senkusha receive tap is no longer above the decode");
    }

    /// <summary>
    /// The channel name is the same string on both sides of the seam.
    ///
    /// A recording written with one spelling and replayed against the other matches nothing, and
    /// the divergence names a protocol failure - which is the shape PP391 already met once.
    /// </summary>
    [Fact]
    public void TheChannelNameAgreesAcrossTheSeam()
    {
        Assert.Equal("senkusha", ChiakiMessageTap.SenkushaChannel);

        string? header = MessageTapSource.Locate(MessageTapSource.TapHeader);
        if (header is null)
            return;

        Assert.Contains(
            $"#define CHIAKI_MESSAGE_TAP_CHANNEL_SENKUSHA \"{ChiakiMessageTap.SenkushaChannel}\"",
            File.ReadAllText(header),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And it is a third channel, distinct from the two that existed - so a scoped replay can ask
    /// about senkusha without the other two answering.
    /// </summary>
    [Fact]
    public void ItIsAThirdChannelAndNotOneOfTheTwo()
    {
        Assert.NotEqual(ChiakiMessageTap.CtrlChannel, ChiakiMessageTap.SenkushaChannel);
        Assert.NotEqual(ChiakiMessageTap.SessionChannel, ChiakiMessageTap.SenkushaChannel);
    }

    /// <summary>
    /// PP394's remaining half, stated so it is not mistaken for done: streamconnection.c still has
    /// no emit site, so PP23 still owes one module.
    ///
    /// Asserted rather than commented, because a comment saying what is missing goes stale and a
    /// failing assertion cannot. This turns red the day streamconnection is tapped, which is when
    /// this sentence should be deleted.
    /// </summary>
    [Fact]
    public void StreamConnectionStillHasNoChannel()
    {
        string? path = MessageTapSource.Locate(@"lib\src\streamconnection.c");
        if (path is null)
            return;

        Assert.DoesNotContain(
            "chiaki_message_tap_emit", File.ReadAllText(path), StringComparison.Ordinal);
    }

    /// <summary>The readers read what they are given (PP272).</summary>
    [Fact]
    public void TheReadersReadTheFile()
    {
        Assert.False(MessageTapSource.TheSenkushaSendsStillGoThroughOnePlace(""));
        Assert.False(MessageTapSource.TheSenkushaReceiveIsStillTappedBeforeTheDecode(""));

        // A send straight onto the transport, which is what all six were before.
        const string AsItWas = """
            static ChiakiErrorCode senkusha_send_big(ChiakiSenkusha *senkusha)
            {
            	ChiakiErrorCode err = chiaki_takion_send_message_data(&senkusha->takion, 1, 1, buf, buf_size, NULL);
            	return err;
            }
            """;

        Assert.Equal(0, MessageTapSource.OtherTransportSendsIn(AsItWas));
        Assert.False(MessageTapSource.TheSenkushaSendsStillGoThroughOnePlace(AsItWas));
    }
}
