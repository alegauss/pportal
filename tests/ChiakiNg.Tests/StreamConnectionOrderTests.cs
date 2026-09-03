using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP640: the sequence PP295's first criterion asks a port to reproduce.
///
/// That criterion names the failure exactly - a port reproducing every function and not their order
/// "would pass a message-level comparison and fail a session" - and had no oracle. These are it.
///
/// Every one is a POSITION in streamconnection.c rather than a sentence about it, which is what
/// makes them a check on the file the port is being written from rather than on a reading of it.
/// </summary>
public class StreamConnectionOrderTests
{
    private static string? Source()
        => StreamConnectionOrder.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// PP640: all six hold in the file the port will be written from.
    ///
    /// Reported together and named individually, because a failure here means the C moved under a
    /// port in progress - and which one moved is the whole of what a reader needs.
    /// </summary>
    [Fact]
    public void AllSixOrderingsHold()
    {
        if (Source() is not { } source)
            return;

        IReadOnlyList<string> broken = StreamConnectionOrder.Broken(source);

        Assert.True(
            broken.Count == 0,
            "streamconnection.c no longer does these in the order a port has to keep: "
                + string.Join("; ", broken));

        Assert.Equal(6, StreamConnectionOrder.All.Count);
    }

    /// <summary>
    /// PP640: and every one says what a port loses by not keeping it.
    ///
    /// An ordering with no cost beside it is one somebody reorders for tidiness. The costs are the
    /// reason each is in the list, and they are the sentences a port's own tests will be written
    /// from.
    /// </summary>
    [Fact]
    public void EachOrderingSaysWhatItCosts()
    {
        Assert.All(StreamConnectionOrder.All, one =>
        {
            Assert.NotEmpty(one.Lead);
            Assert.NotEmpty(one.Costs);
        });

        // Distinct, so the list is six facts and not one repeated.
        Assert.Equal(
            StreamConnectionOrder.All.Count,
            StreamConnectionOrder.All.Select(one => one.Lead).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// PP640: and the readers see a file with the order broken, so none of them is green on a
    /// pattern that stopped matching.
    ///
    /// The three written out here are the three a port is most likely to get wrong by writing the
    /// obvious thing: waiting before draining, sending the event under the lock, and reading a
    /// measurement after the object that made it is gone.
    /// </summary>
    [Fact]
    public void TheReadersSeeTheOrderBroken()
    {
        // Drained AFTER the wait, which is the obvious way to write it and times out.
        Assert.False(StreamConnectionOrder.TheEarlyBufferIsDrainedBeforeTheWait(
            "stream_connection->state = STATE_EXPECT_STREAMINFO;\n"
                + "if(!stream_connection->state_finished)\n\terr = wait();\n"
                + "stream_connection_takion_data_expect_streaminfo(stream_connection, stream_connection->streaminfo_early_buf, n);\n"));

        // Sent while the state mutex is held.
        Assert.False(StreamConnectionOrder.ConnectedIsSentUnlocked(
            "event.type = CHIAKI_EVENT_CONNECTED;\n"
                + "chiaki_session_send_event(session, &event);\n"
                + "chiaki_mutex_unlock(&stream_connection->state_mutex);\n"));

        // The measurement taken after the sender is gone.
        Assert.False(StreamConnectionOrder.TheDelayIsTakenBeforeFini(
            "chiaki_feedback_sender_fini(&stream_connection->feedback_sender);\n"
                + "stream_connection->input_to_wire = stream_connection->feedback_sender.input_to_wire;\n"));

        // And the receivers unwound in the order they were made, which frees one too many.
        Assert.False(StreamConnectionOrder.ReceiversUnwindInReverse(
            "stream_connection->audio_receiver = chiaki_audio_receiver_new(a);\n"
                + "stream_connection->haptics_receiver = chiaki_audio_receiver_new(b);\n"
                + "stream_connection->video_receiver = chiaki_video_receiver_new(c);\n"
                + "err_audio_receiver:\nerr_haptics_receiver:\nerr_video_receiver:\n"));
    }

    /// <summary>
    /// PP640: and the file this is about is the one PP638 measured, so the two readings are of one
    /// tree.
    /// </summary>
    [Fact]
    public void ItIsTheFileTheDeletionMeasured()
        => Assert.Contains(
            StreamConnectionOrder.RelativePath, StreamConnectionConsumers.Measured);
}
