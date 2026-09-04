using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP685: the chokepoint's comment named the wrong pair.
///
/// PP395 made stream_connection_send_data the one place the stream's protobufs cross and wrote what
/// the data type means above it: "1 for most, 2 for the keyboard pair, 9 for the streaminfo ack".
/// Two of the three were right. The senders passing two are the corrupt frame and the IDR request -
/// the video receiver's messages - and that comment was the file's only mention of a keyboard.
///
/// NOTHING READ IT, SO NOTHING WAS WRONG, which is what makes this worth a check rather than a fix.
/// PP684 built the message table from the call sites and did not take the comment's word for it, so
/// the managed side was right whatever the file said. What a wrong sentence costs is the next
/// reader, and the reader it costs most is the one building the table, because a sentence like that
/// is exactly the shape of an answer.
/// </summary>
public class ChokepointCommentTests(ITestOutputHelper output)
{
    private static string? Source()
        => StreamMessagesSource.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE CHECK: the file does not call anything a keyboard.
    ///
    /// By the word rather than by the corrected phrasing, for PP573's reason - a comment that
    /// merely stopped explaining the type would satisfy a check for the new wording being there,
    /// and the defect was a claim that was false rather than a sentence that was missing.
    /// </summary>
    [Fact]
    public void TheFileNamesNoKeyboard()
    {
        if (Source() is not { } source)
            return;

        string[] lines =
        [
            .. source.ReplaceLineEndings("\n").Split('\n')
                .Where(one => one.Contains(StreamMessagesSource.WrongPairName, StringComparison.OrdinalIgnoreCase))
        ];

        Assert.True(
            lines.Length == 0,
            "streamconnection.c calls something a keyboard again:\n  " + string.Join("\n  ", lines));
    }

    /// <summary>
    /// And the two senders that DO carry the type are in the file, so the correction names something.
    ///
    /// PP271: a check on an absence passes on a file that lost the whole subject. These are what
    /// the comment now points at, read out of the source rather than trusted.
    /// </summary>
    [Fact]
    public void TheTwoSendersTheCommentNamesAreReallyThere()
    {
        if (Source() is not { } source)
            return;

        Assert.All(
            StreamMessagesSource.SendersCarryingTypeTwo,
            sender => Assert.Contains(sender, source, StringComparison.Ordinal));
    }

    /// <summary>
    /// And they really pass two, which is the claim the comment makes.
    ///
    /// Read through PP684's own reader, which takes the pairs off the call sites. If either sender
    /// changed its type the comment would be wrong again, in the other direction.
    /// </summary>
    [Fact]
    public void BothOfThemPassTheTypeTheCommentClaims()
    {
        if (Source() is not { } source)
            return;

        IReadOnlyDictionary<string, byte> types = StreamMessagesSource.DataTypesIn(source);

        foreach ((string payloadType, byte dataType) in types)
            output.WriteLine($"{payloadType}: data type {dataType}");

        byte[] twos = [.. types.Values.Where(one => one == StreamMessagesSource.VideoReceiverDataType)];

        Assert.Equal(StreamMessagesSource.SendersCarryingTypeTwo.Count, twos.Length);
    }

    /// <summary>
    /// The corrected comment still explains the type, so the fix did not delete the sentence.
    ///
    /// The easiest way to satisfy the check above is to remove the whole comment, which would cost
    /// a reader more than the wrong word did.
    /// </summary>
    [Fact]
    public void TheCommentStillExplainsWhatTheTypeIs()
    {
        if (Source() is not { } source)
            return;

        Assert.Contains("1 for most", source, StringComparison.Ordinal);
        Assert.Contains("9 for the streaminfo ack", source, StringComparison.Ordinal);
        Assert.Contains("video receiver", source, StringComparison.OrdinalIgnoreCase);
    }
}
