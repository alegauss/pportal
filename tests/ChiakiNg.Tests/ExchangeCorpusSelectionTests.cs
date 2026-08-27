using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP420: which entries of a recording belong in a corpus.
///
/// PP396's first stream capture is 677 entries, of which 566 are CORRUPTFRAME. The eight a replay
/// can expect are the handshake. Nothing said which was which.
/// </summary>
public class ExchangeCorpusSelectionTests
{
    /// <summary>
    /// THE PROPERTY WORTH HAVING A NAME FOR. A message whose count is the run's is not an oracle.
    ///
    /// PP395 refused to tap fragments because a recording of them replays only against a run that
    /// negotiated the same MTU. A heartbeat is that objection with a timer instead.
    /// </summary>
    [Theory]
    [InlineData((ushort)3)]   // HEARTBEAT
    [InlineData((ushort)4)]   // PACKETLOSS
    [InlineData((ushort)5)]   // CORRUPTFRAME
    [InlineData((ushort)16)]  // CONNECTIONQUALITY
    [InlineData((ushort)17)]  // CLIENTMETRIC
    [InlineData((ushort)25)]  // IDRREQUEST
    [InlineData((ushort)27)]  // PERIODICTIMESTAMP
    public void TheStreamsRecurringTypesAreDropped(ushort type)
    {
        Assert.Equal(
            CorpusVerdict.Recurring,
            ExchangeCorpus.Judge(ChiakiMessageTap.StreamChannel, type));
    }

    /// <summary>And the handshake, which is what a replay is held against, is kept.</summary>
    [Theory]
    [InlineData((ushort)0)]   // BIG
    [InlineData((ushort)1)]   // BANG
    [InlineData((ushort)8)]   // DISCONNECT
    [InlineData((ushort)13)]  // STREAMINFO
    [InlineData((ushort)14)]  // STREAMINFOACK
    [InlineData((ushort)21)]  // CONTROLLERCONNECTION
    [InlineData((ushort)31)]  // TAKIONPROTOCOLREQUEST
    [InlineData((ushort)32)]  // TAKIONPROTOCOLREQUESTACK
    public void TheStreamsHandshakeIsKept(ushort type)
    {
        Assert.Equal(
            CorpusVerdict.Kept, ExchangeCorpus.Judge(ChiakiMessageTap.StreamChannel, type));
    }

    /// <summary>
    /// Senkusha drops nothing: it measures a link and stops, so its whole exchange is bounded.
    /// </summary>
    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData((ushort)12)]
    [InlineData((ushort)31)]
    [InlineData((ushort)32)]
    public void SenkushaKeepsEverything(ushort type)
    {
        Assert.Equal(
            CorpusVerdict.Kept, ExchangeCorpus.Judge(ChiakiMessageTap.SenkushaChannel, type));
    }

    /// <summary>
    /// AND CTRL KEEPS ITS HEARTBEAT, which is the one that looks like an inconsistency.
    ///
    /// PP342 asserts that a ctrl heartbeat is answered unconditionally and immediately; the property
    /// is the pair, not how many arrived. Two of them in PP297's capture is that pair. A rule keyed
    /// only to "heartbeats recur" would have thrown away the thing PP391 replays.
    /// </summary>
    [Fact]
    public void TheCtrlChannelKeepsEverythingIncludingItsHeartbeat()
    {
        Assert.Equal(
            CorpusVerdict.Kept,
            ExchangeCorpus.Judge(ChiakiMessageTap.CtrlChannel, (ushort)CtrlMessage.HeartbeatReq));
        Assert.Equal(
            CorpusVerdict.Kept,
            ExchangeCorpus.Judge(ChiakiMessageTap.CtrlChannel, (ushort)CtrlMessage.HeartbeatRep));

        // And so does the session channel, whose entries are HTTP heads with no type at all.
        Assert.Equal(CorpusVerdict.Kept, ExchangeCorpus.Judge("session", 0));
    }

    /// <summary>
    /// WHAT IS DROPPED IS COUNTED. A corpus that quietly kept 8 of 677 reads as full coverage.
    /// </summary>
    [Fact]
    public void WhatIsDroppedIsCountedByType()
    {
        var recording = new ExchangeRecording();

        // A handshake, and the steady state around it.
        Add(recording, "stream", "001f ");
        Add(recording, "stream", "0000 ");
        Add(recording, "stream", "0001 ");
        for (var at = 0; at < 5; at++)
            Add(recording, "stream", "0005 ");
        for (var at = 0; at < 3; at++)
            Add(recording, "stream", "0003 ");
        Add(recording, "stream", "0008 ");
        Add(recording, "ctrl", "00fe ");

        CorpusSelection selection = ExchangeCorpus.Select(recording);

        // The handshake and the ctrl entry survive; the eight recurring ones do not.
        Assert.Equal(5, selection.Kept.Count);

        Assert.Equal(5, selection.DroppedByType["stream/0005"]);
        Assert.Equal(3, selection.DroppedByType["stream/0003"]);
        Assert.Equal(2, selection.DroppedByType.Count);
    }

    /// <summary>
    /// A selection over a recording with nothing recurring drops nothing, and says so.
    ///
    /// PP297's own capture is that case - 13 ctrl entries and 2 session ones - so this is the
    /// assertion that the rule does not quietly shrink the corpus that already exists.
    /// </summary>
    [Fact]
    public void ThePp297CorpusSurvivesTheRuleUntouched()
    {
        string? path = ChiakiNg.Session.SanitizerSource.LocateRelative(
            ExchangeCorpusTests.RelativePath);
        if (path is null)
            return;

        ExchangeRecording? recording = ExchangeRecording.Read(File.ReadAllText(path));
        if (recording is null)
            return;

        CorpusSelection selection = ExchangeCorpus.Select(recording);

        Assert.Equal(recording.Entries.Count, selection.Kept.Count);
        Assert.Empty(selection.DroppedByType);
    }

    /// <summary>An entry with no readable type is kept rather than guessed at.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("GET /sie/ps5/rp/sess/init HTTP/1.1")]
    [InlineData("zzzz not hex")]
    public void AnEntryWithNoTypeIsKept(string payload)
    {
        var entry = new ExchangeEntry(0, ExchangeDirection.Sent, "stream", payload);

        Assert.Equal(ChiakiMessageTap.UnknownType, ExchangeCorpus.TypeIn(entry));
        Assert.Equal(
            CorpusVerdict.Kept,
            ExchangeCorpus.Judge(entry.Channel, ExchangeCorpus.TypeIn(entry)));
    }

    /// <summary>The type is read off the four hex digits PP326's shape leads with.</summary>
    [Fact]
    public void TheTypeIsReadOffThePayloadsFourDigits()
    {
        Assert.Equal(
            (ushort)0x0005,
            ExchangeCorpus.TypeIn(new ExchangeEntry(0, ExchangeDirection.Sent, "stream", "0005 ")));

        Assert.Equal(
            (ushort)0x8004,
            ExchangeCorpus.TypeIn(
                new ExchangeEntry(0, ExchangeDirection.Sent, "ctrl", "8004 <redacted>")));
    }

    private static void Add(ExchangeRecording recording, string channel, string payload)
        => recording.Add(0, ExchangeDirection.Sent, channel, payload);
}
