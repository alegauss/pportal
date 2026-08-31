using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP602, under PP27: the far end of PP601's socket has to answer, not replay.
///
/// PP601 named the way into takion's receive loop that needs no patch - connect takes the caller's
/// socket. The natural next move is to put PP297's recorded console on the other end. These say why
/// that cannot work, and they are worth an assertion rather than a note because the fact they rest
/// on is one line of C that an ordinary-looking edit would flip.
/// </summary>
public class TakionHandshakeReachTests
{
    /// <summary>
    /// The tag is drawn fresh inside connect, so a recorded ack answers a tag that no longer exists.
    /// </summary>
    [Fact]
    public void TheTagIsDrawnFreshOnEveryConnect()
    {
        if (TakionReceiveReach.LocateSource() is not { } path)
            return;

        Assert.True(
            TakionHandshakeReach.TheTagIsDrawnFresh(File.ReadAllText(path)),
            "takion no longer draws its own tag. If it now takes one from the caller, a recorded "
                + "peer CAN drive the handshake and PP602's conclusion is out of date");
    }

    /// <summary>
    /// And the caller cannot supply one, which is the half that makes the first irreversible.
    ///
    /// A tag drawn inside connect but overridable through the connect info would leave a recording
    /// usable; the struct carries neither a tag nor a sequence number.
    /// </summary>
    [Fact]
    public void TheConnectInfoCarriesNoTag()
    {
        if (TakionReceiveReach.LocateHeader() is not { } path)
            return;

        Assert.False(
            TakionHandshakeReach.TheCallerCanSupplyTheTag(File.ReadAllText(path)),
            "ChiakiTakionConnectInfo now carries a tag or sequence number, so a harness can ask for "
                + "the recorded run's values and PP602's responder may not be needed");
    }

    /// <summary>
    /// The fields it does carry, named so the absence above is a statement about a known list.
    ///
    /// PP271's shape: a search that found nothing would report "no tag" on a struct it never read.
    /// </summary>
    [Fact]
    public void TheConnectInfoIsTheListThisExpects()
    {
        if (TakionReceiveReach.LocateHeader() is not { } path)
            return;

        string header = File.ReadAllText(path);
        int at = header.IndexOf(
            "typedef struct chiaki_takion_connect_info", StringComparison.Ordinal);
        Assert.True(at >= 0, "the connect info struct is not in takion.h, so nothing was read");

        int end = header.IndexOf("ChiakiTakionConnectInfo;", at, StringComparison.Ordinal);
        string body = header[at..end];

        foreach (string field in TakionHandshakeReach.ConnectInfoFields)
            Assert.Contains(field, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the reader sees a caller-supplied tag where there is one, so the check above cannot pass
    /// on a struct it failed to parse.
    /// </summary>
    [Fact]
    public void AConnectInfoWithATagIsSeen()
    {
        const string withTag = """
            typedef struct chiaki_takion_connect_info
            {
                ChiakiLog *log;
                uint32_t tag_local;
            } ChiakiTakionConnectInfo;
            """;

        Assert.True(TakionHandshakeReach.TheCallerCanSupplyTheTag(withTag));

        const string without = """
            typedef struct chiaki_takion_connect_info
            {
                ChiakiLog *log;
                bool close_socket;
            } ChiakiTakionConnectInfo;
            """;

        Assert.False(TakionHandshakeReach.TheCallerCanSupplyTheTag(without));

        // A header with no such struct is not a header that says "no tag".
        Assert.False(TakionHandshakeReach.TheCallerCanSupplyTheTag("nothing here"));
    }

    /// <summary>And a comment quoting the assignment is not the assignment.</summary>
    [Fact]
    public void ACommentQuotingTheDrawIsNotTheDraw()
    {
        Assert.False(TakionHandshakeReach.TheTagIsDrawnFresh(
            "\t// tag_local = chiaki_random_32() is what PP602 is about\n"));

        Assert.True(TakionHandshakeReach.TheTagIsDrawnFresh(
            "\ttakion->tag_local = chiaki_random_32(); // 0x4823\n"));
    }
}
