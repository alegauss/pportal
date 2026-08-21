using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP247: the checks a derived candidate skips.
///
/// <see cref="ADiscoveredAddressIsTrustedFurtherThanANamedOne"/> carries the task, and
/// <see cref="TheTwoAbortsDifferInWhereTheErrorSits"/> is the one that only shows up by putting the
/// two side by side.
/// </summary>
public class ResponseCheckTests
{
    private static byte[] Five(byte fill) => [fill, fill, fill, fill, fill];

    /// <summary>The verdicts, in the order the core reaches them.</summary>
    [Fact]
    public void TheVerdictsAreReachedInThatOrder()
    {
        // Size first, before anything is read out of the packet.
        Assert.Equal(
            ResponseVerdict.WrongSize,
            ResponseCheck.Verdict(87, PunchResponse.ResponseType, Five(1), Five(1)));

        Assert.Equal(
            ResponseVerdict.ConsoleProbing,
            ResponseCheck.Verdict(88, PunchProbe.RequestType, Five(1), Five(2)));

        Assert.Equal(
            ResponseVerdict.WrongType,
            ResponseCheck.Verdict(88, 0x09000000, Five(1), Five(1)));

        Assert.Equal(
            ResponseVerdict.WrongRequestId,
            ResponseCheck.Verdict(88, PunchResponse.ResponseType, Five(1), Five(2)));

        Assert.Equal(
            ResponseVerdict.Accepted,
            ResponseCheck.Verdict(88, PunchResponse.ResponseType, Five(7), Five(7)));
    }

    /// <summary>
    /// THE EXEMPTION. Every abort has the same escape, so an address discovered from traffic keeps
    /// the punch alive where a named one would end it.
    /// </summary>
    [Fact]
    public void ADiscoveredAddressIsTrustedFurtherThanANamedOne()
    {
        foreach (ResponseVerdict verdict in new[]
        {
            ResponseVerdict.WrongSize,
            ResponseVerdict.WrongType,
        })
        {
            Assert.Equal(VerdictAction.Abort, ResponseCheck.Action(verdict, CandidateType.Static));
            Assert.NotEqual(VerdictAction.Abort, ResponseCheck.Action(verdict, CandidateType.Derived));
        }

        // And the third: a reply that failed to send.
        Assert.Equal(
            VerdictAction.Abort,
            ResponseCheck.Action(ResponseVerdict.ConsoleProbing, CandidateType.Local, replySucceeded: false));

        Assert.Equal(
            VerdictAction.Reply,
            ResponseCheck.Action(ResponseVerdict.ConsoleProbing, CandidateType.Derived, replySucceeded: false));
    }

    /// <summary>
    /// The two aborts look alike and are not: one sets its error code before the escape and one
    /// after, so a discovered candidate leaves a stale code behind on exactly one of them.
    /// </summary>
    [Fact]
    public void TheTwoAbortsDifferInWhereTheErrorSits()
    {
        Assert.False(ResponseCheck.RecordsAnError(ResponseVerdict.WrongSize, CandidateType.Derived));
        Assert.True(ResponseCheck.RecordsAnError(ResponseVerdict.WrongType, CandidateType.Derived));

        // For a named candidate both abort, so both record.
        Assert.True(ResponseCheck.RecordsAnError(ResponseVerdict.WrongSize, CandidateType.Static));
        Assert.True(ResponseCheck.RecordsAnError(ResponseVerdict.WrongType, CandidateType.Static));
    }

    /// <summary>
    /// The loudest branch is the only one that leaves nothing behind - six lines printed, no error
    /// recorded, for any candidate at all.
    /// </summary>
    [Fact]
    public void TheWrongRequestIdIsTheQuietestLoudBranch()
    {
        Assert.Equal(6, ResponseCheck.LinesTheSilentDropPrints);

        foreach (CandidateType type in Enum.GetValues<CandidateType>())
        {
            Assert.Equal(
                VerdictAction.DropQuietly, ResponseCheck.Action(ResponseVerdict.WrongRequestId, type));
            Assert.False(ResponseCheck.RecordsAnError(ResponseVerdict.WrongRequestId, type));
        }
    }

    /// <summary>
    /// The field the comment calls weird is the one PP243 wrote and PP236 reads back - the only
    /// thing in the packet proving the answer is ours.
    /// </summary>
    [Fact]
    public void TheWeirdDataIsTheProbesOwnSignature()
    {
        Assert.Equal(PunchProbe.RequestIdAt, ResponseCheck.EchoAt);
        Assert.Equal(PunchProbe.RequestIdLength, ResponseCheck.EchoLength);
        Assert.Contains("0x4b", ResponseCheck.WhatTheCommentCallsIt, StringComparison.Ordinal);

        // A probe and the reply built from it agree there; anything else is somebody else's answer.
        byte[] probe = PunchProbe.Build(
            Five(0x33), new byte[20], new byte[20], 0, 0);

        byte[]? reply = PunchResponse.Build(probe, new byte[20], new byte[20], 0, 0, "10.0.0.1", 9295);
        Assert.NotNull(reply);

        Assert.Equal(
            ResponseVerdict.Accepted,
            ResponseCheck.Verdict(
                88,
                PunchResponse.ResponseType,
                reply.AsSpan(ResponseCheck.EchoAt, ResponseCheck.EchoLength),
                Five(0x33)));
    }

    /// <summary>
    /// The retransmit branch is unreachable at a probe count of one, and correct at any larger one -
    /// which is why it is kept rather than called dead.
    /// </summary>
    [Fact]
    public void TheRetransmitIsUnreachableAtOneAndCorrectAboveIt()
    {
        Assert.False(ResponseCheck.RetransmitIsReachable(PunchProbe.RequestCount));
        Assert.True(ResponseCheck.RetransmitIsReachable(2));

        // Because the first counted response already selects.
        Assert.True(ResponseCheck.Selects(1));
        Assert.False(ResponseCheck.Selects(0));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheCheckingIsStillTheCores()
    {
        string? file = ResponseCheckSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            ResponseCheckSource.AllThreeAbortsStillExemptADerivedCandidate(core),
            "all three aborts still exempt a derived candidate");
        Assert.True(
            ResponseCheckSource.TheErrorSitsOnOppositeSidesOfTheEscape(core),
            "and the error code still sits on opposite sides of the two escapes");
        Assert.True(
            ResponseCheckSource.TheWrongIdBranchStillRecordsNothing(core),
            "the wrong-id branch still prints six lines and records nothing");
        Assert.True(
            ResponseCheckSource.TheCommentStillAsksForWhatFollows(core),
            "the comment still asks for what the next line does");
        Assert.True(
            ResponseCheckSource.SelectionIsStillThatTest(core), "selection is still that test");
        Assert.True(
            ResponseCheckSource.TheRetransmitStillIndexesInBounds(core),
            "and the retransmit still indexes with the count");
    }
}
