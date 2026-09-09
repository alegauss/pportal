using System.IO;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP832: the keyframe requests a run makes, counted and printed.
///
/// A user reported artefacts at the bottom of the screen and this tree could not answer the first
/// question about them: corruption that persists across frames is a reference frame lost and never
/// replaced, the remedy is an IDR request, and whether one went out was in no line of any run. The
/// send existed - ManagedVideoReceiver has called it since PP684 - so what was missing was the
/// reading, which is what this holds.
/// </summary>
public class KeyframeRequestCountTests(ITestOutputHelper output)
{
    /// <summary>A sink that answers whatever it is told to, and keeps what it was handed.</summary>
    private sealed class Sink(bool sends) : IStreamMessageSink
    {
        public List<ushort> Types { get; } = [];

        public bool Send(in StreamMessage message)
        {
            Types.Add(message.PayloadType);
            return sends;
        }

        public bool SendBig(in StreamMessage message) => Send(in message);
    }

    /// <summary>
    /// A request that went out is counted, and so is the report beside it.
    /// </summary>
    [Fact]
    public void WhatTheSessionAskedForIsCounted()
    {
        var sink = new Sink(sends: true);
        var outbound = new StreamOutbound(sink);

        outbound.SendCorruptFrame(10, 20);
        Assert.True(outbound.SendIdrRequest());
        Assert.True(outbound.SendIdrRequest());

        output.WriteLine(
            $"{outbound.IdrRequests} requested, {outbound.IdrRequestsRefused} refused, "
                + $"{outbound.CorruptFrameReports} report(s)");

        Assert.Equal(2, outbound.IdrRequests);
        Assert.Equal(0, outbound.IdrRequestsRefused);
        Assert.Equal(1, outbound.CorruptFrameReports);

        // And the messages really went, which is what makes the counts about the wire.
        Assert.Equal(
            [StreamMessages.CorruptFrameType, StreamMessages.IdrRequestType, StreamMessages.IdrRequestType],
            sink.Types);
    }

    /// <summary>
    /// A refused request is counted as ASKED and as REFUSED, which is not one number.
    ///
    /// The return is load-bearing: ManagedVideoReceiver only starts waiting for a keyframe where
    /// the request went out, so a run full of refusals is a takion that could not send rather than
    /// a console that would not answer. Counting only what left would report zero for both, and
    /// counting only the attempt would report a request the console never saw.
    /// </summary>
    [Fact]
    public void ARefusedRequestIsBothAskedAndRefused()
    {
        var outbound = new StreamOutbound(new Sink(sends: false));

        Assert.False(outbound.SendIdrRequest());

        Assert.Equal(1, outbound.IdrRequests);
        Assert.Equal(1, outbound.IdrRequestsRefused);
    }

    /// <summary>
    /// And the run prints them, which is the half a counter nobody reads does not have.
    ///
    /// Read out of the source for PP808's reason: the report is written after a session that wants
    /// a console. What the check holds is that the run reaches the phase's own seam rather than a
    /// second one - an outbound built for the report would count a wire nothing sends on.
    /// </summary>
    [Fact]
    public void TheRunPrintsWhatTheSessionAskedFor()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\StreamRun.cs")
            ?? throw new InvalidOperationException("the run is not in this checkout."));

        Assert.Contains("phase.Outbound?.IdrRequests", text, StringComparison.Ordinal);
        Assert.Contains("phase.Outbound?.IdrRequestsRefused", text, StringComparison.Ordinal);

        // And the phase holds the one the receiver actually sends through.
        string phase = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\ManagedStreamPhase.cs")
            ?? throw new InvalidOperationException("the phase is not in this checkout."));

        Assert.Matches(@"(?m)^\s*Outbound = outbound;", phase);
    }
}
