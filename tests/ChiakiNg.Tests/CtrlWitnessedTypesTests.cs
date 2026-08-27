using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP441, under PP294: which of the 22 ctrl message types a real console was watched exchanging.
///
/// PP440 counted which are modelled and all 22 are. This counts which are WATCHED - seven - and the
/// difference is where a rewrite can be caught being wrong versus where it can only be checked
/// against the C it replaces.
/// </summary>
public class CtrlWitnessedTypesTests(ITestOutputHelper output)
{
    private static string? Corpus()
    {
        string? path = CtrlMessageCensus.LocateCorpus();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE NUMBER. Seven of the 22 are witnessed, and the seven are named so a capture that changes
    /// this is a test somebody reads rather than a number that moved.
    /// </summary>
    [Fact]
    public void SevenOfTheTwentyTwoWereWatched()
    {
        if (Corpus() is not { } corpus)
            return;

        IReadOnlySet<ushort> witnessed = CtrlMessageCensus.Witnessed(corpus);
        output.WriteLine("witnessed: " + string.Join(", ", witnessed.Select(v => $"0x{v:x}")));

        // PP271: a reader that found no entries would call all 22 unwitnessed and be believed.
        Assert.True(
            witnessed.Count >= 5,
            $"only {witnessed.Count} types read out of the recording - the reader is not working");

        // LOGIN, DISPLAYB, SESSION_ID, MIC_TOGGLE, DISPLAY_DEVICES, HEARTBEAT_REQ, HEARTBEAT_REP,
        // and 0x41, which the enum does not name.
        foreach (ushort value in (ushort[])[0x5, 0x16, 0x33, 0x36, 0x910, 0xfe, 0x1fe])
            Assert.Contains(value, witnessed);

        IReadOnlyList<CtrlMessageRow> unwitnessed = CtrlMessageCensus.Unwitnessed(corpus);
        output.WriteLine($"{unwitnessed.Count} unwitnessed: "
            + string.Join(", ", unwitnessed.Select(r => r.CName)));

        Assert.Equal(CtrlMessageCensus.Rows.Count - 7, unwitnessed.Count);
    }

    /// <summary>
    /// The fifteen, named. Every keyboard message is among them, which is the part worth knowing: a
    /// keyboard rewrite has no recording to be wrong against.
    /// </summary>
    [Theory]
    [InlineData("KEYBOARD_OPEN")]
    [InlineData("KEYBOARD_CLOSE_REMOTE")]
    [InlineData("KEYBOARD_TEXT_CHANGE_REQ")]
    [InlineData("KEYBOARD_TEXT_CHANGE_RES")]
    [InlineData("KEYBOARD_CLOSE_REQ")]
    [InlineData("KEYBOARD_ENABLE")]
    [InlineData("KEYBOARD_ENABLE_TOGGLE")]
    [InlineData("LOGIN_PIN_REQ")]
    [InlineData("LOGIN_PIN_REP")]
    [InlineData("GOTO_BED")]
    [InlineData("GO_HOME")]
    [InlineData("ENABLE_DUALSENSE_FEATURES")]
    [InlineData("MIC_CONNECT")]
    [InlineData("DISPLAYA")]
    [InlineData("SWITCH_TO_STREAM_CONNECTION")]
    public void TheseWereNeverWatched(string cName)
    {
        if (Corpus() is not { } corpus)
            return;

        Assert.Contains(CtrlMessageCensus.Unwitnessed(corpus), row => row.CName == cName);
    }

    /// <summary>
    /// PP331's 0x41 is witnessed and named by nothing, reported as its own thing.
    ///
    /// A census of the enum cannot see a number the enum has no name for, so folding it into the
    /// witnessed set would have hidden the one type the capture actually discovered.
    /// </summary>
    [Fact]
    public void TheUnnamedTypeIsWitnessedAndReportedSeparately()
    {
        if (Corpus() is not { } corpus)
            return;

        IReadOnlyList<ushort> unnamed = CtrlMessageCensus.WitnessedAndUnnamed(corpus);

        Assert.Equal([UnhandledCtrlMessage.Observed], unnamed);
    }

    /// <summary>
    /// Only the ctrl channel is read. The same recording carries session, senkusha and stream, and
    /// four hex digits at the start of a payload mean something different on each.
    /// </summary>
    [Fact]
    public void TheOtherChannelsAreNotRead()
    {
        const string Corpus = """
            chiaki-exchange-1
            0	->	senkusha	0031 08-09
            108132	<-	ctrl	0005 00
            200000	->	stream	000d 01-02
            """;

        IReadOnlySet<ushort> witnessed = CtrlMessageCensus.Witnessed(Corpus);

        Assert.Equal([(ushort)0x5], witnessed);
    }

    /// <summary>A payload byte is not a type: the four digits are anchored to the start.</summary>
    [Fact]
    public void APayloadByteIsNotAType()
    {
        // The type is 00fe; the trailing bytes must not become types of their own.
        IReadOnlySet<ushort> witnessed = CtrlMessageCensus.Witnessed(
            "1	<-	ctrl	00fe 0033-0910-abcd\n");

        Assert.Equal([(ushort)0xfe], witnessed);
    }

    /// <summary>PP272: and an empty recording witnesses nothing, leaving all 22 unwitnessed.</summary>
    [Fact]
    public void AnEmptyRecordingWitnessesNothing()
    {
        Assert.Empty(CtrlMessageCensus.Witnessed(""));
        Assert.Empty(CtrlMessageCensus.WitnessedAndUnnamed(""));
        Assert.Equal(CtrlMessageCensus.Rows.Count, CtrlMessageCensus.Unwitnessed("").Count);
    }
}
