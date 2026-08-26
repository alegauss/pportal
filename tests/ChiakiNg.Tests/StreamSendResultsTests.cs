using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP370: no send in the stream connection has its answer discarded.
/// </summary>
public class StreamSendResultsTests
{
    /// <summary>
    /// THE CHECK, over every send that answers something.
    ///
    /// The third result-discarded finding in this family - PP367's decrypt and PP361's log word
    /// being the others - and each was one call in a group whose siblings did it right.
    /// </summary>
    [Fact]
    public void NoSendHasItsAnswerDiscarded()
    {
        string? path = StreamSendResults.Locate();
        if (path is null)
            return;

        IReadOnlyList<string> discarded =
            StreamSendResults.DiscardedResults(File.ReadAllText(path));

        Assert.True(
            discarded.Count == 0,
            "these sends throw away what they answered:\n  " + string.Join("\n  ", discarded));
    }

    /// <summary>Every send is on the list, so a fourth added is a fourth covered.</summary>
    [Fact]
    public void EverySendIsOnTheList()
    {
        string? path = StreamSendResults.Locate();
        if (path is null)
            return;

        string source = File.ReadAllText(path);

        // Each named send must actually exist in the file - a stale name would silently check
        // nothing.
        Assert.All(
            StreamSendResults.SendsThatAnswer,
            send => Assert.Contains(send, source, StringComparison.Ordinal));

        Assert.Equal(8, StreamSendResults.SendsThatAnswer.Count);
    }

    /// <summary>And the reader finds the discard, so the check means something.</summary>
    [Fact]
    public void TheReaderFindsADiscardedSend()
    {
        const string asItWas = """
            	stream_connection_send_streaminfo_ack(stream_connection);

            	ChiakiErrorCode err = stream_connection_send_controller_connection(stream_connection);
            """;

        string found = Assert.Single(StreamSendResults.DiscardedResults(asItWas));

        Assert.Contains("send_streaminfo_ack", found, StringComparison.Ordinal);
    }

    /// <summary>And ignores calls whose result goes somewhere, and the definitions.</summary>
    [Theory]
    [InlineData("\terr = stream_connection_send_big(stream_connection);")]
    [InlineData("\tChiakiErrorCode e = stream_connection_send_heartbeat(sc);")]
    [InlineData("\tif(stream_connection_send_disconnect(sc) != CHIAKI_ERR_SUCCESS)")]
    [InlineData("\treturn stream_connection_send_idr_request(sc);")]
    [InlineData("static ChiakiErrorCode stream_connection_send_big(ChiakiStreamConnection *sc)")]
    public void TheReaderIgnoresAnAnswerThatGoesSomewhere(string line)
    {
        Assert.Empty(StreamSendResults.DiscardedResults(line));
    }

    /// <summary>
    /// STREAMINFO MAKES THE CLIENT SEND THREE THINGS, in order.
    ///
    /// One arrival, three departures - the same shape as PP342's session-id burst in ctrl.c, and
    /// the same reason a table of message-in/message-out pairs would miss it.
    /// </summary>
    [Fact]
    public void StreaminfoTriggersThreeSendsInOrder()
    {
        Assert.Equal(
            ["stream_connection_send_streaminfo_ack",
             "stream_connection_send_controller_connection",
             "stream_connection_enable_microphone"],
            StreamSendResults.StreaminfoBurst);

        string? path = StreamSendResults.Locate();
        if (path is null)
            return;

        string? handler = CFunction.BodyIn(path, "stream_connection_takion_data_expect_streaminfo");
        Assert.NotNull(handler);

        Assert.True(
            StreamSendResults.TheBurstIsStillInOrder(handler),
            "the three sends streaminfo triggers no longer go out in that order");
    }

    /// <summary>
    /// PP375: THE SAME RULE ONE LEVEL DOWN. No transport send in this file discards its answer either.
    ///
    /// PP370's check reads the wrappers; this one reads the calls inside them. BIG is the only message
    /// that makes more than one, because it is fragmented - and the loop assigned a result per
    /// fragment and read none of them.
    /// </summary>
    [Fact]
    public void NoTransportSendHasItsAnswerDiscarded()
    {
        string? path = StreamSendResults.Locate();
        if (path is null)
            return;

        IReadOnlyList<string> discarded =
            StreamSendResults.TakionSendsWhoseResultGoesNowhere(File.ReadAllText(path));

        Assert.True(
            discarded.Count == 0,
            "these transport sends throw away what they answered:\n  " + string.Join("\n  ", discarded));
    }

    /// <summary>
    /// And every fragment of a BIG has its result read - the loop leaving on a failure, the trailing
    /// send logging one.
    ///
    /// The loop must LEAVE, not just log. A partial BIG cannot be completed by sending the rest of it,
    /// so carrying on after a failed fragment sends bytes the console will never assemble. The
    /// trailing send has nothing left to abandon, so logging is the whole of what it owes.
    /// </summary>
    [Fact]
    public void EveryBigFragmentHasItsResultRead()
    {
        string? path = StreamSendResults.Locate();
        if (path is null)
            return;

        string? body = CFunction.BodyIn(path, "static ChiakiErrorCode stream_connection_send_big(");
        Assert.NotNull(body);

        Assert.True(
            StreamSendResults.EveryBigFragmentResultIsRead(body),
            "a BIG fragment's result goes unread, so a truncated BIG reports success");
    }

    /// <summary>And the readers find the loop as it was, so both checks mean something.</summary>
    [Fact]
    public void TheReaderFindsTheUncheckedFragmentLoop()
    {
        const string asItWas = """
            	while(first ? (mtu < total_size + 26) : (mtu < total_size + 25))
            	{
            		if(first)
            		{
            			buf_size = mtu - 26;
            			err = chiaki_takion_send_message_data(&stream_connection->takion, 0, 1, buf + buf_pos, buf_size, NULL);
            			first = false;
            		}
            		buf_pos += buf_size;
            		total_size -= buf_size;
            	}
            	if(total_size > 0)
            	{
            		err = chiaki_takion_send_message_data(&stream_connection->takion, 1, 1, buf + buf_pos, total_size, NULL);
            	}
            	return err;
            """;

        Assert.False(StreamSendResults.EveryBigFragmentResultIsRead(asItWas));

        // And a discarded transport send is found, while an assigned one is not.
        Assert.Single(StreamSendResults.TakionSendsWhoseResultGoesNowhere(
            "\tchiaki_takion_send_message_data(&sc->takion, 1, 1, buf, buf_size, NULL);"));
        Assert.Empty(StreamSendResults.TakionSendsWhoseResultGoesNowhere(asItWas));
    }
}
