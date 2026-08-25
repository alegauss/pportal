using System.Text;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP323: the plaintext of a session reaching managed code, which is the source PP297 was written
/// as though it had.
///
/// Every message here is emitted through lib/src's own chiaki_message_tap_emit rather than handed
/// to the sink directly - the same argument chiaki_shim_log_write makes. What the four sites in
/// ctrl.c and session.c call is what these assertions call, so a tap that worked only when a test
/// drove it would not be a thing this file can produce.
///
/// Collection-serialised, because the tap is GLOBAL: lib/src holds one function pointer and three
/// of the four emit sites are static functions with no handle to thread one through. Two of these
/// running at once would install over each other, which is a failure that reads as a flaky sink.
/// </summary>
[Collection(nameof(MessageTapTests))]
[CollectionDefinition(nameof(MessageTapTests), DisableParallelization = true)]
public class MessageTapTests
{
    /// <summary>A message crosses whole: direction, channel, type and bytes.</summary>
    [Fact]
    public void AMessageCrossesWithItsDirectionChannelTypeAndBytes()
    {
        var seen = new List<TappedMessage>();
        using ChiakiMessageTap tap = ChiakiMessageTap.Install(seen.Add);

        ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, "ctrl", 0x14, [1, 2, 3, 250]);

        TappedMessage only = Assert.Single(seen);
        Assert.Equal(ExchangeTapDirection.Sent, only.Direction);
        Assert.Equal("ctrl", only.Channel);
        Assert.Equal(0x14, only.Type);
        Assert.Equal<byte[]>([1, 2, 3, 250], only.Payload);
    }

    /// <summary>
    /// The payload is COPIED, and this is the assertion that says so.
    ///
    /// The ctrl send site emits from a buffer that chiaki_rpcrypt_encrypt overwrites IN PLACE one
    /// statement later. A tap that handed the span on would give a recorder bytes that turn into
    /// ciphertext while it holds them - which looks like corruption in a recording rather than like
    /// a bug in a tap, and would be found weeks later by a replay nobody could explain.
    ///
    /// So the source buffer is overwritten here the way the encrypt overwrites that one, and the
    /// bytes the sink received must not have moved with it.
    /// </summary>
    [Fact]
    public void ThePayloadIsCopiedBeforeTheCallerCanOverwriteIt()
    {
        byte[] received = [];
        using ChiakiMessageTap tap = ChiakiMessageTap.Install(m => received = m.Payload);

        byte[] buffer = [0xDE, 0xAD, 0xBE, 0xEF];
        ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, "ctrl", 1, buffer);

        // Exactly what the encrypt does to the send site's buffer: same memory, different bytes.
        Array.Fill(buffer, (byte)0);

        Assert.Equal<byte[]>([0xDE, 0xAD, 0xBE, 0xEF], received);
    }

    /// <summary>An empty payload arrives as an empty array, not as null - a heartbeat has no body.</summary>
    [Fact]
    public void AnEmptyPayloadArrivesAsAnEmptyArray()
    {
        var seen = new List<TappedMessage>();
        using ChiakiMessageTap tap = ChiakiMessageTap.Install(seen.Add);

        ChiakiMessageTap.Emit(ExchangeTapDirection.Received, "ctrl", 0xA, []);

        Assert.NotNull(Assert.Single(seen).Payload);
        Assert.Empty(seen[0].Payload);
    }

    /// <summary>
    /// Off until installed and off again after. The sites branch on this, so a tap left wired is a
    /// cost every control message of every session keeps paying.
    /// </summary>
    [Fact]
    public void TheTapIsOffUntilInstalledAndOffAgainAfterDispose()
    {
        Assert.False(ChiakiMessageTap.Active);

        ChiakiMessageTap tap = ChiakiMessageTap.Install(_ => { });
        Assert.True(ChiakiMessageTap.Active);

        tap.Dispose();
        Assert.False(ChiakiMessageTap.Active);

        // Twice, because a session ending can race an explicit uninstall.
        tap.Dispose();
        Assert.False(ChiakiMessageTap.Active);
    }

    /// <summary>
    /// A second install REPLACES the first rather than adding to it, and the first stops receiving.
    ///
    /// lib/src holds one pointer, so this is what the seam can do. It is asserted rather than
    /// documented because the alternative a reader assumes - both sinks fire - would put two
    /// recordings into one file with no way to split them.
    /// </summary>
    [Fact]
    public void ASecondInstallReplacesTheFirstAndTheFirstStopsReceiving()
    {
        var first = new List<TappedMessage>();
        var second = new List<TappedMessage>();

        ChiakiMessageTap.Install(first.Add);
        using ChiakiMessageTap later = ChiakiMessageTap.Install(second.Add);

        ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, "ctrl", 2, [7]);

        Assert.Empty(first);
        Assert.Single(second);
    }

    /// <summary>
    /// And disposing the REPLACED tap does not uninstall the live one.
    ///
    /// The obvious implementation clears the native pointer in Dispose unconditionally, and then a
    /// caller holding the old handle in a using block silently turns the new tap off at the end of
    /// its scope. Nothing errors; the recording just stops.
    /// </summary>
    [Fact]
    public void DisposingTheReplacedTapLeavesTheLiveOneInstalled()
    {
        var second = new List<TappedMessage>();

        ChiakiMessageTap superseded = ChiakiMessageTap.Install(_ => { });
        using ChiakiMessageTap live = ChiakiMessageTap.Install(second.Add);

        superseded.Dispose();

        Assert.True(ChiakiMessageTap.Active);
        ChiakiMessageTap.Emit(ExchangeTapDirection.Received, "session", 0, [1]);
        Assert.Single(second);
    }

    /// <summary>
    /// A sink that throws loses its message and nothing else. The frame above the thunk is C, and
    /// an exception crossing it is a process abort with a stack naming neither side.
    /// </summary>
    [Fact]
    public void ASinkThatThrowsDoesNotStopTheOnesAfterIt()
    {
        var after = 0;
        using ChiakiMessageTap tap = ChiakiMessageTap.Install(m =>
        {
            if (m.Type == 1)
                throw new InvalidOperationException("the sink failed");
            after++;
        });

        ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, "ctrl", 1, [1]);
        ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, "ctrl", 2, [2]);

        Assert.Equal(1, after);
    }

    /// <summary>
    /// The session channel carries text, which is the half PP320 could only redact whole.
    ///
    /// The request is an HTTP head. Arriving as bytes rather than as a formatted hexdump row is the
    /// entire point of PP323: a redactor above this can find RP-RegistKey by name instead of
    /// deciding whether a run of hex digits is a key.
    /// </summary>
    [Fact]
    public void TheSessionChannelCarriesTheRequestAsItsOwnBytes()
    {
        var seen = new List<TappedMessage>();
        using ChiakiMessageTap tap = ChiakiMessageTap.Install(seen.Add);

        const string head = "GET /sce/rp/session HTTP/1.1\r\nRP-RegistKey: 00112233\r\n\r\n";
        ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, "session", 0, Encoding.ASCII.GetBytes(head));

        Assert.Equal(head, Encoding.ASCII.GetString(Assert.Single(seen).Payload));
        Assert.Equal(0, seen[0].Type);
    }

    /// <summary>
    /// Every site still emits in the window where the message is plaintext.
    ///
    /// The ORDER and not the presence: a tap call moved one statement is a recording of ciphertext,
    /// or of a parser's pointers, and no build or run says a word about it.
    /// </summary>
    [Fact]
    public void TheFourSitesStillEmitWhereTheMessageIsPlaintext()
    {
        string? ctrlPath = MessageTapSource.Locate(MessageTapSource.CtrlSource);
        string? sessionPath = MessageTapSource.Locate(MessageTapSource.SessionSource);
        string? tapPath = MessageTapSource.Locate(MessageTapSource.TapHeader);
        if (ctrlPath is null || sessionPath is null || tapPath is null)
            return;

        string ctrl = File.ReadAllText(ctrlPath);
        string session = File.ReadAllText(sessionPath);

        Assert.True(
            MessageTapSource.TheSendSiteStillEmitsBeforeTheEncrypt(ctrl),
            "the ctrl send tap is below the encrypt, so a recording holds ciphertext");
        Assert.True(
            MessageTapSource.TheReceiveSiteStillEmitsBetweenTheDecryptAndTheSwitch(ctrl),
            "the ctrl receive tap is outside the window where the message is plaintext and whole");
        Assert.True(
            MessageTapSource.TheSessionRequestIsStillTappedBeforeItIsSent(session),
            "the session request tap is not before the send");
        Assert.True(
            MessageTapSource.TheSessionResponseIsStillTappedBeforeTheParse(session),
            "the session response tap is below the parse, so it records the parser's own buffer");
    }

    /// <summary>And the tap is still off until somebody sets it, which is what makes it free.</summary>
    [Fact]
    public void TheTapStillStartsOff()
    {
        string? path = MessageTapSource.Locate(@"lib\src\messagetap.c");
        if (path is null)
            return;

        Assert.True(MessageTapSource.TheTapIsStillOffUntilItIsSet(File.ReadAllText(path)));
    }
}
