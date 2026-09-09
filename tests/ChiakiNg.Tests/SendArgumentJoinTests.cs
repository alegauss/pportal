using System.IO;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP831: the join nothing made, between a managed send's arguments and the C call's.
///
/// PP778 found every stream message going out with the data type in the chunk flags; PP813 found
/// senkusha doing the same six tasks later, green the whole time. Both were found by a live run at
/// the cost of a whole task, because the console cannot say so either - takion acks by sequence
/// number and reads no channel, so a command on the wrong one is acknowledged and then ignored.
/// </summary>
public class SendArgumentJoinTests(ITestOutputHelper output)
{
    private static string? Read(string relative)
        => SendArgumentJoin.Locate(relative) is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// Both chokepoints spend the C's arguments where the C spends them.
    ///
    /// The comparison is of KIND: a literal against a literal of the same value, and a varying
    /// expression against a varying one. That is precisely what an inversion changes and what a
    /// builder test cannot see - SenkushaMessages and StreamMessages both carry the right number
    /// either way, and only the argument it is spent in differs.
    /// </summary>
    [Fact]
    public void EveryPortedSendSpendsItsArgumentsWhereTheCDoes()
    {
        string? header = Read(SendArgumentJoin.TakionHeader);
        if (header is null)
            return;

        var wrong = new List<string>();

        foreach (PortedSend send in SendArgumentJoin.Chokepoints)
        {
            string? c = Read(send.CFile);
            string? managed = Read(send.ManagedFile);

            if (c is null || managed is null)
                continue;

            IReadOnlyList<string> said = SendArgumentJoin.DisagreementsIn(send, c, header, managed);

            foreach (string one in said)
                output.WriteLine(one);

            wrong.AddRange(said);
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// And the check FAILS on the inversion that shipped, which is what makes the one above worth
    /// anything.
    ///
    /// PP813's call, verbatim in shape: the varying channel spent as the flags and the constant
    /// spent as the channel. Every builder test passed on it for six tasks. This asserts the reader
    /// reports both halves rather than one, because a check that noticed only the constant would
    /// still pass the mirror-image mistake.
    /// </summary>
    [Fact]
    public void TheInversionThatShippedIsRefused()
    {
        string? header = Read(SendArgumentJoin.TakionHeader);
        string? c = Read(@"lib\src\senkusha.c");
        if (header is null || c is null)
            return;

        PortedSend real = SendArgumentJoin.Chokepoints[0];

        // The same port with the two arguments swapped, and a stand-in file holding it.
        var inverted = real with
        {
            ManagedCallText =
                "takion.SendData(SenkushaMessages.ChunkFlags, message.Body, message.Channel)",
        };

        IReadOnlyList<string> said = SendArgumentJoin.DisagreementsIn(
            inverted, c, header, inverted.ManagedCallText);

        foreach (string one in said)
            output.WriteLine(one);

        // Both parameters disagree: the C's constant flags met a varying value, and its varying
        // channel met a constant.
        Assert.Equal(2, said.Count);
        Assert.Contains(said, one => one.Contains("chunk_flags", StringComparison.Ordinal));
        Assert.Contains(said, one => one.Contains("channel", StringComparison.Ordinal));
    }

    /// <summary>
    /// The C's parameter names come off its header, so a rename upstream is a red check.
    ///
    /// Written down, the correspondence would keep matching a key the header no longer has - which
    /// is the silent half of the same failure this file is about.
    /// </summary>
    [Fact]
    public void TheParameterNamesAreReadFromTheHeaderAndNotWrittenDown()
    {
        string? header = Read(SendArgumentJoin.TakionHeader);
        if (header is null)
            return;

        IReadOnlyList<string> names =
            SendArgumentJoin.CParametersOf(header, "chiaki_takion_send_message_data");

        output.WriteLine(string.Join(", ", names));

        Assert.Equal(["takion", "chunk_flags", "channel", "buf", "buf_size", "seq_num"], names);

        // And the flags come BEFORE the channel, which is the ordering the port reverses on purpose
        // and the reason an inversion is invisible at the call.
        Assert.True(
            names.ToList().IndexOf("chunk_flags") < names.ToList().IndexOf("channel"),
            "the C takes the channel first now, so the port's reordering is no longer a reordering");
    }

    /// <summary>
    /// A named constant resolves and an expression does not, which is the whole distinction.
    /// </summary>
    [Fact]
    public void AConstantResolvesAndAMessageFieldDoesNot()
    {
        Assert.Equal(1, SendArgumentJoin.ConstantOf("SenkushaMessages.ChunkFlags", "irrelevant"));
        Assert.Equal(1, SendArgumentJoin.ConstantOf("StreamChunkFlags", nameof(TakionMessageSink)));
        Assert.Equal(8, SendArgumentJoin.ConstantOf("SenkushaMessages.CommandChannel", "irrelevant"));
        Assert.Equal(1, SendArgumentJoin.ConstantOf("1", "irrelevant"));

        // What varies, which is everything the check wants to see in the channel's position.
        Assert.Null(SendArgumentJoin.ConstantOf("message.Channel", nameof(ManagedSenkushaRunHost)));
        Assert.Null(SendArgumentJoin.ConstantOf("message.DataType", nameof(TakionMessageSink)));
        Assert.Null(SendArgumentJoin.ConstantOf("data_type", "irrelevant"));
    }
}
